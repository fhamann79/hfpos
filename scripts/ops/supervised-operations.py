#!/usr/bin/env python3
"""Human-controlled broker protocol. No shell commands or caller-selected remote paths."""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
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


def docker(*args, timeout=120):
    return native(["/usr/bin/docker", *args], timeout=timeout)


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
    require(not docker("exec", name, "ip", "-4", "route").strip())
    require(not docker("exec", name, "ip", "-6", "route").strip())


def probe_egress(name):
    # Same real, working client for positive loopback and negative reserved IPv4/IPv6.
    for address in ("127.0.0.1", "::1"):
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


def verify_core(name, workspace, manifest):
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
           "JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN ('r','p'));" )
    require(docker("exec", name, "psql", "-X", "-At", "-U", "hfpos_recovery_admin", "-d", "hfpos_recovery",
                   "-c", sql).strip() == b"t")
    expected = {}
    with tarfile.open(workspace / "incoming" / manifest["keyring"], mode="r:") as archive:
        for member in archive:
            if member.isfile():
                filename = member.name.removeprefix("./")
                expected[filename] = hashlib.sha256(archive.extractfile(member).read()).hexdigest()
    target = workspace / "keyring-restored"
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
        require(saved == {"labels": labels, "stage": "db-restored"} or
                saved == {"labels": labels, "stage": "core-restored-app-blocked"})
        inspect_isolation(name, labels)
        probe_egress(name)
        if saved["stage"] == "core-restored-app-blocked":
            verify_core(name, workspace, manifest)
            return
    else:
        require(not any((workspace / "postgres-data").iterdir()))
        require(not any((workspace / "keyring-restored").iterdir()))
        # Existing uncheckpointed containers are never removed or overwritten.
        existing = docker("ps", "-a", "--filter", "name=^/" + name + "$", "--format", "{{.ID}}")
        require(not existing.strip())
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
        docker(*args, PG_IMAGE, "postgres", "-c", "listen_addresses=127.0.0.1,::1")
        inspect_isolation(name, labels)
        ready = False
        for _ in range(30):
            try:
                docker("exec", name, "pg_isready", "-U", "hfpos_recovery_admin", "-d", "hfpos_recovery")
                ready = True
                break
            except Denied:
                time.sleep(1)
        require(ready)
        probe_egress(name)
        docker("exec", name, "mkdir", "-p", "/tmp/ops")
        for script in ("postgres-restore.sh", "verify-postgres-backup.sh", "verify-restored-db.sh"):
            docker("cp", str(bundle / script), name + ":/tmp/ops/" + script)
            docker("exec", "--user", "0", name, "chmod", "0444", "/tmp/ops/" + script)
        dump = manifest["dump"]
        for suffix in ("", ".sha256", ".manifest"):
            original = workspace / "incoming" / (dump + suffix)
            docker("cp", str(original), name + ":/tmp/" + original.name)
            docker("exec", "--user", "0", name, "chmod", "0444", "/tmp/" + original.name)
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
        state_file.write_bytes(canonical({"labels": labels, "stage": "db-restored"}))
        state_file.chmod(0o600)
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
    state_file.write_bytes(canonical({"labels": labels, "stage": "core-restored-app-blocked"}))
    verify_core(name, workspace, manifest)
    # Deliberately no backend/web start, source mount, published port or cleanup action.


def execute_envelope(envelope, config, state=STATE, recovery=RECOVERY, bundle=BUNDLE, payload=b""):
    require(isinstance(envelope, dict) and set(envelope) == {"plan", "signature"})
    plan = validate_plan(envelope["plan"])
    require(set(config) == {"source_sha", "bundle_sha256"})
    require(plan["source_sha"] == config["source_sha"] and plan["bundle_sha256"] == config["bundle_sha256"])
    require(digest_bundle(bundle) == config["bundle_sha256"])
    verify_signature(plan, envelope["signature"], state / "allowed_signers", state)
    import fcntl
    safe_path(state, mode=0o700)
    for name in ("consumed", "audit"):
        safe_path(state / name, mode=0o700)
    with (state / "lock").open("a+b") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        # Mark before effects. An interrupted/failed run cannot be re-executed.
        for identity in ("run-" + plan["run_id"], "nonce-" + plan["nonce"]):
            marker = state / "consumed" / identity
            with marker.open("xb") as output:
                output.write(b"consumed\n")
            marker.chmod(0o600)
        status = "FAILED"
        try:
            workspace = checkpoint(plan, recovery)
            if plan["action"] == "preflight":
                status = "PREFLIGHT_PASS"
            else:
                receive_transfer(plan, payload, workspace)
                restore_core(plan, workspace, bundle)
                status = "CORE_RESTORE_PASS_APP_BLOCKED"
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
            envelope = decode(sys.stdin.buffer.readline(16385))
            plan = validate_plan(envelope["plan"])
            payload = sys.stdin.buffer.read(plan["transfer_size"] + 1)
            require(len(payload) == plan["transfer_size"])
            result = execute_envelope(envelope, config, payload=payload)
            sys.stdout.buffer.write(canonical(result))
            return 0 if result["status"] in ("PREFLIGHT_PASS", "CORE_RESTORE_PASS_APP_BLOCKED") else 1
    except Exception:
        # No exception text, input, native stderr, file paths or data in public output.
        sys.stdout.buffer.write(b'{"status":"DENIED","version":1}\n')
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
