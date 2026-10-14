#!/usr/bin/env python3
"""One-shot HUMAN console recovery. No daemon, broker, app start or SQL resume."""
import argparse
import base64
from contextlib import contextmanager
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import stat
import subprocess
import sys
import tarfile

HERE = Path(__file__).resolve().parent
FILES = ("human-recovery.py", "human-recovery-windows.py", "human-recovery-transfer.py", "human-recovery-local.ps1", "supervised-operations.py", "supervised-archive.py",
         "supervised-local-acl.ps1", "postgres-restore.sh", "verify-postgres-backup.sh",
         "verify-restored-db.sh", "keyring-restore.sh")
IMAGE = "postgres:16.15-bookworm@sha256:0ea6700a3b4f0ae6ce746519073558aed4d88a79d8d07622a9a644946c7319c4"
RECOVERY = Path("/srv/hf-one/pilot/recovery-r4")
LIMIT = 128 * 1024 * 1024


def require(value):
    if not value:
        raise ValueError("STOP")


def root_path(path, directory=True, mode=None):
    path = Path(path)
    require(path.is_absolute())
    for item in (path, *path.parents):
        info = item.lstat()
        require(info.st_uid == 0 and not info.st_mode & 0o022 and not stat.S_ISLNK(info.st_mode))
    info = path.stat()
    require(stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode))
    require(mode is None or stat.S_IMODE(info.st_mode) == mode)
    return path


@contextmanager
def workspace_lock(target):
    import fcntl
    descriptor = os.open(target, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        require(os.fstat(descriptor).st_ino == target.stat().st_ino)
        yield
    finally:
        os.close(descriptor)


def load_helpers():
    # Check root-owned code BEFORE any sibling Python import, including in tests.
    if os.name == "posix" and os.geteuid() == 0:
        root_path(HERE, mode=0o700)
        for name in FILES:
            root_path(HERE / name, False, 0o600)
        root_path(HERE / "source.json", False, 0o600)
    def load(name, filename):
        spec = importlib.util.spec_from_file_location(name, HERE / filename)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module
    return load("human_ops", "supervised-operations.py"), load("human_archive", "supervised-archive.py")


def digest_code():
    digest = hashlib.sha256()
    for name in FILES:
        digest.update(name.encode() + b"\0" + hashlib.sha256((HERE / name).read_bytes().replace(b"\r\n", b"\n")).digest())
    return digest.hexdigest()


def native(args, data=None, timeout=120):
    env = {"PATH": "/usr/bin:/bin", "HOME": "/nonexistent", "LANG": "C"} if os.name == "posix" else None
    if os.name == "nt" and Path(args[0]).name.lower() == "powershell.exe":
        env = {key: value for key, value in os.environ.items() if key.lower() != "psmodulepath"}
    result = subprocess.run(args, input=data, capture_output=True, timeout=timeout, env=env)
    require(result.returncode == 0)
    return result.stdout


def docker(*args, data=None, timeout=120):
    return native(["/usr/bin/docker", "--host", "unix:///var/run/docker.sock", "--config",
                   str(HERE / "docker-empty"), *args], data, timeout)


def decode(data):
    require(len(data) <= 65536)
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result)
            result[key] = value
        return result
    return json.loads(data, object_pairs_hook=pairs)


def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode("ascii")


def workspace(recovery_id):
    require(re.fullmatch(r"[a-f0-9]{32}", recovery_id))
    target = root_path(RECOVERY / recovery_id, mode=0o700)
    for name in ("incoming", "postgres-data", "keyring-restored"):
        root_path(target / name, mode=0o700)
    return target


def image_check():
    item = decode(docker("image", "inspect", IMAGE))[0]
    require(item["Os"] == "linux" and item["Architecture"] == "amd64")
    require(IMAGE.split("@", 1)[1] in [value.split("@", 1)[-1] for value in item["RepoDigests"]])
    require(item["Config"]["Entrypoint"] == ["docker-entrypoint.sh"])
    require(item["Config"]["Cmd"] == ["postgres"])
    # No pull. Effective passwd/tool checks use only the already-local immutable image.
    output = docker("run", "--rm", "--pull=never", "--network", "none", "--read-only",
                    "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--user", "999:999",
                    "--entrypoint", "sh", IMAGE, "-c",
                    "test \"$(id -u)\" = 999 && test \"$(id -g)\" = 999 && "
                    "grep -q '^postgres:x:999:999:' /etc/passwd && "
                    "test -x /usr/local/bin/docker-entrypoint.sh && pg_restore --version", timeout=30)
    require(output.startswith(b"pg_restore (PostgreSQL) 16."))
    return item["Id"]


def preflight(args):
    require(os.name == "posix" and os.geteuid() == 0)
    require(re.fullmatch(r"[a-f0-9]{40}", args.source_sha))
    require(re.fullmatch(r"[a-f0-9]{64}", args.ciphertext_sha256))
    require(digest_code() == args.code_sha256)
    require(decode((HERE / "source.json").read_bytes()) == {"source_sha": args.source_sha, "code_sha256": args.code_sha256})
    root_path(HERE / "docker-empty", mode=0o700)
    require(not any((HERE / "docker-empty").iterdir()))
    target = workspace(args.recovery_id)
    require({p.name for p in target.iterdir()} == {"incoming", "postgres-data", "keyring-restored"})
    require(all(not any((target / name).iterdir()) for name in ("incoming", "postgres-data", "keyring-restored")))
    require(not docker("ps", "-a", "--filter", "name=^/hfpos-human-recovery-" + args.recovery_id + "$",
                       "--format", "{{.ID}}").strip())
    info = target.stat()
    return {"version": 1, "source_sha": args.source_sha, "code_sha256": args.code_sha256,
            "recovery_id": args.recovery_id, "ciphertext_sha256": args.ciphertext_sha256,
            "workspace_device": info.st_dev, "workspace_inode": info.st_ino, "image_id": image_check()}


def validate_envelope(value):
    require(set(value) == {"version", "source_sha", "code_sha256", "recovery_id", "ciphertext_sha256",
                           "workspace_device", "workspace_inode", "image_id"})
    require(type(value["version"]) is int and value["version"] == 1)
    for key, size in (("source_sha", 40), ("code_sha256", 64), ("recovery_id", 32), ("ciphertext_sha256", 64)):
        require(isinstance(value[key], str) and re.fullmatch("[a-f0-9]{%d}" % size, value[key]))
    require(re.fullmatch(r"sha256:[a-f0-9]{64}", value["image_id"]))
    require(all(type(value[key]) is int and value[key] >= 0 for key in ("workspace_device", "workspace_inode")))
    require(value["code_sha256"] == digest_code())
    return value


def transfer_plan(value, payload):
    require(0 < len(payload) <= LIMIT)
    files = {}
    with tarfile.open(fileobj=io.BytesIO(payload), mode="r:") as archive:
        for member in archive:
            require(member.type == tarfile.REGTYPE and not member.pax_headers)
            require(member.name == "transfer.json" or re.fullmatch(r"(?:db|keyring|metadata)/[A-Za-z0-9_-][A-Za-z0-9_.-]{0,119}", member.name))
            require(member.name not in files and len(files) < 128 and 0 <= member.size <= 64 * 1024 * 1024)
            files[member.name] = archive.extractfile(member).read()
    require("transfer.json" in files and sum(map(len, files.values())) <= LIMIT)
    manifest = decode(files["transfer.json"])
    require(set(manifest) == {"version", "recovery_id", "ciphertext_sha256", "files", "dump", "keyring"})
    require(type(manifest["version"]) is int and manifest["version"] == 1)
    require(manifest["recovery_id"] == value["recovery_id"] and manifest["ciphertext_sha256"] == value["ciphertext_sha256"])
    require(set(files) == set(manifest["files"]) | {"transfer.json"})
    require(all(hashlib.sha256(files[name]).hexdigest() == digest for name, digest in manifest["files"].items()))
    dumps = [n for n in files if n.startswith("db/") and n.endswith(".dump")]
    keys = [n for n in files if n.startswith("keyring/") and n.endswith(".tar")]
    require(dumps == [manifest["dump"]] and keys == [manifest["keyring"]])
    require(set(n for n in files if n.startswith("db/")) == {dumps[0], dumps[0] + ".sha256", dumps[0] + ".manifest"})
    require(set(n for n in files if n.startswith("keyring/")) == {keys[0], keys[0] + ".sha256"})
    for name in (*dumps, *keys):
        require(files[name + ".sha256"].decode("ascii").strip() == hashlib.sha256(files[name]).hexdigest() + "  " + Path(name).name)
    return {**value, "transfer_size": len(payload), "transfer_sha256": hashlib.sha256(payload).hexdigest(),
            "transfer_manifest_sha256": hashlib.sha256(canonical(manifest)).hexdigest()}, manifest


def inspect_manual(name, labels):
    item = decode(docker("inspect", name))[0]
    host = item["HostConfig"]
    require(host["NetworkMode"] == "none" and not host["Privileged"] and host["ReadonlyRootfs"])
    require(not host["PortBindings"] and not host.get("Binds") and not host.get("VolumesFrom"))
    require(host["CapDrop"] == ["ALL"] and "no-new-privileges" in host["SecurityOpt"])
    require(host["Memory"] == 805306368 and host["MemorySwap"] == 805306368)
    require(host["NanoCpus"] == 500000000 and host["PidsLimit"] == 128)
    require(not host.get("PidMode") and not host.get("Devices"))
    require(item["Config"]["Image"] == IMAGE and item["Config"]["User"] == "999:999")
    require(all(item["Config"]["Labels"].get(k) == v for k, v in labels.items()))
    mounts = item["Mounts"]
    required_tmpfs = {"/var/lib/postgresql/data": "rw,noexec,nosuid,size=1g,uid=999,gid=999,mode=0700",
                      "/var/run/postgresql": "rw,noexec,nosuid,size=16m,uid=999,gid=999",
                      "/tmp": "rw,noexec,nosuid,size=256m,mode=1777"}
    require(host.get("Tmpfs") == required_tmpfs)
    require(all(m["Type"] == "tmpfs" and m["Destination"] in required_tmpfs for m in mounts))
    mountinfo = docker("exec", name, "cat", "/proc/self/mountinfo").decode("ascii").splitlines()
    for destination in required_tmpfs:
        resolved = docker("exec", name, "readlink", "-f", destination).decode("ascii").strip()
        require(resolved in (destination, "/run/postgresql") and
                any(line.split()[4] == resolved and line.split(" - ", 1)[1].split()[0] == "tmpfs" for line in mountinfo))
    require(docker("exec", name, "ls", "/sys/class/net").strip() == b"lo")
    routes = docker("exec", name, "cat", "/proc/net/route").decode("ascii").splitlines()[1:]
    require(all(line.split()[0] == "lo" for line in routes))
    return item["Id"]


def bind_helpers(ops):
    ops.docker = docker
    # probe_egress's negative subprocess must also use the fixed socket/config/env.
    def probes(name):
        mode = ops.ipv6_mode(name)
        for address in (("127.0.0.1", "::1") if mode == "operational-loopback" else ("127.0.0.1",)):
            require(docker("exec", "-e", "PGCONNECT_TIMEOUT=2", name, "psql", "-X", "-At", "-h", address,
                           "-U", "hfpos_recovery_admin", "-d", "hfpos_recovery", "-c", "SELECT 1;").strip() == b"1")
        for address in ("192.0.2.1", "2001:db8::1"):
            command = ["/usr/bin/docker", "--host", "unix:///var/run/docker.sock", "--config", str(HERE / "docker-empty"),
                       "exec", "-e", "PGCONNECT_TIMEOUT=2", name, "psql", "-X", "-At", "-h", address,
                       "-U", "hfpos_recovery_admin", "-d", "hfpos_recovery", "-c", "SELECT 1;"]
            result = subprocess.run(command, capture_output=True, timeout=10,
                                    env={"PATH": "/usr/bin:/bin", "HOME": "/nonexistent", "LANG": "C"})
            require(result.returncode == 2)
        return mode
    return probes


def restore(value, payload, ops):
    target = workspace(value["recovery_id"])
    with workspace_lock(target):
        restore_locked(value, payload, ops)


def verify_identity(name, labels, container_id, image_id):
    require(inspect_manual(name, labels) == container_id)
    require(decode(docker("inspect", name))[0]["Image"] == image_id)


def verify_roles(name):
    require(docker("exec", name, "psql", "-X", "-At", "-U", "hfpos_recovery_runtime", "-d", "hfpos_recovery", "-c",
                   "SELECT rolcanlogin AND NOT rolsuper AND NOT rolcreatedb AND NOT rolcreaterole "
                   "AND NOT rolreplication AND NOT rolbypassrls AND "
                   "NOT has_schema_privilege(current_user,'public','CREATE') "
                   "FROM pg_roles WHERE rolname=current_user;").strip() == b"t")


def verify_completed(args, ops):
    target = workspace(args.recovery_id)
    with workspace_lock(target):
        saved = decode(root_path(target / "human-checkpoint.json", False, 0o600).read_bytes())
        require(saved["stage"] == "core-restored-app-blocked")
        labels = saved["labels"]
        require(labels["hfpos.human.code"] == digest_code())
        source = decode((HERE / "source.json").read_bytes())
        require(labels["hfpos.human.source"] == source["source_sha"] and source["code_sha256"] == digest_code())
        name = "hfpos-human-recovery-" + args.recovery_id
        manifest = decode(root_path(target / "incoming/transfer.json", False, 0o600).read_bytes())
        require(hashlib.sha256(canonical(manifest)).hexdigest() == labels["hfpos.human.import"])
        ops.validate_import(target, {"recovery_id": args.recovery_id, "ciphertext_sha256": manifest["ciphertext_sha256"],
                            "transfer_manifest_sha256": labels["hfpos.human.import"]})
        image_id = decode(docker("image", "inspect", IMAGE))[0]["Id"]
        verify_identity(name, labels, saved["container_id"], image_id)
        bind_helpers(ops)(name)
        verify_roles(name)
        ops.verify_core(name, target, manifest)
        verify_identity(name, labels, saved["container_id"], image_id)


def restore_locked(value, payload, ops):
    require(os.name == "posix" and os.geteuid() == 0)
    validate_envelope(value)
    args = argparse.Namespace(**value)
    require(preflight(args) == value)  # Recheck empty destinations, source, image, identity.
    target = workspace(value["recovery_id"])
    plan, manifest = transfer_plan(value, payload)
    state = target / "human-checkpoint.json"
    labels = {"hfpos.human.source": value["source_sha"], "hfpos.human.code": value["code_sha256"],
              "hfpos.human.import": plan["transfer_manifest_sha256"]}
    # Intention is durable before the first remote write. ANY subsequent attempt stops.
    ops.write_checkpoint(state, labels, "import-started")
    ops.receive_transfer(plan, payload, target)
    name = "hfpos-human-recovery-" + value["recovery_id"]
    ops.write_checkpoint(state, labels, "container-create-started")
    command = ["run", "-d", "--pull=never", "--name", name, "--network", "none", "--read-only",
               "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--user", "999:999",
               "--memory", "768m", "--memory-swap", "768m", "--cpus", "0.5", "--pids-limit", "128",
               "--tmpfs", "/var/lib/postgresql/data:rw,noexec,nosuid,size=1g,uid=999,gid=999,mode=0700",
               "--tmpfs", "/var/run/postgresql:rw,noexec,nosuid,size=16m,uid=999,gid=999",
               "--tmpfs", "/tmp:rw,noexec,nosuid,size=256m,mode=1777",
               "-e", "POSTGRES_USER=hfpos_recovery_admin", "-e", "POSTGRES_DB=hfpos_recovery",
               "-e", "POSTGRES_HOST_AUTH_METHOD=trust"]
    for key, val in labels.items():
        command += ["--label", key + "=" + val]
    docker(*command, IMAGE, "postgres", "-c", "listen_addresses=127.0.0.1,::1")
    container_id = inspect_manual(name, labels)
    require(decode(docker("inspect", name))[0]["Image"] == value["image_id"])
    ops.write_checkpoint(state, labels, "container-created", container_id)
    ops.wait_postgres_tcp(name, "hfpos_recovery_admin", "hfpos_recovery")
    probes = bind_helpers(ops)
    probes(name)
    docker("exec", "--user", "0", name, "mkdir", "-p", "/tmp/ops")
    files = {"ops/" + s: (HERE / s).read_bytes().replace(b"\r\n", b"\n") for s in
             ("postgres-restore.sh", "verify-postgres-backup.sh", "verify-restored-db.sh")}
    for suffix in ("", ".sha256", ".manifest"):
        file = target / "incoming" / (manifest["dump"] + suffix)
        files[file.name] = file.read_bytes()
    ops.stream_container_files(name, files)
    verify_identity(name, labels, container_id, value["image_id"])
    probes(name)
    verify_identity(name, labels, container_id, value["image_id"])
    ops.write_checkpoint(state, labels, "restore-started", container_id)
    sql = ("CREATE ROLE hfpos_recovery_owner LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;"
           "CREATE ROLE hfpos_recovery_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;"
           "ALTER DATABASE hfpos_recovery OWNER TO hfpos_recovery_owner;"
           "ALTER SCHEMA public OWNER TO hfpos_recovery_owner;")
    docker("exec", name, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "hfpos_recovery_admin", "-d", "hfpos_recovery", "-c", sql)
    docker("exec", "-e", "PGUSER=hfpos_recovery_owner", "-e", "PGDATABASE=hfpos_recovery",
           "-e", "HFPOS_RESTORE_APPROVED=YES", name, "sh", "/tmp/ops/postgres-restore.sh",
           "/tmp/" + Path(manifest["dump"]).name, timeout=600)
    grants = ("REVOKE CREATE ON SCHEMA public FROM PUBLIC;GRANT USAGE ON SCHEMA public TO hfpos_recovery_runtime;"
              "GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA public TO hfpos_recovery_runtime;"
              "GRANT USAGE,SELECT ON ALL SEQUENCES IN SCHEMA public TO hfpos_recovery_runtime;"
              "ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO hfpos_recovery_runtime;"
              "ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE,SELECT ON SEQUENCES TO hfpos_recovery_runtime;")
    docker("exec", name, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "hfpos_recovery_owner", "-d", "hfpos_recovery", "-c", grants)
    verify_roles(name)
    ops.write_checkpoint(state, labels, "keyring-started", container_id)
    result = subprocess.run(["/bin/sh", str(HERE / "keyring-restore.sh"), str(target / "incoming" / manifest["keyring"]),
                             str(target / "keyring-restored")], capture_output=True, timeout=30,
                            env={"PATH": "/usr/bin:/bin", "HFPOS_RESTORE_APPROVED": "YES"})
    require(result.returncode == 0)
    for file in (target / "keyring-restored").iterdir():
        root_path(file, False)
        file.chmod(0o600)
    ops.verify_core(name, target, manifest)
    verify_identity(name, labels, container_id, value["image_id"])
    probes(name)
    verify_roles(name)
    verify_identity(name, labels, container_id, value["image_id"])
    ops.write_checkpoint(state, labels, "core-restored-app-blocked", container_id)


def main(argv=None):
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="action", required=True)
    seal = sub.add_parser("seal")
    seal.add_argument("--source-sha", required=True)
    seal.add_argument("--output", required=True)
    pre = sub.add_parser("preflight")
    for field in ("source-sha", "code-sha256", "recovery-id", "ciphertext-sha256"):
        pre.add_argument("--" + field, required=True)
    run = sub.add_parser("restore")
    run.add_argument("--spool", required=True)
    run.add_argument("--payload-sha256", required=True)
    verify = sub.add_parser("verify")
    verify.add_argument("--recovery-id", required=True)
    args = parser.parse_args(argv)
    try:
        if args.action == "seal":
            require(re.fullmatch(r"[a-f0-9]{40}", args.source_sha))
            repo = HERE.parents[1]
            require(native(["git", "-C", str(repo), "rev-parse", "HEAD"]).strip().decode() == args.source_sha)
            for name in FILES:
                require(native(["git", "-C", str(repo), "show", args.source_sha + ":scripts/ops/" + name]).replace(b"\r\n", b"\n") ==
                        (HERE / name).read_bytes().replace(b"\r\n", b"\n"))
            output = Path(args.output)
            output.mkdir(mode=0o700)
            for name in FILES:
                (output / name).write_bytes((HERE / name).read_bytes().replace(b"\r\n", b"\n"))
            (output / "source.json").write_bytes(canonical({"source_sha": args.source_sha, "code_sha256": digest_code()}))
            (output / "docker-empty").mkdir(mode=0o700)
            print("CODE_SHA256=" + digest_code())
            return 0
        ops, _ = load_helpers()
        ops.docker = docker
        if args.action == "preflight":
            value = preflight(args)
            print("PREFLIGHT_PASS " + base64.b64encode(canonical(value)).decode("ascii"))
        elif args.action == "verify":
            require(os.name == "posix" and os.geteuid() == 0)
            verify_completed(args, ops)
            print("CORE_VERIFY_PASS_APP_BLOCKED")
        else:
            require(os.name == "posix" and os.geteuid() == 0)
            spool = Path(args.spool)
            require(spool.is_absolute() and re.fullmatch(r"human-recovery-[a-f0-9]{32}", spool.name))
            info = spool.lstat()
            require(stat.S_ISDIR(info.st_mode) and stat.S_IMODE(info.st_mode) == 0o700)
            for path in (spool, *spool.parents):
                info = path.lstat()
                require(not stat.S_ISLNK(info.st_mode) and not info.st_mode & 0o022 and info.st_uid in (0, spool.stat().st_uid))
            require({p.name for p in spool.iterdir()} == {"preflight.json", "payload.tar", "receipt.json"})
            for file in spool.iterdir():
                info = file.lstat()
                require(stat.S_ISREG(info.st_mode) and stat.S_IMODE(info.st_mode) == 0o600 and info.st_uid == spool.stat().st_uid)
                require(file.stat().st_size <= LIMIT)
            value = validate_envelope(decode((spool / "preflight.json").read_bytes()))
            payload = (spool / "payload.tar").read_bytes()
            require(re.fullmatch(r"[a-f0-9]{64}", args.payload_sha256) and hashlib.sha256(payload).hexdigest() == args.payload_sha256)
            require(decode((spool / "receipt.json").read_bytes()) == {"payload_sha256": hashlib.sha256(payload).hexdigest(),
                     "envelope_sha256": hashlib.sha256(canonical(value)).hexdigest()})
            transfer_plan(value, payload)
            print("SOURCE=" + value["source_sha"] + " CODE=" + value["code_sha256"] + " APPBLOCKED")
            require(input("Escriba RESTAURAR " + value["recovery_id"] + ": ") == "RESTAURAR " + value["recovery_id"])
            restore(value, payload, ops)
            print("CORE_RESTORE_PASS_APP_BLOCKED")
        return 0
    except (Exception, KeyboardInterrupt):
        print("STOP_APP_BLOCKED_NO_RETRY")
        return 1


if __name__ == "__main__":
    sys.exit(main())
