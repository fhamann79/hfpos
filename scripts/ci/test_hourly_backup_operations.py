"""Synthetic-only retention regressions. Never use live services."""
from contextlib import redirect_stdout
import datetime as dt
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]

def load(name):
    path = ROOT / "scripts/ops/hourly-backup" / name
    spec = importlib.util.spec_from_file_location("operation_test_" + path.stem, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

worker, ops = load("backup.py"), load("operations.py")
NOW = 1793280000
LATEST, OLD = "f" * 32, "a" * 32

class Remote:
    """Exact object store double; no sockets, rclone, or ciphertext production data."""
    def __init__(self):
        self.objects, self.calls, self.fail_after_delete = {}, [], False
    def run(self, args, **kwargs):
        self.calls.append(args)
        if "lsjson" in args:
            prefix = args[args.index("lsjson") + 1] + "/"
            return worker.canonical([{"Path": n[len(prefix):], "IsDir": False, "Size": len(data)}
                                     for n, data in sorted(self.objects.items()) if n.startswith(prefix)])
        if "deletefile" in args:
            self.objects.pop(args[-1])
            if self.fail_after_delete:
                self.fail_after_delete = False
                raise worker.BackupError("SYNTHETIC_LOST_RESPONSE")
            return b""
        raise AssertionError(args)
    def read_bounded(self, args, maximum, limit=30):
        raw = self.run(args, limit=limit)
        worker.require(len(raw) <= maximum, "COMMAND_LIMIT")
        return raw
    def download(self, args, output, size, checksum, expected_bytes=None):
        self.calls.append(args)
        data = self.objects[args[-1]]
        worker.require(len(data) == size and worker.digest(data) == checksum, "REMOTE_HASH")
        worker.require(expected_bytes is None or data == expected_bytes, "REMOTE_HASH")
        output.write_bytes(data)
        output.chmod(0o600)

@unittest.skipUnless(os.name == "posix" and os.geteuid() == 0, "Root-private synthetic files; Linux operational contract")
class OperationsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir="/root")
        self.root = Path(self.temp.name)
        self.base, self.state = self.root / "base", self.root / "state"
        self.base.mkdir(mode=0o700)
        self.state.mkdir(mode=0o700)
        self.lock = self.root / "maintenance.lock"
        self.lock.touch(mode=0o600)
        self.patch = patch.object(worker, "LOCK", self.lock)
        self.patch.start()
        self.c = {"base": str(self.base), "state": str(self.state), "remote": "synthetic:bucket/recovery",
                  "bind_ip": "8.8.8.8", "rclone_config": str(self.root / "rclone.conf"),
                  "retention": {"days": 30, "apply": True}}
        self.remote = Remote()
        self.add(LATEST, NOW - 60)
        self.add(OLD, NOW - 31 * 86400)
        worker.atomic_json(self.state / "last-success.json", json.loads((self.base / LATEST / "INTEGRITY_OFFSITE_VERIFIED.json").read_bytes()))
        worker.atomic_json(self.state / "last-run.json", {"status": "success", "recovery_id": LATEST})
        self.output = io.StringIO()
        self.redirect = redirect_stdout(self.output)
        self.redirect.__enter__()
    def tearDown(self):
        self.redirect.__exit__(None, None, None)
        self.patch.stop()
        self.temp.cleanup()
    def write(self, path, content):
        path.write_bytes(content)
        path.chmod(0o600)
    def add(self, ident, epoch):
        work = self.base / ident
        work.mkdir(mode=0o700)
        cipher = b"SYNTHETIC_ENCRYPTED_ONLY_" + ident.encode()
        manifest = {"format_version": 2, "recovery_id": ident, "snapshot_epoch": epoch,
                    "ciphertext_sha256": worker.digest(cipher), "ciphertext_bytes": len(cipher)}
        verified = {**manifest, "completed_utc": dt.datetime.fromtimestamp(epoch + 1, dt.timezone.utc).isoformat()}
        self.write(work / "recovery.age", cipher)
        worker.atomic_json(work / "manifest.json", manifest)
        worker.atomic_json(work / "INTEGRITY_OFFSITE_VERIFIED.json", verified)
        for name in ("recovery.age", "manifest.json"):
            self.remote.objects[self.c["remote"] + "/" + ident + "/" + name] = (work / name).read_bytes()
        return work
    def prune(self, apply=False):
        return ops.retention(worker, self.c, apply, self.remote, NOW)
    def test_preview_authenticates_but_never_deletes_or_creates_journals(self):
        original = dict(self.remote.objects)
        result = self.prune()
        self.assertEqual([OLD], result["selected"])
        self.assertEqual(original, self.remote.objects)
        self.assertEqual({OLD, LATEST}, {p.name for p in self.base.iterdir()})
        self.assertFalse((self.state / "retention-pending").exists())
        self.assertFalse(any("deletefile" in call for call in self.remote.calls))
        self.assertFalse(any(p.name.startswith("retention-verify-") for p in self.state.iterdir()))
    def test_only_expired_exact_v2_is_deleted_and_evidence_is_kept(self):
        for name in ("deploy-special", "rc6-first-invoice-id", "automatic-v1"):
            (self.base / name).mkdir(mode=0o700)
            self.write(self.base / name / "recovery.age", b"HISTORY")
        recent = "b" * 32
        self.add(recent, NOW - 29 * 86400)
        self.prune(True)
        self.assertFalse((self.base / OLD).exists())
        self.assertTrue((self.base / LATEST).exists() and (self.base / recent).exists())
        self.assertTrue((self.base / "deploy-special/recovery.age").exists())
        receipts = list((self.state / "retention-history").glob("*/*.json"))
        self.assertEqual(1, len(receipts))
        self.assertEqual(OLD, json.loads(receipts[0].read_bytes())["id"])
        deletions = [call[-1] for call in self.remote.calls if "deletefile" in call]
        self.assertEqual([self.c["remote"] + "/" + OLD + "/" + n for n in ("recovery.age", "manifest.json")], deletions)
        self.assertFalse(any(token in call for call in self.remote.calls for token in ("purge", "delete", "rmdirs")))
    def test_explicit_apply_required(self):
        self.c["retention"]["apply"] = False
        self.prune()
        with self.assertRaisesRegex(worker.BackupError, "RETENTION_APPLY_NOT_ENABLED"):
            self.prune(True)
        self.assertTrue((self.base / OLD).exists())
    def test_thirty_day_boundary_is_preserved(self):
        boundary = "b" * 32
        self.add(boundary, NOW - 30 * 86400)
        self.assertEqual([OLD], self.prune()["selected"])
    def test_recent_offsite_verification_preserves_an_old_snapshot_for_thirty_days(self):
        path = self.base / OLD / "INTEGRITY_OFFSITE_VERIFIED.json"
        verified = json.loads(path.read_bytes())
        verified["completed_utc"] = dt.datetime.fromtimestamp(NOW - 1, dt.timezone.utc).isoformat()
        worker.atomic_json(path, verified)
        self.assertEqual([], self.prune()["selected"])
        self.assertTrue((self.base / OLD).exists())
    def test_unknown_ambiguous_old_v1_extra_member_and_hardlink_are_preserved(self):
        work = self.base / OLD
        for mutation in ("extra", "legacy", "hash", "hardlink", "symlink"):
            with self.subTest(mutation=mutation):
                original = {p.name: p.read_bytes() for p in work.iterdir()}
                if mutation == "extra":
                    self.write(work / "PRESERVE", b"protected")
                elif mutation == "legacy":
                    m = json.loads((work / "manifest.json").read_bytes()); m["format_version"] = 1
                    worker.atomic_json(work / "manifest.json", m)
                elif mutation == "hash":
                    self.write(work / "recovery.age", b"x" * len(original["recovery.age"]))
                elif mutation == "hardlink":
                    os.link(work / "recovery.age", self.root / "outside")
                else:
                    (work / "recovery.age").unlink(); (work / "recovery.age").symlink_to(self.base / LATEST / "recovery.age")
                if mutation == "hash":
                    with self.assertRaisesRegex(worker.BackupError, "RETENTION_HASH"):
                        self.prune()
                else:
                    self.assertEqual([], self.prune()["selected"])
                self.assertTrue(work.exists())
                if mutation == "hardlink": (self.root / "outside").unlink()
                for p in work.iterdir(): p.unlink()
                for n, data in original.items(): self.write(work / n, data)
    def test_foreign_directory_symlink_never_followed(self):
        foreign = self.root / "foreign"
        foreign.mkdir(mode=0o700)
        self.write(foreign / "keep", b"preserve")
        (self.base / ("b" * 32)).symlink_to(foreign, target_is_directory=True)
        self.prune(True)
        self.assertEqual(b"preserve", (foreign / "keep").read_bytes())
    def test_remote_extra_nested_object_missing_member_or_corruption_stops_before_journal(self):
        original = dict(self.remote.objects)
        prefix = self.c["remote"] + "/" + OLD + "/"
        for mutation in ("extra", "nested", "missing", "corrupt", "manifest"):
            with self.subTest(mutation=mutation):
                self.remote.objects = dict(original)
                if mutation in ("extra", "nested"): self.remote.objects[prefix + ("extra" if mutation == "extra" else "nested/a")] = b"foreign"
                elif mutation == "missing": self.remote.objects.pop(prefix + "manifest.json")
                else:
                    name = "recovery.age" if mutation == "corrupt" else "manifest.json"
                    self.remote.objects[prefix + name] = b"x" * len(original[prefix + name])
                with self.assertRaises(worker.BackupError): self.prune(True)
                self.assertTrue((self.base / OLD).exists())
                self.assertFalse(any((self.state / "retention-pending").glob("*.json")))
        self.remote.objects = original
    def test_retention_refuses_stale_latest_and_protects_last_good_record(self):
        success = json.loads((self.state / "last-success.json").read_bytes())
        success["snapshot_epoch"] = NOW - 3600
        worker.atomic_json(self.state / "last-success.json", success)
        with self.assertRaisesRegex(worker.BackupError, "RETENTION_BACKUP_UNHEALTHY"): self.prune(True)
        self.assertTrue((self.base / LATEST).exists() and (self.base / OLD).exists())
    def test_pending_after_lost_delete_response_resumes_without_weakening_hash_checks(self):
        self.remote.fail_after_delete = True
        with self.assertRaises(worker.BackupError): self.prune(True)
        journal = self.state / "retention-pending" / (OLD + ".json")
        self.assertTrue(journal.exists())
        self.assertTrue((self.base / OLD / "recovery.age").exists())
        self.assertNotIn(self.c["remote"] + "/" + OLD + "/recovery.age", self.remote.objects)
        self.prune(True)
        self.assertFalse((self.base / OLD).exists() or journal.exists())
        self.assertEqual(1, len(list((self.state / "retention-history").glob("*/*.json"))))
    def test_pending_never_deletes_replaced_or_extra_remote_content(self):
        self.remote.fail_after_delete = True
        with self.assertRaises(worker.BackupError): self.prune(True)
        path = self.c["remote"] + "/" + OLD + "/manifest.json"
        self.remote.objects[path] = b"x" * len(self.remote.objects[path])
        with self.assertRaises(worker.BackupError): self.prune(True)
        self.assertTrue((self.base / OLD).exists())
        self.assertIn(path, self.remote.objects)
    def test_failure_state_separate_and_successful_retry_clears_maintenance_failure(self):
        before = (self.state / "last-success.json").read_bytes()
        self.remote.fail_after_delete = True
        self.assertEqual(1, ops.maintain(worker, self.c, True, self.remote, NOW))
        self.assertEqual(1, worker.status(self.state, NOW))
        self.assertEqual(before, (self.state / "last-success.json").read_bytes())
        self.assertEqual(0, ops.maintain(worker, self.c, True, self.remote, NOW))
        self.assertEqual(0, worker.status(self.state, NOW))
    def test_shared_maintenance_lock_excludes_retention(self):
        with worker.maintenance_lock(), self.assertRaisesRegex(worker.BackupError, "MAINTENANCE_BUSY"):
            self.prune(True)
    def test_listing_output_is_bounded_before_capture_and_excess_reader_is_killed(self):
        class Stream:
            def __init__(self):
                self.reads, self.closed = [], False
            def read(self, size):
                self.reads.append(size)
                return b"x" * size  # Untrusted process could otherwise emit a gigabyte.
            def close(self): self.closed = True
        class Process:
            def __init__(self):
                self.stdout, self.killed = Stream(), False
            def poll(self): return -9 if self.killed else None
            def kill(self): self.killed = True
            def wait(self, timeout): return -9 if self.killed else 0
        process = Process()
        with patch.object(worker.subprocess, "Popen", return_value=process), \
                self.assertRaisesRegex(worker.BackupError, "COMMAND_LIMIT"):
            worker.Runner().read_bounded(["synthetic-rclone", "lsjson"], 16384)
        self.assertEqual([16385], process.stdout.reads)
        self.assertTrue(process.killed and process.stdout.closed)
    def test_large_expired_and_pending_backlog_hashes_only_latest_and_eight_selected(self):
        pending = self.state / "retention-pending"
        pending.mkdir(mode=0o700)
        for i in range(40):
            ident = format(i + 1, "032x")
            self.add(ident, NOW - (40 + i) * 86400)
            if i >= 20:
                record = ops.local_record(worker, self.c, ident, NOW, full=False)
                worker.atomic_json(pending / (ident + ".json"), record)
        original_digest = worker.file_digest
        with patch.object(worker, "file_digest", wraps=original_digest) as checksum:
            result = self.prune()
        self.assertEqual(41, result["eligible"])
        self.assertEqual(8, len(result["selected"]))
        hashed = [call.args[0].parent.name for call in checksum.call_args_list]
        self.assertCountEqual([LATEST, *result["selected"]], hashed)
        self.assertFalse(any("deletefile" in call for call in self.remote.calls))
    def test_batch_is_bounded(self):
        for i in range(12): self.add(format(i + 1, "032x"), NOW - (40 + i) * 86400)
        result = self.prune()
        self.assertEqual(13, result["eligible"])
        self.assertEqual(8, len(result["selected"]))
    def test_read_only_status_never_imports_operations_or_prunes(self):
        with patch.object(worker, "load_config", return_value=self.c), patch.object(worker, "operations") as operations, \
                patch("sys.argv", ["backup.py", "--config", "synthetic", "--status"]), patch.object(worker.time, "time", return_value=NOW):
            self.assertEqual(0, worker.main())
            operations.assert_not_called()
    def test_interrupted_retention_running_state_alerts_when_window_expires(self):
        worker.atomic_json(self.state / "last-retention.json", {"status": "running", "started_epoch": NOW - 3})
        self.assertEqual(0, worker.status(self.state, NOW))
        worker.atomic_json(self.state / "last-retention.json", {"status": "running", "started_epoch": NOW - 1200})
        self.assertEqual(1, worker.status(self.state, NOW))
    def test_installer_snapshots_operations_helper_without_activation(self):
        installer = load("install.py")
        original = ROOT / "scripts/ops/hourly-backup"
        source = self.root / "ops/hourly-backup"
        (self.root / "ops").mkdir(mode=0o700)
        source.mkdir(mode=0o700)
        for name in ("backup.py", "operations.py", "installed-provenance.json", *installer.UNITS):
            self.write(source / name, (original / name).read_bytes())
        for name in (*worker.HELPER_HASHES, "supervised-archive.py", "supervised-operations.py"):
            self.write(source.parent / name, (original.parent / name).read_bytes())
        units, destination = self.root / "units", self.root / "new-installation"
        units.mkdir(mode=0o700)
        with patch.object(installer.backup, "LOCK", self.lock):
            installer.prepare(source, destination, units)
        self.assertEqual((source / "operations.py").read_bytes(), (destination / "hourly-backup/operations.py").read_bytes())
        hashes = json.loads((destination / "source-hashes.json").read_bytes())
        self.assertEqual(worker.digest((source / "operations.py").read_bytes()), hashes["hourly-backup/operations.py"])
        self.assertIn(b"--status", (units / "hf-one-backup-check.service").read_bytes())
    def test_config_optional_keys_preserve_legacy_contract_and_enforce_thirty_days(self):
        full = {"version": 2, **{n: str(self.root / n) for n in ("base", "state", "keys", "recipient", "rclone_config")},
                "sources": {"config": str(self.root / "source-config"), "tls": str(self.root / "source-tls")},
                "bind_ip": "8.8.8.8", "remote": "synthetic:bucket/recovery", "postgres": "pg",
                "containers": ["backend", "web", "pg"], "maintenance_lock": str(worker.LOCK)}
        for n in ("keys", "source-config", "source-tls"): (self.root / n).mkdir(mode=0o700)
        for n in ("recipient", "rclone_config"): self.write(self.root / n, b"synthetic-only")
        path = self.root / "config.json"
        worker.atomic_json(path, full)
        self.assertNotIn("retention", worker.load_config(path))
        worker.atomic_json(path, {**full, "retention": {"days": 30, "apply": False}})
        self.assertFalse(worker.load_config(path)["retention"]["apply"])
        for bad in (29, 31, True, 30.0):
            worker.atomic_json(path, {**full, "retention": {"days": bad, "apply": False}})
            with self.assertRaises(worker.BackupError): worker.load_config(path)

if __name__ == "__main__":
    unittest.main()
