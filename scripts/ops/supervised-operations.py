#!/usr/bin/env python3
"""Human-controlled broker protocol. No shell commands or caller-selected remote paths."""
import argparse
import hashlib
import io
import ipaddress
import json
import os
from pathlib import Path
import re
import select
import stat
import subprocess
import sys
import tarfile
import tempfile
import time

REPOSITORY = "fhamann79/hfpos"
NAMESPACE = "hf-one-r4"
BUNDLE = Path("/opt/hfpos-supervised/bundle")
STATE = Path("/var/lib/hfpos-supervised")
RECOVERY = Path("/srv/hf-one/pilot/recovery-r4")
PG_IMAGE = "postgres:16-alpine@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea"
BUNDLE_FILES = ("supervised-operations.py", "supervised-archive.py", "supervised-bridge.py",
                "supervised-local-acl.ps1",
                "supervised-launcher.ps1",
                "supervised-ssh.sh", "supervised-root.sh", "postgres-restore.sh",
                "verify-postgres-backup.sh", "verify-restored-db.sh", "keyring-restore.sh")
REQUEST_KEYS = {"version", "repository", "action", "source_sha", "bundle_sha256", "ciphertext_sha256",
                "recovery_id", "run_id", "run_attempt"}
PLAN_KEYS = REQUEST_KEYS | {"expires", "nonce", "transfer_size", "transfer_sha256", "transfer_manifest_sha256"}
STATUSES = {"PREFLIGHT_PASS", "CORE_RESTORE_PASS_APP_BLOCKED", "DENIED", "FAILED", "UNCERTAIN"}


class Denied(Exception):
    pass


class TransferAborted(Exception):
    pass


def read_payload(stream, size, timeout=60):
    deadline = time.monotonic() + timeout
    output = io.BytesIO()
    try:
        descriptor = stream.fileno()
    except (AttributeError, io.UnsupportedOperation):
        descriptor = None
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise TransferAborted()
        count = min(65536, size - output.tell() + 1)
        if descriptor is None:
            chunk = stream.read(count)
        else:
            if not select.select([descriptor], [], [], remaining)[0]:
                raise TransferAborted()
            chunk = os.read(descriptor, count)
        if not chunk:
            break
        output.write(chunk)
        if output.tell() > size:
            raise TransferAborted()
    if output.tell() != size:
        raise TransferAborted()
    return output.getvalue()


def require(ok):
    if not ok:
        raise Denied()


def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True) + "\n").encode("ascii")


def pairs(items):
    result = {}
    for key, value in items:
        require(key not in result)
        result[key] = value
    return result


def decode(data, limit=16384):
    require(len(data) <= limit)
    return json.loads(data, object_pairs_hook=pairs)


def hex_value(value, length):
    return isinstance(value, str) and re.fullmatch("[a-f0-9]{%d}" % length, value) is not None


def validate_request(request):
    require(isinstance(request, dict) and set(request) == REQUEST_KEYS)
    require(type(request["version"]) is int and request["version"] == 1)
    require(request["repository"] == REPOSITORY)
    require(request["action"] in ("preflight", "isolated-restore"))
    require(hex_value(request["source_sha"], 40) and hex_value(request["bundle_sha256"], 64))
    require(hex_value(request["ciphertext_sha256"], 64))
    require(hex_value(request["recovery_id"], 32))
    require(isinstance(request["run_id"], str) and re.fullmatch("[1-9][0-9]{0,19}", request["run_id"]))
    require(type(request["run_attempt"]) is int and request["run_attempt"] == 1)
    return request


def validate_plan(plan, now=None):
    require(isinstance(plan, dict) and set(plan) == PLAN_KEYS)
    validate_request({key: plan[key] for key in REQUEST_KEYS})
    require(hex_value(plan["nonce"], 32))
    now = int(time.time()) if now is None else now
    require(type(plan["expires"]) is int and now < plan["expires"] <= now + 600)
    require(type(plan["transfer_size"]) is int and 0 <= plan["transfer_size"] <= 128 * 1024 * 1024)
    require(hex_value(plan["transfer_sha256"], 64) and hex_value(plan["transfer_manifest_sha256"], 64))
    require((plan["transfer_size"] == 0) == (plan["action"] == "preflight"))
    return plan


def bundle_content(directory):
    return {name: (directory / name).read_bytes().replace(b"\r\n", b"\n") for name in BUNDLE_FILES}


def digest_content(contents):
    digest = hashlib.sha256()
    for name in BUNDLE_FILES:
        content = contents[name]
        digest.update(name.encode("ascii") + b"\0" + hashlib.sha256(content).digest())
    return digest.hexdigest()


def digest_bundle(directory):
    return digest_content(bundle_content(directory))


def safe_path(path, directory=True, mode=None, owner=0):
    path = Path(path)
    require(path.is_absolute())
    for ancestor in reversed((path, *path.parents)):
        info = ancestor.lstat()
        require(not stat.S_ISLNK(info.st_mode))
        require(info.st_uid == owner and not info.st_mode & 0o022)
    info = path.lstat()
    require(stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode))
    if mode is not None:
        require(stat.S_IMODE(info.st_mode) == mode)
    return path


def private_dir(path):
    path.mkdir(mode=0o700, exist_ok=True)
    return safe_path(path, mode=0o700)


def native(args, data=None, timeout=120):
    # Never propagate stdout/stderr from native tools to public evidence.
    env = None
    if Path(args[0]).name.lower() == "powershell.exe":
        env = {key: value for key, value in os.environ.items() if key.lower() != "psmodulepath"}
    result = subprocess.run(args, input=data, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, timeout=timeout, check=False, env=env)
    require(result.returncode == 0)
    return result.stdout


def verify_signature(plan, signature, signers, temporary):
    require(isinstance(signature, str) and len(signature) <= 4096)
    require(signature.startswith("-----BEGIN SSH SIGNATURE-----\n") and
            signature.endswith("-----END SSH SIGNATURE-----\n"))
    with tempfile.TemporaryDirectory(dir=temporary) as directory:
        signature_file = Path(directory) / "signature"
        signature_file.write_text(signature, encoding="ascii")
        signature_file.chmod(0o600)
        native(["ssh-keygen", "-Y", "verify", "-f", str(signers), "-I", "fernando",
                "-n", NAMESPACE, "-s", str(signature_file)], canonical(plan), 15)


def report(request, status):
    require(status in STATUSES)
    return {"version": 1, "repository": REPOSITORY, "run_id": request["run_id"],
            "run_attempt": 1, "action": request["action"], "source_sha": request["source_sha"],
            "bundle_sha256": request["bundle_sha256"], "recovery_id": request["recovery_id"],
            "ciphertext_sha256": request["ciphertext_sha256"],
            "status": status, "app_started": False}


def checkpoint(plan, recovery=RECOVERY):
    workspace = safe_path(recovery / plan["recovery_id"], mode=0o700)
    for name in ("incoming", "postgres-data", "keyring-restored"):
        safe_path(workspace / name, mode=0o700)
    return workspace


def validate_import(workspace, plan):
    incoming = workspace / "incoming"
    manifest_path = safe_path(incoming / "transfer.json", directory=False, mode=0o600)
    manifest = decode(manifest_path.read_bytes(), 65536)
    require(hashlib.sha256(canonical(manifest)).hexdigest() == plan["transfer_manifest_sha256"])
    require(set(manifest) == {"version", "recovery_id", "ciphertext_sha256", "files", "dump", "keyring"})
    require(manifest["version"] == 1 and manifest["recovery_id"] == plan["recovery_id"])
    require(manifest["ciphertext_sha256"] == plan["ciphertext_sha256"])
    require(isinstance(manifest["files"], dict) and 5 <= len(manifest["files"]) <= 128)
    for name, expected in manifest["files"].items():
        require(re.fullmatch(r"(?:db|keyring|metadata)/[A-Za-z0-9_-][A-Za-z0-9_.-]{0,119}", name))
        require(hex_value(expected, 64))
        file = safe_path(incoming / name, directory=False, mode=0o600)
        require(file.stat().st_size <= 64 * 1024 * 1024)
        require(hashlib.sha256(file.read_bytes()).hexdigest() == expected)
    dump, keys = manifest["dump"], manifest["keyring"]
    require(isinstance(dump, str) and re.fullmatch(r"db/hfpos-[0-9]{8}T[0-9]{6}Z-[0-9]+\.dump", dump))
    require(isinstance(keys, str) and re.fullmatch(r"keyring/keyring-[0-9]{8}T[0-9]{6}Z-[0-9]+\.tar", keys))
    require(all(name in manifest["files"] for name in (dump, dump + ".sha256", dump + ".manifest", keys, keys + ".sha256")))
    # Recheck the nested archive on the host before the official keyring extractor.
    import importlib.util
    spec = importlib.util.spec_from_file_location("supervised_archive", Path(__file__).with_name("supervised-archive.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.validate_keyring((incoming / keys).read_bytes())
    return manifest


def receive_transfer(plan, payload, workspace):
    require(len(payload) == plan["transfer_size"] and hashlib.sha256(payload).hexdigest() == plan["transfer_sha256"])
    files = {}
    total = 0
    with tarfile.open(fileobj=io.BytesIO(payload), mode="r:") as archive:
        for member in archive:
            require(member.isfile() and not member.pax_headers and member.type == tarfile.REGTYPE)
            name = member.name
            require(name == "transfer.json" or re.fullmatch(r"(?:db|keyring|metadata)/[A-Za-z0-9_-][A-Za-z0-9_.-]{0,119}", name))
            require(name not in files and len(files) < 128 and 0 <= member.size <= 64 * 1024 * 1024)
            total += member.size
            require(total <= 128 * 1024 * 1024)
            files[name] = archive.extractfile(member).read()
    require("transfer.json" in files)
    manifest = decode(files["transfer.json"], 65536)
    require(hashlib.sha256(canonical(manifest)).hexdigest() == plan["transfer_manifest_sha256"])
    require(manifest["recovery_id"] == plan["recovery_id"] and manifest["ciphertext_sha256"] == plan["ciphertext_sha256"])
    require(set(files) == set(manifest["files"]) | {"transfer.json"})
    require(all(hex_value(value, 64) and hashlib.sha256(files[name]).hexdigest() == value
                for name, value in manifest["files"].items()))
    incoming = workspace / "incoming"
    existing = {file.relative_to(incoming).as_posix() for file in incoming.rglob("*") if not file.is_dir()}
    require(existing <= set(files))
    # Never overwrite. Existing matching root-only checkpoints support safe resume.
    for name, data in files.items():
        destination = incoming / name
        private_dir(destination.parent)
        if destination.exists() or destination.is_symlink():
            safe_path(destination, directory=False, mode=0o600)
            require(destination.read_bytes() == data)
        else:
            with destination.open("xb") as output:
                output.write(data)
            destination.chmod(0o600)
    validate_import(workspace, plan)


def docker(*args, timeout=120, data=None):
    return native(["/usr/bin/docker", *args], data=data, timeout=timeout)


def wait_postgres_tcp(name, user, database):
    # The image's temporary init server accepts sockets, but never TCP.
    for _ in range(30):
        try:
            docker("exec", name, "pg_isready", "-h", "127.0.0.1", "-p", "5432", "-t", "1",
                   "-U", user, "-d", database, timeout=5)
            return
        except Denied:
            time.sleep(1)
    raise Denied()


def stream_container_files(name, files):
    output = io.BytesIO()
    scripts = {"ops/postgres-restore.sh", "ops/verify-postgres-backup.sh", "ops/verify-restored-db.sh"}
    with tarfile.open(fileobj=output, mode="w", format=tarfile.USTAR_FORMAT) as archive:
        for filename, content in files.items():
            require(filename in scripts or re.fullmatch(r"hfpos-[0-9]{8}T[0-9]{6}Z-[0-9]+\.dump(?:\.sha256|\.manifest)?", filename))
            require(len(content) <= 64 * 1024 * 1024)
            member = tarfile.TarInfo(filename)
            member.mode = 0o444
            member.size = len(content)
            archive.addfile(member, io.BytesIO(content))
    docker("exec", "-i", "--user", "0", name, "tar", "-xf", "-", "-C", "/tmp", "--no-same-owner", data=output.getvalue())
    for filename, content in files.items():
        actual = docker("exec", name, "sha256sum", "/tmp/" + filename).split()[0]
        require(actual.decode("ascii") == hashlib.sha256(content).hexdigest())


def loopback_routes(data):
    for line in data.decode("ascii").splitlines():
        fields = line.split()
        require(fields and ipaddress.ip_network(fields[0], strict=False).is_loopback)
        require("dev" in fields and fields[fields.index("dev") + 1] == "lo")


def inspect_isolation(name, labels):
    data = decode(docker("inspect", name), 131072)
    require(isinstance(data, list) and len(data) == 1)
    item = data[0]
    host = item["HostConfig"]
    require(host["NetworkMode"] == "none" and not host["Privileged"])
    require(host["ReadonlyRootfs"] and not host["PortBindings"])
    require(host["CapDrop"] == ["ALL"] and "no-new-privileges" in host["SecurityOpt"])
    require(host["Memory"] == 805306368 and host["MemorySwap"] == 805306368)
    require(host["NanoCpus"] == 500000000 and host["PidsLimit"] == 128)
    require(not host.get("Binds") and not host.get("VolumesFrom"))
    require(not item["Mounts"] or all(m["Type"] == "tmpfs" for m in item["Mounts"]))
    require(item["Config"]["Image"] == PG_IMAGE and item["Config"]["User"] == "70:70")
    require(all(item["Config"]["Labels"].get(key) == value for key, value in labels.items()))
    require(not host.get("PidMode") and not host.get("Devices"))
    require(docker("exec", name, "ls", "/sys/class/net").strip() == b"lo")
    loopback_routes(docker("exec", name, "ip", "-4", "route"))
    if ipv6_mode(name) != "kernel-disabled":
        loopback_routes(docker("exec", name, "ip", "-6", "route"))
    return item["Id"]


def ipv6_mode(name):
    # Docker none may disable IPv6 on lo, or leave the stack enabled without ::1.
    # Neither is an operational IPv6 loopback that can satisfy a positive PG probe.
    present = docker("exec", name, "sh", "-c", "if test -d /proc/sys/net/ipv6; then echo yes; else echo no; fi").strip()
    require(present in (b"yes", b"no"))
    if present == b"no":
        docker("exec", name, "test", "!", "-e", "/proc/net/if_inet6")
        return "kernel-disabled"
    flags = [docker("exec", name, "cat", "/proc/sys/net/ipv6/conf/" + interface + "/disable_ipv6").strip()
             for interface in ("all", "lo")]
    require(all(flag in (b"0", b"1") for flag in flags))
    addresses = docker("exec", name, "cat", "/proc/net/if_inet6").decode("ascii").splitlines()
    for line in addresses:
        fields = line.split()
        require(len(fields) == 6 and fields[-1] == "lo" and fields[0] == "0" * 31 + "1")
    if b"1" in flags:
        require(not addresses)
        return "namespace-disabled"
    return "operational-loopback" if addresses else "unconfigured-loopback"


def probe_egress(name):
    # Same real, working client for positive loopback and negative reserved IPv4/IPv6.
    mode = ipv6_mode(name)
    for address in (("127.0.0.1", "::1") if mode == "operational-loopback" else ("127.0.0.1",)):
        output = docker("exec", "-e", "PGCONNECT_TIMEOUT=2", name, "psql", "-X", "-At",
                        "-h", address, "-p", "5432", "-U", "hfpos_recovery_admin", "-d", "hfpos_recovery",
                        "-c", "SELECT 1;")
        require(output.strip() == b"1")
    for address in ("192.0.2.1", "2001:db8::1"):
        result = subprocess.run(["/usr/bin/docker", "exec", "-e", "PGCONNECT_TIMEOUT=2", name,
                                 "psql", "-X", "-At", "-h", address, "-p", "5432", "-U",
                                 "hfpos_recovery_admin", "-d", "hfpos_recovery", "-c", "SELECT 1;"],
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10)
        require(result.returncode == 2)
    return mode


def write_checkpoint(path, labels, stage, container_id=None):
    if path.exists():
        safe_path(path, directory=False, mode=0o600)
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".core-checkpoint-", delete=False) as output:
        temporary = Path(output.name)
        try:
            output.write(canonical({"labels": labels, "stage": stage, "container_id": container_id}))
            output.flush()
            os.fsync(output.fileno())
            temporary.chmod(0o600)
            os.replace(temporary, path)
        finally:
            if temporary.exists():
                temporary.unlink()
    descriptor = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def verify_core(name, workspace, manifest):
    target = safe_path(workspace / "keyring-restored", mode=0o700)
    docker("exec", "-e", "PGUSER=hfpos_recovery_owner", "-e", "PGDATABASE=hfpos_recovery",
           name, "sh", "/tmp/ops/verify-restored-db.sh")
    sql = ("SELECT (SELECT count(*)=2 AND bool_and(NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole "
           "AND NOT rolreplication) FROM pg_roles WHERE rolname IN ('hfpos_recovery_owner','hfpos_recovery_runtime')) "
           "AND NOT has_schema_privilege('hfpos_recovery_runtime','public','CREATE') "
           "AND (SELECT bool_and(has_table_privilege('hfpos_recovery_runtime',c.oid,'SELECT') "
           "AND has_table_privilege('hfpos_recovery_runtime',c.oid,'INSERT') "
           "AND has_table_privilege('hfpos_recovery_runtime',c.oid,'UPDATE') "
           "AND has_table_privilege('hfpos_recovery_runtime',c.oid,'DELETE') "
           "AND pg_get_userbyid(c.relowner)='hfpos_recovery_owner') FROM pg_class c "
           "JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','p')) "
           "AND (SELECT COALESCE(bool_and(has_sequence_privilege('hfpos_recovery_runtime',c.oid,'USAGE') "
           "AND has_sequence_privilege('hfpos_recovery_runtime',c.oid,'SELECT') "
           "AND NOT has_sequence_privilege('hfpos_recovery_runtime',c.oid,'UPDATE') "
           "AND pg_get_userbyid(c.relowner)='hfpos_recovery_owner'),true) FROM pg_class c "
           "JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='S') "
           "AND (SELECT count(*)=4 AND bool_and(x.privilege_type IN ('SELECT','INSERT','UPDATE','DELETE')) "
           "AND NOT bool_or(x.is_grantable) FROM pg_default_acl a "
           "CROSS JOIN LATERAL aclexplode(a.defaclacl) x WHERE a.defaclrole='hfpos_recovery_owner'::regrole "
           "AND a.defaclnamespace='public'::regnamespace AND a.defaclobjtype='r' "
           "AND x.grantee='hfpos_recovery_runtime'::regrole) "
           "AND (SELECT count(*)=2 AND bool_and(x.privilege_type IN ('USAGE','SELECT')) "
           "AND NOT bool_or(x.is_grantable) FROM pg_default_acl a "
           "CROSS JOIN LATERAL aclexplode(a.defaclacl) x WHERE a.defaclrole='hfpos_recovery_owner'::regrole "
           "AND a.defaclnamespace='public'::regnamespace AND a.defaclobjtype='S' "
           "AND x.grantee='hfpos_recovery_runtime'::regrole);" )
    require(docker("exec", name, "psql", "-X", "-At", "-U", "hfpos_recovery_admin", "-d", "hfpos_recovery",
                   "-c", sql).strip() == b"t")
    expected = {}
    with tarfile.open(workspace / "incoming" / manifest["keyring"], mode="r:") as archive:
        for member in archive:
            if member.isfile():
                filename = member.name.removeprefix("./")
                expected[filename] = hashlib.sha256(archive.extractfile(member).read()).hexdigest()
    require({file.name for file in target.iterdir()} == set(expected))
    for filename, value in expected.items():
        file = safe_path(target / filename, directory=False, mode=0o600)
        require(hashlib.sha256(file.read_bytes()).hexdigest() == value)


def restore_core(plan, workspace, bundle):
    manifest = validate_import(workspace, plan)
    name = "hfpos-recovery-" + plan["recovery_id"]
    labels = {"hfpos.supervised.source": plan["source_sha"],
              "hfpos.supervised.bundle": plan["bundle_sha256"],
              "hfpos.supervised.import": hashlib.sha256(canonical(manifest)).hexdigest()}
    state_file = workspace / "core-checkpoint.json"
    if state_file.exists():
        safe_path(state_file, directory=False, mode=0o600)
        saved = decode(state_file.read_bytes())
        require(set(saved) == {"labels", "stage", "container_id"} and saved["labels"] == labels)
        stage = saved["stage"]
        require(stage in ("container-prepared", "container-created", "db-restored", "core-restored-app-blocked"))
        require((stage == "container-prepared" and saved["container_id"] is None) or hex_value(saved["container_id"], 64))
    else:
        require(not any((workspace / "postgres-data").iterdir()))
        require(not any((workspace / "keyring-restored").iterdir()))
        # Existing uncheckpointed containers are never removed or overwritten.
        existing = docker("ps", "-a", "--filter", "name=^/" + name + "$", "--format", "{{.ID}}")
        require(not existing.strip())
        write_checkpoint(state_file, labels, "container-prepared")
        stage = "container-prepared"
    if stage == "container-prepared":
        existing = docker("ps", "-a", "--filter", "name=^/" + name + "$", "--format", "{{.ID}}")
        args = ["run", "-d", "--name", name, "--network", "none", "--read-only",
                "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--user", "70:70",
                "--memory", "768m", "--memory-swap", "768m", "--cpus", "0.5", "--pids-limit", "128",
                "--tmpfs", "/var/lib/postgresql/data:rw,noexec,nosuid,size=1g,uid=70,gid=70,mode=0700",
                "--tmpfs", "/var/run/postgresql:rw,noexec,nosuid,size=16m,uid=70,gid=70",
                "--tmpfs", "/tmp:rw,noexec,nosuid,size=256m,mode=1777",
                "-e", "POSTGRES_USER=hfpos_recovery_admin", "-e", "POSTGRES_DB=hfpos_recovery",
                "-e", "POSTGRES_HOST_AUTH_METHOD=trust"]
        for key, value in labels.items():
            args += ["--label", key + "=" + value]
        if not existing.strip():
            docker(*args, PG_IMAGE, "postgres", "-c", "listen_addresses=127.0.0.1,::1")
        container_id = inspect_isolation(name, labels)
        write_checkpoint(state_file, labels, "container-created", container_id)
        stage = "container-created"
    else:
        container_id = inspect_isolation(name, labels)
        require(container_id == saved["container_id"])
    if stage == "container-created":
        wait_postgres_tcp(name, "hfpos_recovery_admin", "hfpos_recovery")
        probe_egress(name)
        docker("exec", "--user", "0", name, "mkdir", "-p", "/tmp/ops")
        files = {"ops/" + script: (bundle / script).read_bytes() for script in
                 ("postgres-restore.sh", "verify-postgres-backup.sh", "verify-restored-db.sh")}
        dump = manifest["dump"]
        for suffix in ("", ".sha256", ".manifest"):
            original = workspace / "incoming" / (dump + suffix)
            files[original.name] = original.read_bytes()
        stream_container_files(name, files)
        # After this checkpoint, automatic resume must NOT repeat SQL/pg_restore.
        write_checkpoint(state_file, labels, "restore-started", container_id)
        # pg_restore runs as a non-superuser owning only the isolated recovery DB.
        sql = ("CREATE ROLE hfpos_recovery_owner LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION; "
               "CREATE ROLE hfpos_recovery_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION; "
               "ALTER DATABASE hfpos_recovery OWNER TO hfpos_recovery_owner; "
               "ALTER SCHEMA public OWNER TO hfpos_recovery_owner;")
        docker("exec", name, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "hfpos_recovery_admin",
               "-d", "hfpos_recovery", "-c", sql)
        docker("exec", "-e", "PGUSER=hfpos_recovery_owner", "-e", "PGDATABASE=hfpos_recovery",
               "-e", "HFPOS_RESTORE_APPROVED=YES", name, "sh", "/tmp/ops/postgres-restore.sh",
               "/tmp/" + Path(dump).name, timeout=600)
        grants = ("REVOKE CREATE ON SCHEMA public FROM PUBLIC; "
                  "GRANT USAGE ON SCHEMA public TO hfpos_recovery_runtime; "
                  "GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO hfpos_recovery_runtime; "
                  "GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO hfpos_recovery_runtime; "
                  "ALTER DEFAULT PRIVILEGES FOR ROLE hfpos_recovery_owner IN SCHEMA public "
                  "GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO hfpos_recovery_runtime; "
                  "ALTER DEFAULT PRIVILEGES FOR ROLE hfpos_recovery_owner IN SCHEMA public "
                  "GRANT USAGE,SELECT ON SEQUENCES TO hfpos_recovery_runtime;")
        docker("exec", name, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "hfpos_recovery_owner",
               "-d", "hfpos_recovery", "-c", grants)
        privileges = docker("exec", name, "psql", "-X", "-At", "-U", "hfpos_recovery_runtime",
                            "-d", "hfpos_recovery", "-c",
                            "SELECT NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication "
                            "AND NOT has_schema_privilege(current_user,'public','CREATE') "
                            "FROM pg_roles WHERE rolname=current_user;")
        require(privileges.strip() == b"t")
        write_checkpoint(state_file, labels, "db-restored", container_id)
    else:
        probe_egress(name)
        if stage == "core-restored-app-blocked":
            verify_core(name, workspace, manifest)
            return
    require(not any((workspace / "keyring-restored").iterdir()))
    env = {"PATH": "/usr/bin:/bin", "HFPOS_RESTORE_APPROVED": "YES"}
    result = subprocess.run(["/bin/sh", str(bundle / "keyring-restore.sh"),
                             str(workspace / "incoming" / manifest["keyring"]),
                             str(workspace / "keyring-restored")], env=env,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30)
    require(result.returncode == 0)
    for file in (workspace / "keyring-restored").iterdir():
        require(file.is_file() and not file.is_symlink())
        file.chmod(0o600)
    verify_core(name, workspace, manifest)
    write_checkpoint(state_file, labels, "core-restored-app-blocked", container_id)
    # Deliberately no backend/web start, source mount, published port or cleanup action.


def execute_envelope(envelope, config, state=STATE, recovery=RECOVERY, bundle=BUNDLE, payload=b"", payload_stream=None):
    require(isinstance(envelope, dict) and set(envelope) == {"plan", "signature"})
    plan = validate_plan(envelope["plan"])
    require(set(config) == {"source_sha", "bundle_sha256"})
    require(plan["source_sha"] == config["source_sha"] and plan["bundle_sha256"] == config["bundle_sha256"])
    require(digest_bundle(bundle) == config["bundle_sha256"])
    import fcntl
    safe_path(state, mode=0o700)
    for name in ("consumed", "audit"):
        safe_path(state / name, mode=0o700)
    with (state / "lock").open("a+b") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        verify_signature(plan, envelope["signature"], state / "allowed_signers", state)
        markers = [state / "consumed" / identity for identity in
                   ("run-" + plan["run_id"], "nonce-" + plan["nonce"])]
        if any(marker.exists() for marker in markers):
            raise FileExistsError()
        # Authentication, replay and exclusive lock precede all blob reads. Persist
        # consumption before receiving, including interrupted/slow transfers.
        for marker in markers:
            with marker.open("xb") as output:
                output.write(b"consumed\n")
                output.flush()
                os.fsync(output.fileno())
            marker.chmod(0o600)
        descriptor = os.open(state / "consumed", os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(descriptor)
        finally:
            os.close(descriptor)
        status = "FAILED"
        try:
            if payload_stream is not None:
                payload = read_payload(payload_stream, plan["transfer_size"])
            elif len(payload) != plan["transfer_size"]:
                raise TransferAborted()
            workspace = checkpoint(plan, recovery)
            if plan["action"] == "preflight":
                status = "PREFLIGHT_PASS"
            else:
                receive_transfer(plan, payload, workspace)
                restore_core(plan, workspace, bundle)
                status = "CORE_RESTORE_PASS_APP_BLOCKED"
        except TransferAborted:
            status = "UNCERTAIN"
        except (Denied, OSError, ValueError, subprocess.SubprocessError):
            pass
        result = report(plan, status)
        audit = state / "audit" / (plan["run_id"] + ".json")
        with audit.open("xb") as output:
            output.write(canonical(result))
        audit.chmod(0o600)
        return result


def bootstrap(args):
    require(os.name == "posix" and os.geteuid() == 0)
    require(hex_value(args.source_sha, 40) and hex_value(args.bundle_sha256, 64))
    source = Path(__file__).parent
    reviewed = bundle_content(source)
    require(digest_content(reviewed) == args.bundle_sha256)
    require(not BUNDLE.exists() and not STATE.exists())
    public = Path(args.signer_public).read_text(encoding="ascii").strip()
    transport = Path(args.transport_public).read_text(encoding="ascii").strip()
    require(re.fullmatch(r"(?:ssh-ed25519|sk-ssh-ed25519@openssh.com) [A-Za-z0-9+/=]+(?: [A-Za-z0-9_.@-]+)?", public))
    require(re.fullmatch(r"ssh-ed25519 [A-Za-z0-9+/=]+(?: [A-Za-z0-9_.@-]+)?", transport))
    require(public.split()[:2] != transport.split()[:2])
    require('ID=ubuntu' in Path('/etc/os-release').read_text().splitlines())
    ssh_config = Path("/etc/ssh/sshd_config.d/90-hfpos-supervised.conf")
    sudo_config = Path("/etc/sudoers.d/hfpos-supervised")
    authorization = Path("/etc/hfpos-supervised/authorized_keys")
    require(all(not path.exists() and not path.is_symlink() for path in (ssh_config, sudo_config, authorization)))
    account = subprocess.run(["/usr/bin/id", "hfpos-supervised"], capture_output=True, check=False)
    require(account.returncode == 1)
    private_dir(BUNDLE.parent)
    private_dir(BUNDLE)
    for name in BUNDLE_FILES:
        file = BUNDLE / name
        file.write_bytes(reviewed[name])
        file.chmod(0o600)
    private_dir(STATE)
    private_dir(STATE / "consumed")
    private_dir(STATE / "audit")
    (STATE / "allowed_signers").write_text('fernando namespaces="' + NAMESPACE + '" ' + public + "\n", encoding="ascii")
    (STATE / "config.json").write_bytes(canonical({"source_sha": args.source_sha, "bundle_sha256": args.bundle_sha256}))
    for name in ("allowed_signers", "config.json"):
        (STATE / name).chmod(0o600)
    for source_name, destination in (
            ("supervised-ssh.sh", Path("/usr/local/libexec/hfpos-supervised-ssh")),
            ("supervised-root.sh", Path("/usr/local/sbin/hfpos-supervised-root"))):
        require(not destination.exists() and not destination.is_symlink())
        destination.parent.mkdir(mode=0o755, exist_ok=True)
        safe_path(destination.parent)
        destination.write_bytes((BUNDLE / source_name).read_bytes())
        destination.chmod(0o755)
    native(["/usr/sbin/useradd", "--system", "--no-create-home", "--home-dir", "/nonexistent",
            "--shell", "/bin/sh", "hfpos-supervised"])
    native(["/usr/sbin/usermod", "--password", "*", "hfpos-supervised"])
    authorization.parent.mkdir(mode=0o755)
    safe_path(authorization.parent, mode=0o755)
    authorization.write_text('restrict,command="/usr/local/libexec/hfpos-supervised-ssh" ' + transport + "\n", encoding="ascii")
    authorization.chmod(0o644)
    # Public keys are readable by the dedicated account, never writable by it.
    sudo_config.write_text('hfpos-supervised ALL=(root) NOPASSWD: /usr/local/sbin/hfpos-supervised-root ""\n', encoding="ascii")
    sudo_config.chmod(0o440)
    ssh_config.write_text("Match User hfpos-supervised\n"
                          "    AuthorizedKeysFile /etc/hfpos-supervised/authorized_keys\n"
                          "    ForceCommand /usr/local/libexec/hfpos-supervised-ssh\n"
                          "    AuthenticationMethods publickey\n    PasswordAuthentication no\n"
                          "    KbdInteractiveAuthentication no\n    DisableForwarding yes\n"
                          "    PermitTTY no\n    PermitTunnel no\n    PermitUserRC no\nMatch all\n", encoding="ascii")
    ssh_config.chmod(0o600)
    native(["/usr/sbin/visudo", "-cf", str(sudo_config)])
    native(["/usr/sbin/sshd", "-t"])
    effective = native(["/usr/sbin/sshd", "-T", "-C", "user=hfpos-supervised,host=localhost,addr=127.0.0.1"]).decode()
    for line in ("forcecommand /usr/local/libexec/hfpos-supervised-ssh", "disableforwarding yes", "permittty no",
                 "passwordauthentication no", "kbdinteractiveauthentication no", "permituserrc no", "permittunnel no",
                 "authorizedkeysfile /etc/hfpos-supervised/authorized_keys", "authenticationmethods publickey", "pubkeyauthentication yes"):
        require(line in effective.splitlines())
    native(["/usr/bin/systemctl", "reload", "ssh"])
    print("BOOTSTRAP_PASS_HUMAN_HOST_CONNECTIVITY_VALIDATION_PENDING")


def main():
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("digest")
    commands.add_parser("serve")
    boot = commands.add_parser("bootstrap")
    for name in ("source-sha", "bundle-sha256", "signer-public", "transport-public"):
        boot.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        if args.command == "digest":
            print(digest_bundle(Path(__file__).parent))
        elif args.command == "bootstrap":
            bootstrap(args)
        else:
            os.umask(0o077)
            require(os.name == "posix" and os.geteuid() == 0)
            safe_path(BUNDLE, mode=0o700)
            for name in BUNDLE_FILES:
                safe_path(BUNDLE / name, directory=False, mode=0o600)
            for source_name, installed in (("supervised-ssh.sh", "/usr/local/libexec/hfpos-supervised-ssh"),
                                           ("supervised-root.sh", "/usr/local/sbin/hfpos-supervised-root")):
                require(safe_path(Path(installed), directory=False, mode=0o755).read_bytes() == (BUNDLE / source_name).read_bytes())
            safe_path(STATE / "allowed_signers", directory=False, mode=0o600)
            config = decode(safe_path(STATE / "config.json", directory=False, mode=0o600).read_bytes())
            # Unbuffered header avoids read-ahead into the unauthenticated blob.
            stream = sys.stdin.buffer.raw
            envelope = decode(stream.readline(16385))
            result = execute_envelope(envelope, config, payload_stream=stream)
            sys.stdout.buffer.write(canonical(result))
            return 0 if result["status"] in ("PREFLIGHT_PASS", "CORE_RESTORE_PASS_APP_BLOCKED") else 1
    except Exception:
        # No exception text, input, native stderr, file paths or data in public output.
        sys.stdout.buffer.write(b'{"status":"DENIED","version":1}\n')
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
