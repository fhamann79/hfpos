#!/usr/bin/env python3
"""Human-only preparation. Does not start services/timers or run a backup."""
import argparse
import hashlib
import importlib.util
import os
from pathlib import Path
import sys
spec = importlib.util.spec_from_file_location("hourly_backup", Path(__file__).resolve().with_name("backup.py"))
backup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(backup)

UNITS = ("hf-one-backup.service", "hf-one-backup.timer", "hf-one-backup-check.service", "hf-one-backup-check.timer")


def prepare(source, destination, unitdir):
    backup.require(destination.is_absolute() and not destination.exists() and not destination.is_symlink())
    backup.secure_path(destination.parent, True)
    backup.secure_path(unitdir, True)
    names = {"hourly-backup/" + n: source / n for n in ("backup.py", "operations.py", "installed-provenance.json")}
    names.update({n: source.parent / n for n in (*backup.HELPER_HASHES, "supervised-archive.py", "supervised-operations.py")})
    files = {n: backup.secure_path(p).read_bytes() for n, p in names.items()}
    units = {n: backup.secure_path(source / n).read_bytes() for n in UNITS}
    for n, expected in backup.HELPER_HASHES.items():
        backup.require(backup.digest(files[n]) == expected, "HELPER_HASH")
    backup.require(all(not (unitdir / n).exists() and not (unitdir / n).is_symlink() for n in UNITS), "EXISTING_UNITS_HUMAN_RECONCILIATION")
    destination.mkdir(mode=0o700)
    (destination / "hourly-backup").mkdir(mode=0o700)
    for n, data in files.items():
        with (destination / n).open("xb") as file:
            file.write(data)
        (destination / n).chmod(0o600)
    for n, data in units.items():
        with (unitdir / n).open("xb") as file:
            file.write(data)
        (unitdir / n).chmod(0o644)
    backup.atomic_json(destination / "source-hashes.json", {n: hashlib.sha256(v).hexdigest() for n, v in {**files, **units}.items()})
    # Keep a preexisting lock inode. Never truncate/unlink a lock in use by old auto-v1.
    if not backup.LOCK.exists():
        fd = os.open(backup.LOCK, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600)
        os.close(fd)
    backup.secure_path(backup.LOCK, private=True)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--prepare-only", action="store_true", required=True)
    args = p.parse_args()
    try:
        backup.require(os.name == "posix" and os.geteuid() == 0)
        os.umask(0o077)
        prepare(Path(__file__).resolve().parent, Path("/usr/local/lib/hf-one-hourly-backup-v2"), Path("/etc/systemd/system"))
        print("PREPARED_NOT_ACTIVATED_HUMAN_CONFIG_AND_MAINTENANCE_GATE")
        return 0
    except Exception:
        print("STOP_INSTALL_NO_ACTIVATION_NO_RETRY")
        return 1


if __name__ == "__main__":
    sys.exit(main())
