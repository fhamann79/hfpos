#!/usr/bin/env python3
"""Materialize production references using synthetic config only; never compose up."""

import json
import copy
import os
from pathlib import Path
from unittest.mock import patch
import subprocess
import sys
import tempfile
import unittest

from validate_pilot_compose import storage_path, validate_config


ROOT = Path(__file__).resolve().parents[2]
BASE = ROOT / "deploy/compose/compose.production.example.yml"
OVERLAY = ROOT / "deploy/compose/compose.production.postgres.example.yml"
MIGRATOR = ROOT / "deploy/compose/compose.production.migrator.example.yml"
BASE_SHA = "d9939e1c4f2d62dfc071f68b344d4cc33ec099ef"
VALUES = {
    "HFPOS_BACKEND_IMAGE": "synthetic/backend@sha256:" + "1" * 64,
    "HFPOS_WEB_IMAGE": "synthetic/web@sha256:" + "2" * 64,
    "HFPOS_MIGRATIONS_IMAGE": "synthetic/migrations@sha256:" + "3" * 64,
    "HFPOS_RELEASE_VERSION": "v0.1.0-rc.1",
    "HFPOS_DATABASE_CONNECTION": "Host=postgres;Database=synthetic;Username=synthetic_runtime;Password=Synthetic-only-runtime",
    "HFPOS_MIGRATION_DATABASE_CONNECTION": "Host=postgres;Database=synthetic;Username=synthetic_migrator;Password=Synthetic-only-migrator",
    "HFPOS_JWT_KEY": "Synthetic-only-531-configuration-not-a-real-secret",
    "HFPOS_JWT_ISSUER": "synthetic-only", "HFPOS_JWT_AUDIENCE": "synthetic-only",
    "HFPOS_ALLOWED_HOSTS": "pilot.example.invalid", "HFPOS_HEALTH_HOST": "pilot.example.invalid",
    "HFPOS_KEYRING_PATH": "/synthetic-only/keyring", "HFPOS_TLS_PATH": "/synthetic-only/tls",
    "HFPOS_PG_IMAGE": "postgres:16-alpine@sha256:" + "4" * 64,
    "HFPOS_PG_DATA_PATH": "/srv/hf-one/synthetic-dev531/storage/postgres",
    "HFPOS_PG_STORAGE_ROOT": "/srv/hf-one/synthetic-dev531/storage",
    "HFPOS_PG_ADMIN_PASSWORD_FILE": "/synthetic-only/external-admin-password",
    "HFPOS_PG_ADMIN_USER": "synthetic_admin", "HFPOS_PG_DATABASE": "synthetic",
}


def materialize(values, topology="cohost"):
    env = {key: value for key, value in os.environ.items()
           if not key.startswith(("HFPOS_", "COMPOSE_"))}
    env.update(values)
    context = "desktop-linux" if os.name == "nt" else "default"
    command = ["docker", "--context", context, "compose", "--project-name", "hfpos-dev531-config-only",
               "--env-file", str(ROOT / "deploy/compose/.env.production.example"), "-f", str(BASE)]
    if topology == "cohost":
        command += ["--env-file", str(ROOT / "deploy/compose/.env.production.postgres.example"), "-f", str(OVERLAY)]
    elif topology == "external":
        command += ["--env-file", str(ROOT / "deploy/compose/.env.production.migrator.example"), "-f", str(MIGRATOR)]
    result = subprocess.run(command + ["--profile", "migration", "config", "--format", "json"],
                            env=env, capture_output=True, text=True)
    # Never expose config or native stderr: both can contain secret values.
    return result.returncode, json.loads(result.stdout) if result.returncode == 0 else None


class PilotComposeTests(unittest.TestCase):
    def test_legacy_base_and_smoke_fixture_are_unchanged(self):
        paths = ("deploy/compose/compose.production.example.yml", "deploy/compose/.env.production.example",
                 "scripts/ops/start-deployment-smoke.ps1")
        diff = subprocess.run(["git", "diff", "--quiet", BASE_SHA, "--", *paths],
                              cwd=ROOT, capture_output=True)
        self.assertEqual(0, diff.returncode, "Legacy base/env/smoke fixture changed from approved main.")
        legacy = dict(VALUES)
        legacy.pop("HFPOS_MIGRATION_DATABASE_CONNECTION")
        code, base = materialize(legacy, topology="legacy")
        self.assertEqual(0, code, "Base config failed (native output suppressed).")
        self.assertNotIn("postgres", base["services"])
        self.assertTrue(base["services"]["backend"]["environment"]["ConnectionStrings__DefaultConnection"] ==
                        base["services"]["migrations"]["environment"]["ConnectionStrings__DefaultConnection"],
                        "Legacy contract changed; this is not target-pilot role proof.")

    def test_external_pilot_has_separate_required_migrator_without_pg(self):
        code, config = materialize(VALUES, topology="external")
        self.assertEqual(0, code, "External pilot config failed (native output suppressed).")
        self.assertNotIn("postgres", config["services"])
        self.assertEqual([], validate_config(config, cohost=False))
        code, _ = materialize(dict(VALUES, HFPOS_MIGRATION_DATABASE_CONNECTION=""), topology="external")
        self.assertNotEqual(0, code)
        code, config = materialize(dict(VALUES, HFPOS_MIGRATION_DATABASE_CONNECTION=VALUES["HFPOS_DATABASE_CONNECTION"]), topology="external")
        self.assertEqual(0, code)
        self.assertTrue(validate_config(config, cohost=False))

    def test_cohost_overlay_separates_configured_roles(self):
        code, config = materialize(VALUES)
        self.assertEqual(0, code, "Overlay config failed (native output suppressed).")
        self.assertEqual([], validate_config(config))
        self.assertIn("bind: {create_host_path: false}", OVERLAY.read_text(encoding="utf-8"))
        self.assertEqual("synthetic_admin", config["services"]["postgres"]["environment"]["POSTGRES_USER"])
        self.assertTrue("Username=synthetic_runtime;" in config["services"]["backend"]["environment"]["ConnectionStrings__DefaultConnection"])
        self.assertTrue("Username=synthetic_migrator;" in config["services"]["migrations"]["environment"]["ConnectionStrings__DefaultConnection"])

    def test_required_pg_configuration_and_secrets_fail_closed(self):
        for key in ("HFPOS_PG_IMAGE", "HFPOS_PG_DATA_PATH", "HFPOS_PG_ADMIN_PASSWORD_FILE",
                    "HFPOS_PG_ADMIN_USER", "HFPOS_PG_DATABASE", "HFPOS_MIGRATION_DATABASE_CONNECTION"):
            with self.subTest(variable=key):
                values = dict(VALUES, **{key: ""})
                code, _ = materialize(values)
                self.assertNotEqual(0, code)

    def test_unpinned_or_wrong_major_images_and_same_connections_refused(self):
        for image in ("postgres:16", "postgres:latest@sha256:" + "4" * 64,
                      "postgres:17@sha256:" + "4" * 64):
            with self.subTest(image=image):
                code, config = materialize(dict(VALUES, HFPOS_PG_IMAGE=image))
                self.assertEqual(0, code)
                self.assertTrue(validate_config(config))
        code, config = materialize(dict(VALUES, HFPOS_MIGRATION_DATABASE_CONNECTION=VALUES["HFPOS_DATABASE_CONNECTION"]))
        self.assertEqual(0, code)
        self.assertTrue(validate_config(config))

    def test_private_network_storage_migration_and_lifecycle_contracts(self):
        code, config = materialize(VALUES)
        self.assertEqual(0, code)
        self.assertEqual([], validate_config(config))
        for name in ("postgres", "backend", "web"):
            self.assertEqual("unless-stopped", config["services"][name]["restart"])
            self.assertEqual({"max-size": "10m", "max-file": "3"}, config["services"][name]["logging"]["options"])
        self.assertEqual("no", config["services"]["migrations"]["restart"])
        for mutation in ("ports", "web-network", "storage", "health", "auto-migration", "password", "secret-in-repo"):
            with self.subTest(guard=mutation):
                bad = copy.deepcopy(config)
                if mutation == "ports":
                    bad["services"]["postgres"]["ports"] = [{"published": "5432", "target": 5432}]
                elif mutation == "web-network":
                    bad["services"]["web"]["networks"]["database"] = {}
                elif mutation == "storage":
                    bad["services"]["postgres"]["volumes"][0].setdefault("bind", {})["create_host_path"] = True
                elif mutation == "health":
                    bad["services"]["backend"]["depends_on"]["postgres"]["condition"] = "service_started"
                elif mutation == "auto-migration":
                    bad["services"]["migrations"]["profiles"] = []
                elif mutation == "password":
                    bad["services"]["postgres"]["environment"]["POSTGRES_PASSWORD"] = "Synthetic-only-inline"
                else:
                    bad["secrets"]["hfpos_pg_admin_password"]["file"] = str(ROOT / "synthetic-never-created-password")
                self.assertTrue(validate_config(bad))

    def test_real_validator_cli_only_materializes_and_suppresses_sensitive_output(self):
        env = {key: value for key, value in os.environ.items()
               if not key.startswith(("HFPOS_", "COMPOSE_"))}
        helper = ROOT / "scripts/ci/validate_pilot_compose.py"
        with tempfile.TemporaryDirectory(prefix="hfpos-dev531-config-") as directory:
            path = Path(directory) / "synthetic.env"
            path.write_text("\n".join(f"{key}={value}" for key, value in VALUES.items()), encoding="utf-8")
            for options in ([], ["--external"]):
                result = subprocess.run([sys.executable, "-B", str(helper), *options, str(path)],
                                        env=env, capture_output=True, text=True)
                self.assertEqual(0, result.returncode, "Validator CLI failed (output suppressed).")
                self.assertIn("PILOT COMPOSE CONFIG PASS", result.stdout)
                self.assertNotIn("Synthetic-only", result.stdout + result.stderr)
            missing = dict(VALUES)
            missing.pop("HFPOS_MIGRATION_DATABASE_CONNECTION")
            path.write_text("\n".join(f"{key}={value}" for key, value in missing.items()), encoding="utf-8")
            result = subprocess.run([sys.executable, "-B", str(helper), "--external", str(path)],
                                    env=env, capture_output=True, text=True)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("output suppressed", result.stderr)
            self.assertNotIn("Synthetic-only", result.stdout + result.stderr)

    def test_storage_requires_a_dedicated_leaf_without_normalization_escape(self):
        code, config = materialize(VALUES)
        self.assertEqual(0, code)
        safe = VALUES["HFPOS_PG_STORAGE_ROOT"]
        for root, data in (("/", "/"), ("/etc", "/etc/postgres"),
                           ("/home", "/home"), (safe, safe),
                           (safe, "/srv/hf-one/other/postgres"),
                           (safe, safe + "/../other/postgres"),
                           (safe, safe + "/./postgres"),
                           (safe + "/.", safe + "/postgres"),
                           (safe, safe + "//postgres"),
                           ("/" + safe, "/" + safe + "/postgres"),
                           ("/usr", "/usr/postgres"), ("/var/lib/docker", "/var/lib/docker/postgres")):
            with self.subTest(root=root, data=data):
                bad = copy.deepcopy(config)
                bad["x-hfpos-pg-storage"] = {"root": root, "data": data}
                bad["services"]["postgres"]["volumes"][0]["source"] = data
                self.assertTrue(validate_config(bad), "Unsafe storage accepted (paths suppressed).")
        repo = "/srv/hf-one/synthetic-dev531/repo"
        for data in ("/srv/hf-one/synthetic-dev531", repo, repo + "/postgres"):
            bad = copy.deepcopy(config)
            bad["x-hfpos-pg-storage"] = {"root": "/srv/hf-one/synthetic-dev531", "data": data}
            bad["services"]["postgres"]["volumes"][0]["source"] = data
            with patch("validate_pilot_compose.ROOT", Path(repo)):
                self.assertEqual(Path("/srv/hf-one/unrelated/storage").as_posix(),
                                 str(storage_path("/srv/hf-one/unrelated/storage")))
                with self.assertRaises(ValueError):
                    storage_path(data)
                self.assertTrue(validate_config(bad))
        code, _ = materialize(dict(VALUES, HFPOS_PG_STORAGE_ROOT=""))
        self.assertNotEqual(0, code)
        for data in ("/", "/etc", "/home", safe + "/../escaped", safe + "/./postgres"):
            code, bad = materialize(dict(VALUES, HFPOS_PG_DATA_PATH=data))
            self.assertEqual(0, code, "Synthetic config failed (output suppressed).")
            self.assertTrue(validate_config(bad), "Compose normalization hid unsafe raw PGDATA.")

    def test_real_cli_ignores_ambient_overrides_and_cannot_fill_missing_fields(self):
        helper = ROOT / "scripts/ci/validate_pilot_compose.py"
        clean = {key: value for key, value in os.environ.items()
                 if not key.upper().startswith(("HFPOS_", "COMPOSE_"))}
        with tempfile.TemporaryDirectory(prefix="hfpos-dev531-ambient-") as directory:
            path = Path(directory) / "synthetic.env"
            hostile = dict(clean, HFPOS_PG_IMAGE="postgres:17", HFPOS_PG_DATA_PATH="/",
                           HFPOS_PG_STORAGE_ROOT="/", HFPOS_DATABASE_CONNECTION="Synthetic-only-ambient-shared",
                           HFPOS_MIGRATION_DATABASE_CONNECTION="Synthetic-only-ambient-shared",
                           COMPOSE_FILE="nonexistent-synthetic.yml", COMPOSE_ENV_FILES="missing-synthetic.env",
                           COMPOSE_PROFILES="stale", COMPOSE_PROJECT_NAME="INVALID PROJECT")
            for options in ([], ["--external"]):
                path.write_text("\n".join(f"{key}={value}" for key, value in VALUES.items()), encoding="utf-8")
                result = subprocess.run([sys.executable, "-B", str(helper), *options, str(path)],
                                        env=hostile, capture_output=True, text=True)
                self.assertEqual(0, result.returncode, "Ambient overrides affected CLI (output suppressed).")
                self.assertIn("PILOT COMPOSE CONFIG PASS", result.stdout)
                self.assertNotIn("Synthetic-only", result.stdout + result.stderr)
                required = ["HFPOS_MIGRATION_DATABASE_CONNECTION", "HFPOS_DATABASE_CONNECTION"]
                if not options:
                    required.append("HFPOS_PG_STORAGE_ROOT")
                for absent in required:
                    missing = {key: value for key, value in VALUES.items() if key != absent}
                    path.write_text("\n".join(f"{key}={value}" for key, value in missing.items()), encoding="utf-8")
                    ambient = dict(clean, **{absent: VALUES[absent]})
                    result = subprocess.run([sys.executable, "-B", str(helper), *options, str(path)],
                                            env=ambient, capture_output=True, text=True)
                    self.assertNotEqual(0, result.returncode, "Missing envfile field filled by shell.")
                    self.assertIn("output suppressed", result.stderr)
                    self.assertNotIn("Synthetic-only", result.stdout + result.stderr)

    def test_real_cli_rejects_storage_outside_dedicated_hf_one_namespaces(self):
        helper = ROOT / "scripts/ci/validate_pilot_compose.py"
        env = {key: value for key, value in os.environ.items()
               if not key.upper().startswith(("HFPOS_", "COMPOSE_"))}
        with tempfile.TemporaryDirectory(prefix="hfpos-dev531-namespace-") as directory:
            path = Path(directory) / "synthetic.env"
            for root, data in (("/var/cache", "/var/cache/apt"),
                               ("/var/spool", "/var/spool/mail"),
                               ("/var/backups", "/var/backups/postgres"),
                               ("/var/www", "/var/www/postgres"),
                               ("/home/arbitrary", "/home/arbitrary/postgres"),
                               ("/mnt/arbitrary", "/mnt/arbitrary/postgres"),
                               ("/opt/arbitrary", "/opt/arbitrary/postgres"),
                               ("/synthetic-only", "/synthetic-only/postgres"),
                               ("/srv/hf-one", "/srv/hf-one/postgres"),
                               ("/var/lib/hf-one", "/var/lib/hf-one/postgres"),
                               ("/srv/hf-one-other/pilot", "/srv/hf-one-other/pilot/postgres"),
                               ("/var/lib/hf-one2/pilot", "/var/lib/hf-one2/pilot/postgres"),
                               ("/srv/hf-one/pilot.name", "/srv/hf-one/pilot.name/postgres")):
                with self.subTest(root=root, data=data):
                    values = dict(VALUES, HFPOS_PG_STORAGE_ROOT=root, HFPOS_PG_DATA_PATH=data)
                    path.write_text("\n".join(f"{key}={value}" for key, value in values.items()), encoding="utf-8")
                    result = subprocess.run([sys.executable, "-B", str(helper), str(path)],
                                            env=env, capture_output=True, text=True)
                    self.assertNotEqual(0, result.returncode, "Unapproved storage namespace accepted (output suppressed).")
                    self.assertNotIn("Synthetic-only", result.stdout + result.stderr)
            for namespace in ("/srv/hf-one", "/var/lib/hf-one"):
                root = namespace + "/synthetic-dev531/storage"
                values = dict(VALUES, HFPOS_PG_STORAGE_ROOT=root, HFPOS_PG_DATA_PATH=root + "/postgres")
                path.write_text("\n".join(f"{key}={value}" for key, value in values.items()), encoding="utf-8")
                result = subprocess.run([sys.executable, "-B", str(helper), str(path)],
                                        env=env, capture_output=True, text=True)
                self.assertEqual(0, result.returncode, "Dedicated namespace CLI failed (output suppressed).")
                self.assertIn("PILOT COMPOSE CONFIG PASS", result.stdout)


if __name__ == "__main__":
    unittest.main()
