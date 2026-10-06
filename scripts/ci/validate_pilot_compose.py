#!/usr/bin/env python3
"""Check optional pilot config, without starting services or exposing secrets."""

import json
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
import re
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[2]
PG16 = re.compile(r"(?:docker\.io/library/)?postgres:16(?:\.[0-9]+)?(?:-[a-z0-9.-]+)?@sha256:[a-f0-9]{64}")


def absolute(path):
    return PurePosixPath(path).is_absolute() or PureWindowsPath(path).is_absolute()


def storage_path(value):
    # Lexical Linux paths only: Compose can normalize away unsafe dot components.
    path = PurePosixPath(value)
    if (not path.is_absolute() or value.startswith("//") or value != str(path) or
            any(part in (".", "..") for part in value.split("/")) or "\\" in value):
        raise ValueError("noncanonical storage path")
    broad = ("/", "/home", "/root", "/opt", "/srv", "/tmp", "/var", "/var/lib",
             "/var/log", "/mnt", "/media")
    system = ("/bin", "/boot", "/dev", "/etc", "/lib", "/lib64", "/proc", "/run",
              "/sbin", "/sys", "/usr", "/var/lib/docker", "/var/lib/containerd")
    if str(path) in broad or any(path.is_relative_to(parent) for parent in system):
        raise ValueError("system storage path")
    repo = PurePosixPath(ROOT.as_posix())
    if repo.is_absolute() and (path.is_relative_to(repo) or repo.is_relative_to(path)):
        raise ValueError("repository storage overlap")
    return path


def validate_config(config, cohost=True):
    errors = []
    services = config.get("services", {})
    connections = [services.get(name, {}).get("environment", {}).get("ConnectionStrings__DefaultConnection")
                   for name in ("backend", "migrations")]
    if not all(connections) or connections[0] == connections[1]:
        errors.append("Runtime and migrator connections must be present and distinct.")
    migration = services.get("migrations", {})
    if migration.get("profiles") != ["migration"] or migration.get("environment", {}).get("HFPOS_MIGRATION_APPROVED") != "NO":
        errors.append("Migrations must remain opt-in and unapproved by default.")
    if not cohost:
        if "postgres" in services:
            errors.append("External pilot must not create PostgreSQL.")
        return errors
    pg = services.get("postgres", {})
    if not PG16.fullmatch(pg.get("image", "")):
        errors.append("Require an explicit PostgreSQL 16 tag with sha256 digest.")
    if pg.get("ports") or set(pg.get("networks", {})) != {"database"}:
        errors.append("PostgreSQL must have no host ports and only the database network.")
    if not config.get("networks", {}).get("database", {}).get("internal"):
        errors.append("Database network must be internal.")
    if "database" in services.get("web", {}).get("networks", {}):
        errors.append("Web must not join the database network.")
    volumes = [volume for volume in pg.get("volumes", []) if volume.get("target") == "/var/lib/postgresql/data"]
    try:
        storage = config.get("x-hfpos-pg-storage", {})
        root = storage_path(storage.get("root", ""))
        data = storage_path(storage.get("data", ""))
        if (data == root or not data.is_relative_to(root) or len(volumes) != 1 or
                volumes[0].get("source") != str(data) or volumes[0].get("type") != "bind" or
                volumes[0].get("bind", {}).get("create_host_path", False)):
            raise ValueError("storage contract")
    except (ValueError, TypeError):
        errors.append("Require a dedicated canonical PGDATA leaf below the explicit approved storage root, without automatic creation.")
    environment = pg.get("environment", {})
    if (not environment.get("POSTGRES_USER") or not environment.get("POSTGRES_DB") or
            environment.get("POSTGRES_PASSWORD_FILE") != "/run/secrets/hfpos_pg_admin_password" or
            environment.get("POSTGRES_HOST_AUTH_METHOD") != "scram-sha-256" or "POSTGRES_PASSWORD" in environment):
        errors.append("Require external admin password file and SCRAM, no inline bootstrap password.")
    secret = config.get("secrets", {}).get("hfpos_pg_admin_password", {})
    if (not absolute(secret.get("file", "")) or
            Path(secret.get("file", "")).resolve().is_relative_to(ROOT) or
            not any(item.get("source") == "hfpos_pg_admin_password" for item in pg.get("secrets", []))):
        errors.append("Require absolute external admin password file.")
    for name in ("backend", "migrations"):
        service = services.get(name, {})
        if ("database" not in service.get("networks", {}) or
                service.get("depends_on", {}).get("postgres", {}).get("condition") != "service_healthy"):
            errors.append("Backend and migrations require PostgreSQL health and database network.")
    return errors


def main(arguments):
    external = len(arguments) == 2 and arguments[0] == "--external"
    if not external and len(arguments) != 1:
        print("Usage: validate_pilot_compose.py [--external] EXTERNAL_ENV_FILE", file=sys.stderr)
        return 1
    overlay = "migrator" if external else "postgres"
    context = "desktop-linux" if os.name == "nt" else "default"
    env = {key: value for key, value in os.environ.items()
           if not key.upper().startswith(("HFPOS_", "COMPOSE_"))}
    result = subprocess.run(["docker", "--context", context, "compose", "--env-file", arguments[-1],
                             "-f", str(ROOT / "deploy/compose/compose.production.example.yml"),
                             "-f", str(ROOT / f"deploy/compose/compose.production.{overlay}.example.yml"),
                             "--profile", "migration", "config", "--format", "json"],
                            env=env, capture_output=True, text=True)
    if result.returncode:
        print("Pilot Compose refused: missing/invalid external configuration; output suppressed.", file=sys.stderr)
        return 1
    errors = validate_config(json.loads(result.stdout), cohost=not external)
    for error in errors:
        print(error, file=sys.stderr)
    if errors:
        return 1
    print("PILOT COMPOSE CONFIG PASS (not image, role privileges, TLS or deployment validation)")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except (OSError, ValueError, KeyError, TypeError):
        print("Pilot Compose validation failed; output suppressed.", file=sys.stderr)
        raise SystemExit(1)
