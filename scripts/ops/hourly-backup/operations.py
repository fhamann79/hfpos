"""Scoped auto-v2 retention; no restore or application access."""
import datetime as dt
import json
import math
import os
from pathlib import Path
import re
import stat
import tempfile
import time

ID = re.compile(r"[a-f0-9]{32}")
FILES = {"recovery.age", "manifest.json", "INTEGRITY_OFFSITE_VERIFIED.json"}
LIMIT = 8  # Bound maintenance to eight expired sets per normal backup, not an unbounded purge.
META_KEYS = {"format_version", "recovery_id", "snapshot_epoch", "ciphertext_sha256", "ciphertext_bytes"}


def read(w, path, limit=16384):
    """Never adopt links, group-writable inputs, or unexpectedly large metadata."""
    p = w.secure_path(path, private=True)
    fd = os.open(p, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, "rb") as stream:
        before = os.fstat(stream.fileno())
        w.require(stat.S_ISREG(before.st_mode) and before.st_nlink == 1 and
                  0 < before.st_size <= limit, "OPERATIONS_FILE")
        w.require((before.st_dev, before.st_ino) == (p.stat().st_dev, p.stat().st_ino), "OPERATIONS_IDENTITY")
        data = stream.read(limit + 1)
        after = os.fstat(stream.fileno())
    w.require((before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) ==
              (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns) and len(data) == before.st_size,
              "OPERATIONS_CHANGED")
    return data


def document(w, path):
    return json.loads(read(w, path), object_pairs_hook=w.unique)


def private_dir(w, path):
    try:
        path.mkdir(mode=0o700)
    except FileExistsError:
        pass
    return w.secure_path(path, True, True)


def finite_epoch(w, value):
    w.require(type(value) in (int, float) and math.isfinite(value) and value > 0, "EPOCH")
    return value


def health(w, state, now):
    """Retention requires a fresh successful backup, independently of maintenance state."""
    codes = set()
    try:
        success = document(w, state / "last-success.json")
        age = now - finite_epoch(w, success["snapshot_epoch"])
        if age < 0:
            codes.add("CLOCK")
        elif age >= 3600:
            codes.add("STALE")
    except (OSError, ValueError, KeyError, TypeError, w.BackupError):
        codes.add("NO_VERIFIED_COPY")
    try:
        if document(w, state / "last-run.json")["status"] != "success":
            codes.add("BACKUP_STATE")
    except (OSError, ValueError, KeyError, TypeError, w.BackupError):
        codes.add("BACKUP_STATE")
    return sorted(codes)


def manifest(w, value, ident, now, completed=False):
    w.require(set(value) == META_KEYS | ({"completed_utc"} if completed else set()), "RETENTION_MANIFEST")
    w.require(type(value["format_version"]) is int and value["format_version"] == 2 and value["recovery_id"] == ident, "RETENTION_VERSION")
    epoch = finite_epoch(w, value["snapshot_epoch"])
    w.require(epoch <= now and re.fullmatch(r"[a-f0-9]{64}", value["ciphertext_sha256"]) and
              type(value["ciphertext_bytes"]) is int and 0 < value["ciphertext_bytes"] <= w.MAX_TOTAL, "RETENTION_MANIFEST")
    if completed:
        when = dt.datetime.fromisoformat(value["completed_utc"])
        w.require(when.tzinfo is not None and epoch <= when.timestamp() <= now, "RETENTION_COMPLETED")
    return value


def local_record(w, c, ident, now, full=True):
    work = w.secure_path(Path(c["base"]) / ident, True, True)
    w.require({p.name for p in work.iterdir()} == FILES, "RETENTION_MEMBERS")
    m = manifest(w, document(w, work / "manifest.json"), ident, now)
    verified = manifest(w, document(w, work / "INTEGRITY_OFFSITE_VERIFIED.json"), ident, now, True)
    w.require({k: v for k, v in verified.items() if k != "completed_utc"} == m, "RETENTION_EVIDENCE")
    cipher = w.secure_path(work / "recovery.age", private=True)
    w.require(cipher.stat().st_size == m["ciphertext_bytes"] and (not full or w.file_digest(cipher) == m["ciphertext_sha256"]), "RETENTION_HASH")
    info = work.stat()
    return {"version": 1, "id": ident, "base": c["base"], "remote": c["remote"],
            "inode": [info.st_dev, info.st_ino], "manifest": m, "verified": verified}


def remote_args(c, *args):
    return ["rclone", "--config", c["rclone_config"], "--bind", c["bind_ip"], "--s3-no-head",
            "--contimeout", "10s", "--timeout", "20s", "--retries", "1", *args]


def validate_remote(w, c, record, runner, pending=False):
    """Every present object is reauthenticated, including after a partial deletion."""
    root = c["remote"] + "/" + record["id"]
    raw = runner.read_bounded(remote_args(c, "lsjson", root, "--recursive"), 16384, limit=30)
    w.require(len(raw) <= 16384, "RETENTION_REMOTE_LIMIT")
    listing = json.loads(raw, object_pairs_hook=w.unique)
    w.require(isinstance(listing, list) and len(listing) <= 2, "RETENTION_REMOTE_MEMBERS")
    objects = {}
    expected = {"recovery.age": (record["manifest"]["ciphertext_bytes"], record["manifest"]["ciphertext_sha256"]),
                "manifest.json": (len(w.canonical(record["manifest"])), w.digest(w.canonical(record["manifest"])))}
    for entry in listing:
        name = entry["Path"]
        w.require(name in expected and name not in objects and entry.get("IsDir") is False and
                  type(entry["Size"]) is int and entry["Size"] == expected[name][0], "RETENTION_REMOTE_MEMBERS")
        objects[name] = expected[name]
    w.require(pending or set(objects) == set(expected), "RETENTION_REMOTE_MISSING")
    with tempfile.TemporaryDirectory(prefix="retention-verify-", dir=c["state"]) as temporary:
        for name, (size, checksum) in objects.items():
            runner.download(remote_args(c, "cat", root + "/" + name), Path(temporary) / name,
                            size, checksum, w.canonical(record["manifest"]) if name == "manifest.json" else None)
    return set(objects)


def validate_pending(w, c, record, now, full=False):
    w.require(set(record) == {"version", "id", "base", "remote", "inode", "manifest", "verified"} and
              type(record["version"]) is int and record["version"] == 1 and ID.fullmatch(record["id"]) and record["base"] == c["base"] and
              record["remote"] == c["remote"] and isinstance(record["inode"], list) and len(record["inode"]) == 2 and
              all(type(n) is int and n >= 0 for n in record["inode"]), "RETENTION_JOURNAL")
    m = manifest(w, record["manifest"], record["id"], now)
    verified = manifest(w, record["verified"], record["id"], now, True)
    w.require({k: v for k, v in verified.items() if k != "completed_utc"} == m, "RETENTION_JOURNAL")
    work = Path(c["base"]) / record["id"]
    if work.exists() or work.is_symlink():
        w.secure_path(work, True, True)
        info = work.stat()
        w.require([info.st_dev, info.st_ino] == record["inode"], "RETENTION_IDENTITY")
        w.require({p.name for p in work.iterdir()} <= FILES, "RETENTION_MEMBERS")
        for p in work.iterdir():
            w.secure_path(p, private=True)
            expected = m if p.name == "manifest.json" else verified
            if p.name == "recovery.age":
                w.require(p.stat().st_size == m["ciphertext_bytes"] and (not full or w.file_digest(p) == m["ciphertext_sha256"]), "RETENTION_HASH")
            else:
                w.require(document(w, p) == expected, "RETENTION_HASH")
    return work


def expired(record, now, days):
    # Preserve a full 30 days after both the snapshot and offsite verification.
    completed = dt.datetime.fromisoformat(record["verified"]["completed_utc"]).timestamp()
    return max(record["manifest"]["snapshot_epoch"], completed) < now - days * 86400


def retention(w, c, apply=False, runner=None, now=None):
    w.require("retention" in c, "RETENTION_NOT_CONFIGURED")
    w.require(not apply or c["retention"]["apply"], "RETENTION_APPLY_NOT_ENABLED")
    runner = runner or w.Runner()
    now = time.time() if now is None else now
    state, base = Path(c["state"]), Path(c["base"])
    with w.maintenance_lock():
        # A last-good backup is never purged, nor is retention run during an outage.
        w.require(not health(w, state, now), "RETENTION_BACKUP_UNHEALTHY")
        last = document(w, state / "last-success.json")
        protected = last["recovery_id"]
        w.require(ID.fullmatch(protected), "RETENTION_LAST_SUCCESS")
        current = local_record(w, c, protected, now)
        w.require(current["verified"] == last, "RETENTION_LAST_SUCCESS")
        pending = state / "retention-pending"
        history = state / "retention-history"
        # Preview never creates directories or changes the journal.
        journals = []
        if pending.exists() or pending.is_symlink():
            w.secure_path(pending, True, True)
            journals = list(pending.iterdir())
            w.require(len(journals) <= 256 and all(ID.fullmatch(p.stem) and p.name == p.stem + ".json" for p in journals), "RETENTION_PENDING")
        records, skipped = {}, 0
        for path in sorted(journals):
            record = document(w, path)
            w.require(record["id"] == path.stem, "RETENTION_JOURNAL")
            validate_pending(w, c, record, now)
            records[record["id"]] = (record, True)
        entries = list(base.iterdir())
        w.require(len(entries) <= 8192, "RETENTION_LIMIT")
        for path in sorted(entries):
            if path.name == protected or path.name in records:
                continue
            if not ID.fullmatch(path.name):
                skipped += 1
                continue
            try:
                # Fresh sets cannot expire; do not reread every retained ciphertext each half-hour.
                header = manifest(w, document(w, path / "manifest.json"), path.name, now)
                if header["snapshot_epoch"] >= now - c["retention"]["days"] * 86400:
                    continue
                record = local_record(w, c, path.name, now, full=False)
            except (OSError, ValueError, KeyError, TypeError, w.BackupError):
                skipped += 1
                continue
            if expired(record, now, c["retention"]["days"]):
                records[path.name] = (record, False)
        selected = sorted(records.values(), key=lambda item: item[0]["manifest"]["snapshot_epoch"])[:LIMIT]
        result = {"mode": "apply" if apply else "preview", "eligible": len(records), "selected": [], "preserved_unknown": skipped}
        if apply:
            private_dir(w, pending)
            private_dir(w, history)
        for record, resume in selected:
            ident = record["id"]
            w.require(ident != protected and expired(record, now, c["retention"]["days"]), "RETENTION_PROTECTED")
            # Full ciphertext reads happen only after selecting the bounded batch.
            if resume:
                validate_pending(w, c, record, now, full=True)
            else:
                w.require(local_record(w, c, ident, now) == record, "RETENTION_CHANGED")
            remote = validate_remote(w, c, record, runner, pending=resume)
            result["selected"].append(ident)
            if not apply:
                continue
            journal = pending / (ident + ".json")
            if not resume:
                w.require(not journal.exists() and not journal.is_symlink(), "RETENTION_JOURNAL")
                w.atomic_json(journal, record)
            # Only two literal, authenticated objects. Never purge/rmdirs a shared R2 prefix.
            for name in ("recovery.age", "manifest.json"):
                if name in remote:
                    runner.run(remote_args(c, "deletefile", c["remote"] + "/" + ident + "/" + name), limit=30)
            w.require(not validate_remote(w, c, record, runner, pending=True), "RETENTION_REMOTE_REMAINS")
            work = validate_pending(w, c, record, now, full=True)
            if work.exists():
                for name in ("recovery.age", "manifest.json", "INTEGRITY_OFFSITE_VERIFIED.json"):
                    p = work / name
                    if p.exists() or p.is_symlink():
                        # Revalidate the whole remaining local set before each unlink.
                        validate_pending(w, c, record, now)
                        p.unlink()
                work.rmdir()  # Fails if any unexpected member appeared; never recursive.
            month = dt.datetime.fromtimestamp(now, dt.timezone.utc).strftime("%Y-%m")
            completed = private_dir(w, history / month) / journal.name
            w.require(not completed.exists() and not completed.is_symlink(), "RETENTION_HISTORY")
            journal.rename(completed)  # Preserve evidence; do not erase the verified record.
        return result


def maintain(w, c, apply=False, runner=None, now=None):
    """Retention failure has its own state; it never rewrites backup success."""
    try:
        if apply:
            w.atomic_json(Path(c["state"]) / "last-retention.json",
                          {"status": "running", "started_epoch": time.time() if now is None else now})
        result = retention(w, c, apply, runner, now)
        if apply:
            w.atomic_json(Path(c["state"]) / "last-retention.json", {"status": "success"})
        print(w.canonical(result).decode(), end="")
        return 0
    except Exception:
        if apply:
            w.atomic_json(Path(c["state"]) / "last-retention.json", {"status": "failed"})
        print("RETENTION_FAILED_NO_BROAD_CLEANUP")
        return 1
