"""Synthetic auto-v1/v2 contracts and failure paths; no operational configuration."""
from contextlib import nullcontext, redirect_stdout
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
OPS = ROOT / "scripts/ops"


def load(path):
    spec = importlib.util.spec_from_file_location("test_" + path.stem.replace("-", "_"), path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


old = load(ROOT / "scripts/ci/test_human_recovery.py")
worker = load(OPS / "hourly-backup/backup.py")


def automatic(version=2):
    legacy = old.fixture()
    db = {Path(n).name: v for n, v in legacy.items() if n.startswith("db/")}
    package = worker.make_package(db, {"key-synthetic.xml": b"<key>synthetic</key>"},
        {"config/synthetic.env": b"CONFIG_SECRET_SENTINEL", "tls/synthetic.conf": b"synthetic-tls"},
        b"-- role/grants map without passwords", b"-- schema/ownership map", [{"Config": {"Env": ["RAW_ENV_SENTINEL"]}}], 1)
    with tarfile.open(fileobj=io.BytesIO(package)) as tar:
        values = {m.name: tar.extractfile(m).read() for m in tar}
    if version == 1:
        values.pop("metadata/format.json")
        key = next(n for n in values if n.startswith("keyring/") and n.endswith(".tar"))
        values["keyring/keyring.tar"] = values.pop(key)
        values.pop(key + ".sha256")
        values["keyring/keyring.tar.sha256"] = (worker.digest(values["keyring/keyring.tar"]) + "  keyring.tar\n").encode()
        values.pop("metadata/internal-sha256.txt")
    return rehash(values, version)


def rehash(values, version):
    name = "internal-sha256.txt" if version == 1 else "metadata/internal-sha256.txt"
    values[name] = "".join(worker.digest(v) + "  " + n + "\n" for n, v in sorted(values.items()) if n != name).encode()
    return values


class BackupContracts(unittest.TestCase):
    # Reuse the existing private ACL/root fixture, not another recovery implementation.
    setUp = old.HumanTests.setUp
    tearDown = old.HumanTests.tearDown
    private_parent = old.HumanTests.private_parent
    def test_auto_v1_v2_normalize_only_after_full_validation_with_source_identity(self):
        for version in (1, 2):
            with self.subTest(version=version):
                values = automatic(version)
                tar = self.folder / ("auto" + str(version) + ".tar")
                tar.write_bytes(old.tar_bytes(values))
                parent = self.private_parent() if os.name == "nt" else self.folder
                output, offline = parent / "output", parent / "offline"
                manifest = self.archive.filtered_export(tar, output, self.value["recovery_id"], self.value["ciphertext_sha256"], offline)
                self.assertEqual(self.value["ciphertext_sha256"], manifest["ciphertext_sha256"])
                self.assertRegex(manifest["keyring"], r"keyring/keyring-20261009T170757Z-1.tar")
                self.assertNotIn("metadata/containers.private.json", manifest["files"])
                self.assertNotIn("metadata/roles.sql", manifest["files"])
                payload = old.tar_bytes({n: (output / n).read_bytes() for n in (*manifest["files"], "transfer.json")})
                self.h.transfer_plan(self.value, payload)
                self.assertNotIn(b"RAW_ENV_SENTINEL", payload)
                self.assertNotIn(b"CONFIG_SECRET_SENTINEL", payload)
                with tarfile.open(offline / "host-config.tar") as config:
                    self.assertEqual(b"CONFIG_SECRET_SENTINEL", config.extractfile("config/synthetic.env").read())
                self.assertEqual("auto-v" + str(version), json.loads((offline / "source.json").read_bytes())["format"])
                shutil.rmtree(output)
                shutil.rmtree(offline)

    def test_automatic_ambiguity_hash_coverage_version_members_links_and_limits_deny_before_output(self):
        original = automatic()
        cases = []
        def add(name, value, recalculate=True):
            changed = dict(original)
            changed[name] = value
            cases.append(rehash(changed, 2) if recalculate else changed)
        add("metadata/format.json", b'{"format":"hfpos-auto","version":2,"version":2}')
        add("metadata/format.json", b'{"format":"hfpos-auto","version":3}')
        add("metadata/unexpected.json", b"synthetic")
        add("internal-sha256.txt", b"ambiguous")
        add("metadata/capture.json", b"tampered", False)
        add("keyring/keyring.tar", b"ambiguous")
        add("snapshot/host-config.tar", old.tar_bytes({"../escape": b"denied"}))
        add("metadata/internal-sha256.txt", original["metadata/internal-sha256.txt"] * 2, False)
        for i, values in enumerate(cases):
            tar = self.folder / (str(i) + ".tar")
            tar.write_bytes(old.tar_bytes(values))
            with self.subTest(case=i), self.assertRaises((self.archive.ops.Denied, ValueError, tarfile.TarError)):
                self.archive.filtered_export(tar, self.folder / "denied", self.value["recovery_id"], self.value["ciphertext_sha256"])
            self.assertFalse((self.folder / "denied").exists())
        for kind in (tarfile.SYMTYPE, tarfile.LNKTYPE):
            stream = io.BytesIO()
            with tarfile.open(fileobj=stream, mode="w") as t:
                m = tarfile.TarInfo("snapshot/host-config.tar")
                m.type, m.linkname = kind, "/outside"
                t.addfile(m)
            tar.write_bytes(stream.getvalue())
            with self.assertRaises(self.archive.ops.Denied):
                self.archive.filtered_export(tar, self.folder / "denied", self.value["recovery_id"], self.value["ciphertext_sha256"])
        for attribute, value in (("MAX_FILE", 10), ("MAX_TOTAL", 100), ("MAX_MEMBERS", 3)):
            tar.write_bytes(old.tar_bytes(original))
            with patch.object(self.archive, attribute, value), self.assertRaises(self.archive.ops.Denied):
                self.archive.filtered_export(tar, self.folder / "denied", self.value["recovery_id"], self.value["ciphertext_sha256"])
        tar.write_bytes(old.tar_bytes(original) + old.tar_bytes({"metadata/hidden.txt": b"not-covered"}))
        with self.assertRaises(self.archive.ops.Denied):
            self.archive.filtered_export(tar, self.folder / "denied", self.value["recovery_id"], self.value["ciphertext_sha256"])

    def test_late_age_authentication_failure_and_watchdog_never_export_offline_or_transfer(self):
        cipher = self.folder / "cipher.age"
        cipher.write_bytes(b"synthetic-ciphertext")
        tar = self.folder / "plaintext.tar"
        tar.write_bytes(old.tar_bytes(automatic()))
        parent = self.private_parent() if os.name == "nt" else self.folder
        identity = parent / "identity.age"
        self.archive.private_write(identity, b"synthetic-only")
        if os.name == "nt":
            cipher = parent / "cipher.age"
            self.archive.private_write(cipher, b"synthetic-ciphertext")
        real = subprocess.Popen
        for hanging in (False, True):
            def fake_age(args, **kwargs):
                if args[0] != "age":
                    return real(args, **kwargs)
                code = "import pathlib,sys,time;sys.stdout.buffer.write(pathlib.Path(sys.argv[1]).read_bytes());sys.stdout.flush();" + ("time.sleep(30)" if hanging else "sys.exit(1)")
                return real([sys.executable, "-c", code, str(tar)], **kwargs)
            real_timer = self.archive.threading.Timer
            with patch.object(self.archive.subprocess, "Popen", side_effect=fake_age), \
                    patch.object(self.archive.threading, "Timer", side_effect=lambda seconds, callback: real_timer(0.3, callback)), \
                    self.assertRaises(self.archive.ops.Denied):
                self.archive.decrypt_export(cipher, identity, parent, parent / "output", self.value["recovery_id"],
                    worker.digest(cipher.read_bytes()), offline_config_output=parent / "offline")
            self.assertFalse((parent / "output").exists() or (parent / "offline").exists())
            self.assertFalse(any(p.name.startswith("supervised-") for p in parent.iterdir()))


class WorkerTests(unittest.TestCase):
    def test_installed_identity_and_official_helpers_not_copied_or_modified(self):
        p = json.loads((OPS / "hourly-backup/installed-provenance.json").read_bytes())
        self.assertEqual(39947, p["installer_bytes"])
        self.assertEqual(7, len(p["payload"]))
        for n, expected in worker.HELPER_HASHES.items():
            self.assertEqual(expected, worker.digest((OPS / n).read_bytes().replace(b"\r\n", b"\n")))
            self.assertEqual(expected, p["payload"][n])
            self.assertFalse((OPS / "hourly-backup" / n).exists())

    def test_online_capture_is_subset_not_equal_and_does_not_replace_shared_lock(self):
        worker.require_key_superset({"key-a.xml": b"old"}, {"key-a.xml": b"old", "key-b.xml": b"new"})
        for after in ({}, {"key-a.xml": b"changed"}):
            with self.assertRaises(worker.BackupError):
                worker.require_key_superset({"key-a.xml": b"old"}, after)
        with self.assertRaises(worker.BackupError):
            worker.same_config(b"schema1", b"schema2")
        text = (OPS / "hourly-backup/backup.py").read_text()
        self.assertLess(text.index("with maintenance_lock():"), text.index('containers = json.loads(run('))
        self.assertIn("LOCK_EX | fcntl.LOCK_NB", text)
        guide = (ROOT / "docs/hourly-backup-recovery.md").read_text()
        deployment = (ROOT / "docs/deployment-runbook.md").read_text()
        self.assertIn("flock -n " + worker.LOCK.as_posix(), guide)
        self.assertIn(worker.LOCK.as_posix(), deployment)
        self.assertIn("NO sustituyen lock", guide)

    def test_r2_hash_manifest_status_age_and_command_timeout_are_failclosed(self):
        for args in ((b"a", b"b", b"m", b"m"), (b"a", b"a", b"m", b"wrong")):
            with self.assertRaises(worker.BackupError):
                worker.verify_pullback(*args)
        with tempfile.TemporaryDirectory() as directory, redirect_stdout(io.StringIO()) as output:
            state = Path(directory)
            (state / "last-success.json").write_bytes(worker.canonical({"snapshot_epoch": 100}))
            (state / "last-run.json").write_bytes(worker.canonical({"status": "success"}))
            self.assertEqual(0, worker.status(state, 3699))
            for now in (99, 3700):
                self.assertEqual(1, worker.status(state, now))
            (state / "last-run.json").write_bytes(worker.canonical({"status": "failed"}))
            self.assertEqual(1, worker.status(state, 101))
            self.assertNotIn("snapshot_epoch", output.getvalue())
        with patch.object(worker.subprocess, "run", side_effect=subprocess.TimeoutExpired("SYNTHETIC_SECRET_SENTINEL", 1)):
            with self.assertRaisesRegex(worker.BackupError, "^TIMEOUT$"):
                worker.Runner().run(["synthetic"])

    @unittest.skipUnless(os.name == "posix" and os.geteuid() == 0, "Linux operational CLI contract without services")
    def test_maintenance_denial_marks_last_attempt_failed_and_never_calls_capture(self):
        with tempfile.TemporaryDirectory(dir="/root") as directory, redirect_stdout(io.StringIO()) as output:
            with patch.object(sys, "argv", ["backup.py", "--config", "synthetic"]), \
                    patch.object(worker, "load_config", return_value={"state": directory}), \
                    patch.object(worker, "backup", side_effect=worker.BackupError("MAINTENANCE_BUSY")), \
                    patch.object(worker, "capture_tree") as capture:
                self.assertEqual(1, worker.main())
            capture.assert_not_called()
            self.assertEqual("failed", json.loads((Path(directory) / "last-run.json").read_bytes())["status"])
            self.assertEqual("BACKUP_FAILED_MAINTENANCE_BUSY\n", output.getvalue())

    @unittest.skipUnless(os.name == "posix" and os.geteuid() == 0, "Root private external configuration contract")
    def test_external_config_rejects_overlap_unknown_keys_symlinks_and_wrong_lock(self):
        with tempfile.TemporaryDirectory(dir="/root") as directory:
            root = Path(directory)
            c = {"version": 2, "bind_ip": "192.0.2.1", "remote": "synthetic:bucket/recovery", "postgres": "pg",
                 "containers": ["be", "web", "pg"], "maintenance_lock": str(worker.LOCK)}
            for n in ("base", "state", "keys", "config", "tls"):
                (root / n).mkdir(mode=0o700)
                if n in ("base", "state", "keys"):
                    c[n] = str(root / n)
            c["sources"] = {n: str(root / n) for n in ("config", "tls")}
            for n in ("recipient", "rclone_config"):
                (root / n).touch(mode=0o600)
                c[n] = str(root / n)
            config = root / "backup.json"
            config.touch(mode=0o600)
            from types import SimpleNamespace
            with patch.object(worker.ipaddress, "ip_address", return_value=SimpleNamespace(is_global=True)):
                config.write_bytes(worker.canonical(c))
                self.assertEqual(c, worker.load_config(config))
                for changed in (dict(c, base=c["keys"]), dict(c, unknown="denied"), dict(c, maintenance_lock="/other/lock"),
                                dict(c, remote="synthetic:bucket/../unsafe"), dict(c, version=True)):
                    config.write_bytes(worker.canonical(changed))
                    with self.assertRaises(worker.BackupError):
                        worker.load_config(config)
                config.write_bytes(worker.canonical(c))
                link = root / "link.json"
                link.symlink_to(config)
                with self.assertRaises(worker.BackupError):
                    worker.load_config(link)

    @unittest.skipUnless(os.name == "posix" and os.geteuid() == 0, "Linux synthetic producer with command doubles")
    def test_producer_failures_withdraw_only_new_sensitive_staging_and_never_publish_success(self):
        for failure in ("dump", "age", "r2", "pullback", "timeout", "cleanup", None):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory(dir="/root") as directory:
                root = Path(directory)
                base, state = root / "base", root / "state"
                for p in (base, state):
                    p.mkdir(mode=0o700)
                historical = base / "historical.age"
                historical.write_bytes(b"untouched-history")
                recipient, rconf = root / "recipient.pub", root / "rclone.conf"
                recipient.write_bytes(b"synthetic-recipient")
                rconf.write_bytes(b"SYNTHETIC_RCLONE_SECRET")
                for p in (recipient, rconf):
                    p.chmod(0o600)
                c = {"base": str(base), "state": str(state), "postgres": "synthetic-pg", "containers": ["be", "web", "pg"],
                    "sources": {"config": str(root / "config"), "tls": str(root / "tls")}, "keys": str(root / "keys"),
                    "recipient": str(recipient), "rclone_config": str(rconf), "bind_ip": "192.0.2.1", "remote": "synthetic:bucket"}
                copied = root / "code"
                copied.mkdir(mode=0o700)
                (copied / "hourly-backup").mkdir(mode=0o700)
                for n in (*worker.HELPER_HASHES, "supervised-archive.py", "supervised-operations.py"):
                    (copied / n).write_bytes((OPS / n).read_bytes().replace(b"\r\n", b"\n"))
                    (copied / n).chmod(0o600)
                containers = [{"Id": n, "Image": "synthetic", "Config": {"Env": ["RAW_ENV_SENTINEL"]}, "Mounts": [],
                    "State": {"Running": True, "Health": {"Status": "healthy"}}} for n in c["containers"]]
                calls, remote = [], {}
                class SyntheticRunner:
                    deadline = time.monotonic() + 1100
                    def run(self, args, data=None, **kwargs):
                        calls.append(args)
                        if args[0] == "docker":
                            if args[1] == "inspect":
                                return worker.canonical(containers)
                            if data and b"pg_database_size" in data:
                                return b"1000"
                            if data and b"postgres-backup.sh" in data:
                                if failure in ("dump", "timeout"):
                                    raise worker.BackupError("TIMEOUT" if failure == "timeout" else "COMMAND_FAILED")
                            if args[1] == "cp" and "/db/." in args[2]:
                                for n, v in old.fixture().items():
                                    if n.startswith("db/"):
                                        p = Path(args[3]) / Path(n).name
                                        p.write_bytes(v)
                                        p.chmod(0o600)
                            if "rm -rf" in " ".join(args) and failure == "cleanup":
                                raise worker.BackupError("COMMAND_FAILED")
                            return b"-- synthetic metadata"
                        if args[0] == "age":
                            if failure == "age":
                                raise worker.BackupError("COMMAND_FAILED")
                            Path(args[args.index("-o") + 1]).write_bytes(b"synthetic-encrypted-only")
                            return b""
                        if args[0] == "rclone":
                            if failure == "r2":
                                raise worker.BackupError("COMMAND_FAILED")
                            source, target = args[-2:]
                            if source.startswith("synthetic:"):
                                Path(target).write_bytes(b"corrupt" if failure == "pullback" else remote[source])
                            else:
                                remote[target] = Path(source).read_bytes()
                            return b""
                        raise AssertionError("Unexpected synthetic command")
                runner = SyntheticRunner()
                def tree(path, flat=False, owners=None):
                    return {"key-synthetic.xml": b"<key>synthetic</key>"} if flat else {"synthetic.env": b"SENSITIVE_CONFIG"}
                with patch.object(worker, "maintenance_lock", return_value=nullcontext()), \
                        patch.object(worker, "capture_tree", side_effect=tree), \
                        patch.object(worker, "HERE", copied / "hourly-backup"), \
                        patch.object(worker, "Runner", return_value=runner), redirect_stdout(io.StringIO()):
                    if failure:
                        with self.assertRaises(worker.BackupError):
                            worker.backup(c, runner)
                    else:
                        self.assertEqual(0, worker.backup(c, runner))
                self.assertEqual(b"untouched-history", historical.read_bytes())
                self.assertFalse(any(p.name.startswith("staging-") for p in base.rglob("*")))
                result = json.loads((state / "last-run.json").read_bytes())
                self.assertEqual("failed" if failure else "success", result["status"])
                self.assertEqual(not failure, (state / "last-success.json").exists())
                if failure == "cleanup":
                    self.assertEqual("HUMAN_CONTAINMENT_REQUIRED", result["staging"])
                self.assertFalse(any("stop" in a or "restart" in a or "pull" in a for a in calls))

    @unittest.skipUnless(os.name == "posix" and os.geteuid() == 0, "Synthetic source UID/GID contract")
    def test_service_owned_keyring_and_web_tls_read_without_chown_and_reject_arbitrary_owner(self):
        with tempfile.TemporaryDirectory(dir="/root") as d:
            root = Path(d)
            for name, uid, flat in (("keyring", 1654, True), ("tls", 101, False)):
                source = root / name
                source.mkdir(mode=0o700)
                file = source / ("key-synthetic.xml" if flat else "private.key")
                file.write_bytes(b"synthetic-only")
                file.chmod(0o600)
                os.chown(file, uid, uid)
                os.chown(source, uid, uid)
                before = source.stat(), file.stat()
                tree = worker.capture_tree(source, flat=flat, owners={0, uid})
                self.assertEqual((b"synthetic-only", 0o600, uid, uid), tree[file.name])
                self.assertEqual((before[0].st_uid, before[1].st_uid), (source.stat().st_uid, file.stat().st_uid))
                with self.assertRaises(worker.BackupError):
                    worker.capture_tree(source, flat=flat)  # Root-only paths remain strict.
                os.chown(file, 12345, 12345)
                with self.assertRaises(worker.BackupError):
                    worker.capture_tree(source, flat=flat, owners={0, uid})
                os.chown(file, uid, uid)
                file.chmod(0o620)
                with self.assertRaises(worker.BackupError):
                    worker.capture_tree(source, flat=flat, owners={0, uid})

    @unittest.skipUnless(os.name == "posix" and os.geteuid() == 0, "Linux owned staging/maintenance contract")
    def test_shared_lock_conflict_and_owned_cleanup_never_delete_foreign_link_or_history(self):
        import fcntl
        with tempfile.TemporaryDirectory(dir="/root") as d:
            base = Path(d)
            lock = base / "lock"
            lock.touch(mode=0o600)
            with patch.object(worker, "LOCK", lock):
                with worker.maintenance_lock():
                    with self.assertRaisesRegex(worker.BackupError, "MAINTENANCE_BUSY"):
                        with worker.maintenance_lock():
                            self.fail("must not acquire shared maintenance lock")
            staging = base / "staging-synthetic"
            staging.mkdir(mode=0o700)
            i = staging.stat()
            old = base / "history.age"
            old.write_bytes(b"preserve")
            old.chmod(0o600)
            (staging / "foreign").symlink_to(old)
            with self.assertRaises(worker.BackupError):
                worker.clean_owned(staging, (base, i.st_dev, i.st_ino))
            self.assertEqual(b"preserve", old.read_bytes())
            (staging / "foreign").unlink()
            (staging / "secret").write_bytes(b"synthetic-sensitive")
            (staging / "secret").chmod(0o600)
            worker.clean_owned(staging, (base, i.st_dev, i.st_ino))
            self.assertFalse(staging.exists())
            self.assertTrue(old.exists())


if __name__ == "__main__":
    unittest.main()
