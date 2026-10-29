#!/usr/bin/env python3
"""Proposed auto-v2 online capture. Human installation only; no restore or deployment."""
import argparse
from contextlib import contextmanager
import datetime as dt
import hashlib
import importlib.util
import io
import ipaddress
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import threading
import time
from types import SimpleNamespace
import uuid

HERE = Path(__file__).resolve().parent
MAX_FILE, MAX_TOTAL = 64 * 1024**2, 128 * 1024**2
ENV = {"PATH": "/usr/bin:/bin", "HOME": "/nonexistent", "LC_ALL": "C", "TZ": "UTC"}
HELPER_HASHES = {"postgres-backup.sh": "7ab165df036531b3a0cb6fee4ad0855c50d757628aa1217804c098aaa85e136c",
                 "verify-postgres-backup.sh": "4820dcd9e1574e7ef3ab9ce57115f6ee29f0b1cbce3394460b4e2168ac759199"}
LOCK = Path("/run/lock/hf-one-manual-backup.lock")


class BackupError(Exception):
    pass


def require(value, code="CONTRACT"):
    if not value:
        raise BackupError(code)


def unique(items):
    result = {}
    for key, value in items:
        require(key not in result)
        result[key] = value
    return result


def digest(data):
    return hashlib.sha256(data).hexdigest()


def file_digest(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(65536), b""):
            result.update(chunk)
    return result.hexdigest()


def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()


def secure_path(path, directory=False, private=False, source_root=None, source_owners=frozenset({0})):
    path = Path(path)
    require(path.is_absolute() and path == path.resolve() and ".." not in path.parts, "PATH")
    for item in (path, *path.parents):
        info = item.lstat()
        sticky_lock_parent = item == Path("/run/lock") and stat.S_ISDIR(info.st_mode) and info.st_mode & stat.S_ISVTX
        owners = source_owners if source_root is not None and (item == source_root or source_root in item.parents) else {0}
        require(info.st_uid in owners and not stat.S_ISLNK(info.st_mode) and (not info.st_mode & 0o022 or sticky_lock_parent), "OWNER")
        if source_root is not None and (item == source_root or source_root in item.parents):
            require(info.st_gid in source_owners, "SOURCE_GROUP")
    info = path.stat()
    require(stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode), "TYPE")
    require(directory or info.st_nlink == 1, "HARDLINK")
    require(not private or stat.S_IMODE(info.st_mode) == (0o700 if directory else 0o600), "MODE")
    return path


def load_config(path):
    data = secure_path(path, private=True).read_bytes()
    require(0 < len(data) <= 16384)
    c = json.loads(data, object_pairs_hook=unique)
    required = {"version", "base", "state", "keys", "sources", "recipient", "rclone_config",
                "bind_ip", "remote", "postgres", "containers", "maintenance_lock"}
    require(required <= set(c) <= required | {"retention"})
    if "retention" in c:
        require(isinstance(c["retention"], dict) and set(c["retention"]) == {"days", "apply"} and
                type(c["retention"]["days"]) is int and c["retention"]["days"] == 30 and
                type(c["retention"]["apply"]) is bool, "RETENTION_CONFIG")
    require(type(c["version"]) is int and c["version"] == 2)
    require(c["maintenance_lock"] == str(LOCK), "LOCK_PROTOCOL")
    require(ipaddress.ip_address(c["bind_ip"]).is_global, "BIND_IP")
    require(re.fullmatch(r"[A-Za-z0-9_-]+:[A-Za-z0-9_/-]+", c["remote"]) and
            all(p not in ("", ".", "..") for p in c["remote"].split(":", 1)[1].split("/")), "REMOTE")
    require(isinstance(c["containers"], list) and len(c["containers"]) == 3 and len(set(c["containers"])) == 3)
    require(all(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,99}", n) for n in c["containers"]))
    require(c["postgres"] == c["containers"][2])
    require(isinstance(c["sources"], dict) and set(c["sources"]) == {"config", "tls"})
    directories = [secure_path(c[n], True, True) for n in ("base", "state")]
    keys = Path(c["keys"])
    directories.append(secure_path(keys, True, source_root=keys, source_owners={0, 1654}))
    for name, p in c["sources"].items():
        root = Path(p)
        directories.append(secure_path(root, True, source_root=root, source_owners={0, 101} if name == "tls" else {0}))
    # Cleanup roots must never contain sources, one another, or operational secrets.
    require(all(a != b and a not in b.parents and b not in a.parents
                for i, a in enumerate(directories) for b in directories[i + 1:]), "OVERLAP")
    for name in ("recipient", "rclone_config"):
        p = secure_path(c[name], private=True)
        require(all(root not in p.parents for root in directories[:2]), "OVERLAP")
    return c


def atomic_json(path, value):
    temporary = path.with_suffix(".new")
    with temporary.open("xb") as file:
        file.write(canonical(value))
        file.flush()
        os.fsync(file.fileno())
    temporary.chmod(0o600)
    require(not path.is_symlink())
    temporary.replace(path)


def capture_tree(path, flat=False, owners=frozenset({0})):
    def check(p, directory=False):
        return secure_path(p, directory, source_root=path, source_owners=owners)
    def identity(info):
        return (info.st_dev, info.st_ino, info.st_uid, info.st_gid, info.st_mode, info.st_nlink, info.st_size, info.st_mtime_ns)
    check(path, True)
    result, total = {}, 0
    for folder, directories, files in os.walk(path, followlinks=False):
        for name in directories:
            require(not flat)
            check(Path(folder) / name, True)
        for name in sorted(files):
            src = check(Path(folder) / name)
            relative = src.relative_to(path).as_posix()
            require(re.fullmatch(r"[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)*", relative))
            require(not flat or re.fullmatch(r"key-[A-Za-z0-9-]+\.xml", relative))
            before = src.stat()
            require(before.st_size <= (1024**2 if flat else MAX_FILE), "CAPTURE_LIMIT")
            fd = os.open(src, os.O_RDONLY | os.O_NOFOLLOW)
            with os.fdopen(fd, "rb") as file:
                require(identity(before) == identity(os.fstat(file.fileno())), "CHANGED")
                data = file.read((1024**2 if flat else MAX_FILE) + 1)
                after = os.fstat(file.fileno())
            check(src)
            require(identity(before) == identity(after) == identity(src.stat()) and len(data) == before.st_size, "CHANGED")
            result[relative] = (data, stat.S_IMODE(before.st_mode), before.st_uid, before.st_gid)
            total += len(data)
            require(len(result) <= (128 if flat else 4096) and total <= (4 * 1024**2 - 10240 if flat else MAX_FILE - 10240), "CAPTURE_LIMIT")
    require(result, "EMPTY")
    return result


def require_key_superset(before, after):
    require(before and all(after.get(n) == data for n, data in before.items()), "KEY_ROTATION")


def same_config(before, after):
    require(before == after, "CAPTURE_CHANGED")


def stable_containers(items):
    require(len(items) == 3 and all(c["State"]["Running"] for c in items), "CONTAINERS")
    require(all(items[i]["State"].get("Health", {}).get("Status") == "healthy" for i in (0, 2)), "HEALTH")
    return [{k: c[k] for k in ("Id", "Image", "Config", "Mounts")} for c in items]


def tar_bytes(files):
    stream = io.BytesIO()
    with tarfile.open(fileobj=stream, mode="w", format=tarfile.USTAR_FORMAT) as archive:
        for name, content in sorted(files.items()):
            member = tarfile.TarInfo(name)
            if isinstance(content, tuple):
                content, member.mode, member.uid, member.gid = content
            else:
                member.mode = 0o600
            member.size = len(content)
            archive.addfile(member, io.BytesIO(content))
    return stream.getvalue()


def make_package(db_files, keys, configs, roles, schema, containers, epoch):
    dumps = [n for n in db_files if re.fullmatch(r"hfpos-[0-9]{8}T[0-9]{6}Z-[0-9]+\.dump", n)]
    require(len(dumps) == 1)
    dump = dumps[0]
    require(set(db_files) == {dump, dump + ".sha256", dump + ".manifest"})
    key = "keyring/keyring-" + dump[6:-5] + ".tar"
    key_data = tar_bytes({"./" + name: data for name, data in keys.items()})
    require(len(key_data) <= 4 * 1024**2)
    files = {"db/" + n: data for n, data in db_files.items()}
    files.update({key: key_data, key + ".sha256": (digest(key_data) + "  " + Path(key).name + "\n").encode(),
        "snapshot/host-config.tar": tar_bytes(configs), "metadata/roles.sql": roles,
        "metadata/schema-ownership-grants.sql": schema, "metadata/containers.private.json": canonical(containers),
        "metadata/capture.json": canonical({"snapshot_epoch": epoch}),
        "metadata/format.json": canonical({"format": "hfpos-auto", "version": 2})})
    files["metadata/internal-sha256.txt"] = "".join(digest(v) + "  " + n + "\n" for n, v in sorted(files.items())).encode()
    require(all(len(v) <= MAX_FILE for v in files.values()) and sum(map(len, files.values())) <= MAX_TOTAL, "PACKAGE_LIMIT")
    package = tar_bytes(files)
    require(len(package) <= MAX_TOTAL, "PACKAGE_LIMIT")
    return package


@contextmanager
def maintenance_lock():
    import fcntl
    # Installer and human maintenance prepare the SAME inode; never unlink/recreate it.
    secure_path(LOCK, private=True)
    fd = os.open(LOCK, os.O_RDWR | os.O_NOFOLLOW)
    try:
        info = os.fstat(fd)
        require((info.st_dev, info.st_ino) == (LOCK.stat().st_dev, LOCK.stat().st_ino) and info.st_nlink == 1)
        try:
            fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            raise BackupError("MAINTENANCE_BUSY") from None
        yield
    finally:
        os.close(fd)


def clean_owned(staging, identity):
    require(staging.name.startswith("staging-") and staging.parent == identity[0], "CLEANUP_PATH")
    secure_path(staging, True, True)
    info = staging.stat()
    require((info.st_dev, info.st_ino) == identity[1:], "CLEANUP_IDENTITY")
    # Check every member first. Do not follow a substituted link or adopt foreign resources.
    for folder, directories, files in os.walk(staging, followlinks=False):
        for name in directories:
            secure_path(Path(folder) / name, True)
        for name in files:
            secure_path(Path(folder) / name)
    shutil.rmtree(staging)


class Runner:
    def __init__(self):
        self.deadline = time.monotonic() + 1100

    def run(self, args, data=None, outfile=None, limit=None):
        remaining = min(self.deadline - time.monotonic(), limit or 1100)
        require(remaining > 0, "TIMEOUT")
        try:
            if outfile is None:
                result = subprocess.run(args, input=data, capture_output=True, env=ENV, timeout=remaining)
                require(len(result.stdout) <= MAX_TOTAL, "COMMAND_LIMIT")
            else:
                with outfile.open("xb") as file:
                    result = subprocess.run(args, input=data, stdout=file, stderr=subprocess.DEVNULL, env=ENV, timeout=remaining)
                require(outfile.stat().st_size <= MAX_TOTAL, "COMMAND_LIMIT")
            require(result.returncode == 0, "COMMAND_FAILED")
            return result.stdout if outfile is None else b""
        except subprocess.TimeoutExpired:
            raise BackupError("TIMEOUT") from None

    def download(self, args, output, expected_size, expected_hash, expected_bytes=None):
        require(type(expected_size) is int and 0 <= expected_size <= MAX_TOTAL, "REMOTE_LIMIT")
        require(expected_bytes is None or len(expected_bytes) == expected_size)
        remaining = self.deadline - time.monotonic()
        require(remaining > 0, "TIMEOUT")
        process = subprocess.Popen(args, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                                   stderr=subprocess.DEVNULL, env=ENV)
        def expire():
            try:
                process.kill()
            except OSError:
                pass
        watchdog = threading.Timer(remaining, expire)
        watchdog.start()
        try:
            total, checksum = 0, hashlib.sha256()
            with output.open("xb") as file:
                while True:
                    # Read at most ONE byte beyond the known local object, never the remote whole.
                    chunk = process.stdout.read(min(65536, expected_size + 1 - total))
                    if not chunk:
                        break
                    total += len(chunk)
                    require(total <= expected_size, "REMOTE_OVERSIZE")
                    if expected_bytes is not None:
                        require(chunk == expected_bytes[total - len(chunk):total], "REMOTE_HASH")
                    checksum.update(chunk)
                    file.write(chunk)
            require(process.wait(timeout=max(1, self.deadline - time.monotonic())) == 0, "COMMAND_FAILED")
            require(total == expected_size and checksum.hexdigest() == expected_hash, "REMOTE_HASH")
        except subprocess.TimeoutExpired:
            raise BackupError("TIMEOUT") from None
        finally:
            watchdog.cancel()
            if process.poll() is None:
                expire()
            process.wait(timeout=5)
            process.stdout.close()


def status(state, now=None, include_retention=True):
    try:
        success = json.loads((state / "last-success.json").read_bytes(), object_pairs_hook=unique)
        attempt = json.loads((state / "last-run.json").read_bytes(), object_pairs_hook=unique)
        age = (time.time() if now is None else now) - success["snapshot_epoch"]
        require(0 <= age < 3600 and attempt["status"] == "success", "STALE_OR_FAILED")
        retention = state / "last-retention.json"
        if include_retention and (retention.exists() or retention.is_symlink()):
            result = json.loads(secure_path(retention, private=True).read_bytes(), object_pairs_hook=unique)
            if result["status"] == "running":
                started = result["started_epoch"]
                require(type(started) in (int, float), "RETENTION_STATE")
                elapsed = (time.time() if now is None else now) - started
                require(0 <= elapsed < 1200, "RETENTION_OVERRUN")
            else:
                require(result["status"] == "success", "RETENTION_FAILED")
        print("BACKUP_VERIFIED_UNDER_ONE_HOUR")
        return 0
    except (OSError, ValueError, KeyError, TypeError, BackupError):
        print("BACKUP_ALERT_HUMAN_ACTION_REQUIRED")
        return 1


def backup(c, runner=None):
    state = secure_path(c["state"], True, True)
    ident = uuid.uuid4().hex
    try:
        # Entry failures and a busy maintenance lock must invalidate the last attempt too.
        atomic_json(state / "last-run.json", {"status": "running", "recovery_id": ident})
        return backup_attempt(c, runner or Runner(), ident)
    except Exception as error:
        try:
            attempt = json.loads((state / "last-run.json").read_bytes(), object_pairs_hook=unique)
            if attempt.get("status") != "failed" or attempt.get("recovery_id") != ident:
                atomic_json(state / "last-run.json", {"status": "failed", "recovery_id": ident,
                    "reason": str(error) if isinstance(error, BackupError) else "ENTRY_OR_CAPTURE_FAILURE"})
        except Exception:
            raise BackupError("STATE_RECORD_FAILED") from None
        raise


def backup_attempt(c, runner, ident):
    base, state = Path(c["base"]), Path(c["state"])
    with maintenance_lock():
        work = base / ident
        work.mkdir(mode=0o700)
        staging = work / ("staging-" + ident)
        staging.mkdir(mode=0o700)
        info = staging.stat()
        identity = (work, info.st_dev, info.st_ino)
        pg_stage = "/tmp/hfone-auto-" + ident
        pg_owned = False
        def run(*args, **kwargs):
            return runner.run(list(args), **kwargs)
        def pg(command):
            script = ('set -eu\nexport PGUSER="$POSTGRES_USER" PGDATABASE="$POSTGRES_DB"\n'
                      'export PGHOST=/var/run/postgresql PGPORT=5432\n'
                      'export PGOPTIONS="-c statement_timeout=45000 -c lock_timeout=5000"\nexec ' + command)
            return run("docker", "exec", "-i", c["postgres"], "timeout", "--kill-after=5", "55", "sh", "-s", data=script.encode())
        def sql_canonical(data):
            return b"\n".join(line for line in data.splitlines() if not line.startswith((b"\\restrict ", b"\\unrestrict ")))
        def r2_args(*args):
            return ["rclone", "--config", c["rclone_config"], "--bind", c["bind_ip"], "--s3-no-head",
                    "--contimeout", "20s", "--timeout", "60s", "--retries", "2", *args]
        failed = True
        try:
            require(shutil.disk_usage(base).free >= max(1024**3, int(pg('psql -X -w -Atq -c "SELECT pg_database_size(current_database());"')) * 5 + 512 * 1024**2), "CAPACITY")
            containers = json.loads(run("docker", "inspect", *c["containers"]))
            original_containers = stable_containers(containers)
            configs = {n: capture_tree(Path(p), owners={0, 101} if n == "tls" else {0}) for n, p in c["sources"].items()}
            before = capture_tree(Path(c["keys"]), flat=True, owners={0, 1654})
            recipient = secure_path(c["recipient"], private=True).read_bytes()
            require(0 < len(recipient) <= 4096)
            rconf_hash = digest(secure_path(c["rclone_config"], private=True).read_bytes())
            roles = pg("pg_dumpall --roles-only --no-role-passwords")
            schema = pg("pg_dump --schema-only")
            epoch = time.time()
            run("docker", "exec", c["postgres"], "mkdir", "-m", "700", pg_stage)
            pg_owned = True
            for name, expected in HELPER_HASHES.items():
                helper = secure_path(HERE.parent / name)
                require(digest(helper.read_bytes()) == expected, "HELPER_HASH")
                run("docker", "cp", str(helper), c["postgres"] + ":" + pg_stage + "/" + name)
            version = next((s.split("=", 1)[1] for s in containers[0]["Config"]["Env"] if s.startswith("HFPOS_RELEASE_VERSION=")), "unknown")
            script = b'''set -eu
umask 077
export PGUSER="$POSTGRES_USER" PGDATABASE="$POSTGRES_DB" PGHOST=/var/run/postgresql PGPORT=5432
export PGOPTIONS="-c statement_timeout=540000 -c lock_timeout=10000"
sh "$1/postgres-backup.sh" "$1/db"
for dump in "$1/db/"*.dump; do sh "$1/verify-postgres-backup.sh" "$dump"; done
'''
            inner_limit = int(min(600, runner.deadline - time.monotonic() - 60))
            require(inner_limit >= 30, "TIMEOUT_MARGIN")
            run("docker", "exec", "-i", "-e", "HFPOS_RELEASE_VERSION=" + version, c["postgres"], "timeout", "--kill-after=10", str(inner_limit), "sh", "-s", "--", pg_stage, data=script)
            db = staging / "db"
            db.mkdir(mode=0o700)
            run("docker", "cp", c["postgres"] + ":" + pg_stage + "/db/.", str(db))
            db_files = {p.name: secure_path(p).read_bytes() for p in db.iterdir() if p.stat().st_size <= MAX_FILE}
            require(len(db_files) == len(list(db.iterdir())), "DUMP_LIMIT")
            after = capture_tree(Path(c["keys"]), flat=True, owners={0, 1654})
            require_key_superset(before, after)
            same_config(configs, {n: capture_tree(Path(p), owners={0, 101} if n == "tls" else {0}) for n, p in c["sources"].items()})
            same_config(original_containers, stable_containers(json.loads(run("docker", "inspect", *c["containers"]))))
            same_config(sql_canonical(roles), sql_canonical(pg("pg_dumpall --roles-only --no-role-passwords")))
            same_config(sql_canonical(schema), sql_canonical(pg("pg_dump --schema-only")))
            same_config(recipient, secure_path(c["recipient"], private=True).read_bytes())
            same_config(rconf_hash, digest(secure_path(c["rclone_config"], private=True).read_bytes()))
            host = {n + "/" + key: data for n, tree in configs.items() for key, data in tree.items()}
            # Pin recipient/config inside encrypted offline snapshot too; no imported runtime environment.
            host["recovery/recipient.pub"] = recipient
            host["recovery/rclone.conf"] = secure_path(c["rclone_config"], private=True).read_bytes()
            package = staging / "package.tar"
            package.write_bytes(make_package(db_files, after, host, roles, schema, containers, epoch))
            spec = importlib.util.spec_from_file_location("backup_archive", HERE.parent / "supervised-archive.py")
            archive = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(archive)
            archive.filtered_export(package, staging / "validated", ident, "0" * 64)
            public = staging / "recipient.pub"
            public.write_bytes(recipient)
            cipher = work / "recovery.age"
            run("age", "-R", str(public), "-o", str(cipher), str(package))
            require(0 < cipher.stat().st_size <= MAX_TOTAL, "CIPHERTEXT_LIMIT")
            manifest = {"format_version": 2, "recovery_id": ident, "snapshot_epoch": epoch,
                        "ciphertext_sha256": file_digest(cipher), "ciphertext_bytes": cipher.stat().st_size}
            atomic_json(work / "manifest.json", manifest)
            remote = c["remote"] + "/" + ident
            for name in ("recovery.age", "manifest.json"):
                runner.run(r2_args("copyto", str(work / name), remote + "/" + name))
                metadata = canonical(manifest) if name == "manifest.json" else None
                runner.download(r2_args("cat", remote + "/" + name), staging / name,
                    len(metadata) if metadata is not None else manifest["ciphertext_bytes"],
                    digest(metadata) if metadata is not None else manifest["ciphertext_sha256"], metadata)
            # Do not claim success until sensitive NEW staging and PG stage are withdrawn.
            run("docker", "exec", c["postgres"], "sh", "-c", 'test -d "$1" && test ! -L "$1" && test "$(stat -c %u "$1")" = 0 && rm -rf -- "$1"', "sh", pg_stage, limit=15)
            pg_owned = False
            clean_owned(staging, identity)
            manifest["completed_utc"] = dt.datetime.now(dt.timezone.utc).isoformat()
            atomic_json(work / "INTEGRITY_OFFSITE_VERIFIED.json", manifest)
            atomic_json(state / "last-success.json", manifest)
            atomic_json(state / "last-run.json", {"status": "success", "recovery_id": ident})
            failed = False
        finally:
            cleanup_ok = True
            if pg_owned:
                try:
                    Runner().run(["docker", "exec", c["postgres"], "sh", "-c", 'test -d "$1" && test ! -L "$1" && test "$(stat -c %u "$1")" = 0 && rm -rf -- "$1"', "sh", pg_stage], limit=15)
                except Exception:
                    cleanup_ok = False
            if staging.exists() or staging.is_symlink():
                try:
                    clean_owned(staging, identity)
                except Exception:
                    cleanup_ok = False
            if failed or not cleanup_ok:
                atomic_json(state / "last-run.json", {"status": "failed", "recovery_id": ident,
                                                      "staging": "REMOVED" if cleanup_ok else "HUMAN_CONTAINMENT_REQUIRED"})
            require(cleanup_ok, "CLEANUP_HUMAN_REQUIRED")
    return status(state, include_retention=False)


def operations():
    path = secure_path(HERE / "operations.py")
    spec = importlib.util.spec_from_file_location("backup_operations", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module, SimpleNamespace(**globals())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    action = parser.add_mutually_exclusive_group()
    action.add_argument("--status", action="store_true")
    action.add_argument("--retention-preview", action="store_true")
    action.add_argument("--retention-apply", action="store_true")
    args = parser.parse_args()
    c = None
    try:
        require(os.name == "posix" and os.geteuid() == 0, "ROOT_REQUIRED")
        os.umask(0o077)
        c = load_config(args.config)
        if args.status:
            return status(Path(c["state"]))  # Read-only, including local retention failure/overrun.
        if args.retention_preview or args.retention_apply:
            ops, worker = operations()
            return ops.maintain(worker, c, apply=args.retention_apply)
        runner = Runner()
        result = backup(c, runner)
        if "retention" in c:
            ops, worker = operations()
            result = max(result, ops.maintain(worker, c, apply=c["retention"]["apply"], runner=runner))
        return result
    except Exception as error:
        if c is not None and isinstance(error, BackupError) and str(error) == "MAINTENANCE_BUSY":
            try:
                atomic_json(Path(c["state"]) / "last-run.json", {"status": "failed", "reason": "MAINTENANCE_BUSY"})
            except Exception:
                pass
        print("BACKUP_FAILED_" + (str(error) if isinstance(error, BackupError) else "SAFE_STOP"))
        return 1


if __name__ == "__main__":
    sys.exit(main())
