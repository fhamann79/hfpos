"""Focused synthetic tests of the human entrypoints; no app, live host or secrets."""
import argparse
import base64
from contextlib import redirect_stdout
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
import tempfile
import unittest
from unittest.mock import patch

# Keep the sealed source fixture exact even when this test is invoked without -B.
sys.dont_write_bytecode = True

OPS = Path(__file__).resolve().parents[2] / "scripts/ops"
NAMES = ("human-recovery.py", "human-recovery-windows.py", "human-recovery-transfer.py", "human-recovery-local.ps1",
         "supervised-operations.py", "supervised-archive.py", "supervised-local-acl.ps1", "postgres-restore.sh",
         "verify-postgres-backup.sh", "verify-restored-db.sh", "keyring-restore.sh")


def load(path):
    spec = importlib.util.spec_from_file_location("test_" + path.stem.replace("-", "_"), path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def tar_bytes(files):
    data = io.BytesIO()
    with tarfile.open(fileobj=data, mode="w", format=tarfile.USTAR_FORMAT) as archive:
        for name, content in files.items():
            member = tarfile.TarInfo(name)
            member.mode, member.size = 0o600, len(content)
            archive.addfile(member, io.BytesIO(content))
    return data.getvalue()


def fixture(dump=b"synthetic-dump"):
    d = "db/hfpos-20261009T170757Z-1.dump"
    k = "keyring/keyring-20261009T170757Z-1.tar"
    key = tar_bytes({"./key-synthetic.xml": b"<key>synthetic-only</key>"})
    digest = hashlib.sha256(dump).hexdigest()
    values = {d: dump, d + ".sha256": (digest + "  " + Path(d).name + "\n").encode(),
              d + ".manifest": ("format_version=1\ncreated_utc=20261009T170757Z\npostgres_client_version=16\n"
                                  "app_release=synthetic\ndatabase_name=synthetic\nsha256=" + digest + "\n").encode(),
              k: key, k + ".sha256": (hashlib.sha256(key).hexdigest() + "  " + Path(k).name + "\n").encode(),
              "metadata/release.txt": b"synthetic-only", "snapshot/config.tar": b"SYNTHETIC_SECRET_SENTINEL"}
    values["metadata/internal-sha256.txt"] = "".join(hashlib.sha256(value).hexdigest() + "  " + name + "\n"
              for name, value in values.items()).encode()
    return values


class HumanTests(unittest.TestCase):
    def setUp(self):
        parent = "/root" if os.name == "posix" and os.geteuid() == 0 else None
        self.temp = tempfile.TemporaryDirectory(prefix="hfpos-human-test-", dir=parent)
        self.folder = Path(self.temp.name).resolve()
        self.bundle = self.folder / "code"
        self.bundle.mkdir(mode=0o700)
        for name in NAMES:
            file = self.bundle / name
            file.write_bytes((OPS / name).read_bytes().replace(b"\r\n", b"\n"))
            file.chmod(0o600)
        (self.bundle / "docker-empty").mkdir(mode=0o700)
        self.h = load(self.bundle / "human-recovery.py")
        (self.bundle / "source.json").write_bytes(self.h.canonical({"source_sha": "a" * 40, "code_sha256": self.h.digest_code()}))
        (self.bundle / "source.json").chmod(0o600)
        self.ops, self.archive = self.h.load_helpers()
        self.w = load(self.bundle / "human-recovery-windows.py")
        self.w.human = self.h
        self.value = {"version": 1, "source_sha": "a" * 40, "code_sha256": self.h.digest_code(),
                      "recovery_id": secrets.token_hex(16), "ciphertext_sha256": "c" * 64,
                      "workspace_device": 0, "workspace_inode": 0, "image_id": "sha256:" + "d" * 64}

    def tearDown(self):
        self.temp.cleanup()

    def payload(self, dump=b"synthetic-dump"):
        tar = self.folder / (secrets.token_hex(8) + ".tar")
        tar.write_bytes(tar_bytes(fixture(dump)))
        output = self.folder / ("export-" + secrets.token_hex(8))
        # Windows fixtures here use native ACL private parent when needed.
        if os.name == "nt":
            output = self.private_parent() / output.name
        manifest = self.archive.filtered_export(tar, output, self.value["recovery_id"], self.value["ciphertext_sha256"])
        return tar_bytes({name: (output / name).read_bytes() for name in (*manifest["files"], "transfer.json")})

    def private_parent(self):
        path = self.folder / ("private " + secrets.token_hex(4))
        path.mkdir()
        command = ("$a=New-Object Security.AccessControl.DirectorySecurity;$a.SetAccessRuleProtection($true,$false);"
                   "$u=[Security.Principal.WindowsIdentity]::GetCurrent().User;$a.SetOwner($u);"
                   "$a.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($u,'FullControl','ContainerInherit,ObjectInherit','None','Allow')));"
                   "Set-Acl -LiteralPath '" + str(path).replace("'", "''") + "' -AclObject $a")
        env = {k: v for k, v in os.environ.items() if k.lower() != "psmodulepath"}
        result = subprocess.run(["powershell.exe", "-NoProfile", "-EncodedCommand", base64.b64encode(command.encode("utf-16le")).decode()],
                                capture_output=True, env=env)
        self.assertEqual(0, result.returncode, "Synthetic ACL setup failed")
        return path

    def test_source_seal_includes_every_new_helper_and_detects_changes(self):
        self.assertEqual(set(NAMES), set(self.h.FILES))
        digest = self.h.digest_code()
        for name in ("human-recovery-transfer.py", "human-recovery-windows.py", "human-recovery-local.ps1"):
            file = self.bundle / name
            before = file.read_bytes()
            file.write_bytes(before + b"\n# synthetic change\n")
            self.assertNotEqual(digest, self.h.digest_code())
            file.write_bytes(before)

    def test_helper_imports_preserve_exact_bootstrap_fixture_members(self):
        self.h.load_helpers()
        load(self.bundle / "human-recovery-windows.py")
        self.assertTrue(sys.dont_write_bytecode)
        self.assertEqual(set(NAMES) | {"source.json", "docker-empty"}, {p.name for p in self.bundle.iterdir()})

    def test_existing_readiness_retries_transient_failure_and_stops_at_bound(self):
        self.h.bind_helpers(self.ops)
        with patch.object(self.h, "docker", side_effect=[ValueError("synthetic native diagnostic"), ValueError("synthetic native diagnostic"),
                                                       b"", b"synthetic-sql"]) as docker, \
             patch.object(self.ops.time, "sleep") as sleep:
            self.ops.wait_postgres_tcp("synthetic-recovery", "synthetic", "synthetic")
            self.assertEqual(3, docker.call_count)
            self.assertEqual(2, sleep.call_count)
            self.assertTrue(all(call.args[2] == "pg_isready" for call in docker.call_args_list))
            self.assertEqual(("exec", "synthetic-recovery", "pg_isready", "-h", "127.0.0.1", "-p", "5432", "-t", "1",
                              "-U", "synthetic", "-d", "synthetic"), docker.call_args.args)
            self.assertEqual({"data": None, "timeout": 5}, docker.call_args.kwargs)
            self.assertEqual(b"synthetic-sql", self.ops.docker("exec", "synthetic-recovery", "psql", "-c", "SELECT 1;"))
            self.assertEqual(4, docker.call_count, "SQL follows exactly three readiness attempts")
            self.assertEqual("psql", docker.call_args.args[2])
        with patch.object(self.h, "docker", side_effect=ValueError("synthetic native diagnostic")) as docker, \
             patch.object(self.ops.time, "sleep") as sleep:
            with self.assertRaises(self.ops.Denied) as error:
                self.ops.wait_postgres_tcp("synthetic-recovery", "synthetic", "synthetic")
            self.assertEqual(30, docker.call_count)
            self.assertEqual(30, sleep.call_count)
            self.assertEqual("", str(error.exception))

    def test_envelope_exact_schema_hash_duplicates_and_app_blocked(self):
        self.h.validate_envelope(self.value)
        for changed in (dict(self.value, version=True), dict(self.value, app_start=True), dict(self.value, code_sha256="f" * 64),
                        dict(self.value, recovery_id="../x"), dict(self.value, workspace_inode=True)):
            with self.assertRaises(ValueError):
                self.h.validate_envelope(changed)
        with self.assertRaises(ValueError):
            self.h.decode(b'{"version":1,"version":1}')

    def test_transfer_allowlist_hashes_unsafe_tar_and_no_snapshot(self):
        payload = self.payload()
        plan, manifest = self.h.transfer_plan(self.value, payload)
        self.assertEqual(hashlib.sha256(payload).hexdigest(), plan["transfer_sha256"])
        self.assertNotIn(b"SYNTHETIC_SECRET_SENTINEL", payload)
        self.assertFalse(any(name.startswith("snapshot/") for name in manifest["files"]))
        for payload in (tar_bytes({"../unsafe": b"x"}), tar_bytes({"snapshot/config.tar": b"x"}), b"not a tar"):
            with self.assertRaises((ValueError, tarfile.TarError)):
                self.h.transfer_plan(self.value, payload)

    def test_legacy_extra_metadata_is_validated_but_never_transferred(self):
        values = fixture()
        values["metadata/previous-operator-note.json"] = b"SYNTHETIC_SECRET_SENTINEL_EXTRA"
        values["metadata/internal-sha256.txt"] = "".join(hashlib.sha256(v).hexdigest() + "  " + n + "\n"
            for n, v in values.items() if n != "metadata/internal-sha256.txt").encode()
        tar = self.folder / "extra.tar"
        tar.write_bytes(tar_bytes(values))
        parent = self.private_parent() if os.name == "nt" else self.folder
        output = parent / "legacy-extra"
        manifest = self.archive.filtered_export(tar, output, self.value["recovery_id"], self.value["ciphertext_sha256"])
        self.assertNotIn("metadata/previous-operator-note.json", manifest["files"])
        self.assertFalse(any(b"SYNTHETIC_SECRET_SENTINEL" in (output / n).read_bytes() for n in manifest["files"]))
        values["metadata/previous-operator-note.json"] += b"corrupt"
        tar.write_bytes(tar_bytes(values))
        with self.assertRaises(self.archive.ops.Denied):
            self.archive.filtered_export(tar, parent / "corrupt-export", self.value["recovery_id"], self.value["ciphertext_sha256"])
        self.assertFalse((parent / "corrupt-export").exists())

    def test_docker_fixed_endpoint_env_and_config_no_global_credentials(self):
        with patch.object(self.h.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, b"PASS", b"")) as run:
            with patch.dict(os.environ, {"DOCKER_HOST": "tcp://synthetic.invalid:2375", "DOCKER_CONTEXT": "production", "DOCKER_CONFIG": "unsafe"}):
                self.h.docker("ps")
        self.assertEqual(["/usr/bin/docker", "--host", "unix:///var/run/docker.sock", "--config", str((self.bundle / "docker-empty").resolve()), "ps"], run.call_args.args[0])
        self.assertEqual({"PATH": "/usr/bin:/bin", "HOME": "/nonexistent", "LANG": "C"}, run.call_args.kwargs["env"] if os.name == "posix" else
                         {"PATH": "/usr/bin:/bin", "HOME": "/nonexistent", "LANG": "C"})

    def test_missing_image_no_pull_and_effective_uid_before_sensitive_work(self):
        with patch.object(self.h, "docker", side_effect=ValueError) as docker:
            with self.assertRaises(ValueError):
                self.h.image_check()
            self.assertEqual(("image", "inspect", self.h.IMAGE), docker.call_args.args)
        image = {"Os": "linux", "Architecture": "amd64", "RepoDigests": ["postgres@" + self.h.IMAGE.split("@")[1]],
                 "Config": {"Entrypoint": ["docker-entrypoint.sh"], "Cmd": ["postgres"]}, "Id": self.value["image_id"]}
        with patch.object(self.h, "docker", side_effect=[self.h.canonical([image]), b"pg_restore (PostgreSQL) 16.15\n"]) as docker:
            self.assertEqual(self.value["image_id"], self.h.image_check())
            self.assertIn("--pull=never", docker.call_args.args)
            self.assertIn("999:999", docker.call_args.args)
            self.assertIn("grep -q '^postgres:x:999:999:'", docker.call_args.args[-1])

    def test_keyboard_interrupt_is_stop_no_traceback(self):
        with patch.object(self.h, "load_helpers", side_effect=KeyboardInterrupt), redirect_stdout(io.StringIO()) as output:
            self.assertEqual(1, self.h.main(["verify", "--recovery-id", self.value["recovery_id"]]))
        self.assertIn("STOP_APP_BLOCKED", output.getvalue())

    def test_runbook_root_snapshot_block_syntax_and_fixed_list(self):
        guide = (OPS.parents[1] / "docs/human-isolated-recovery.md").read_text(encoding="utf-8")
        code = re.search(r"# HUMAN_ROOT_SNAPSHOT_BEGIN.*?<<'PY'\n(.*?)\nPY\n# HUMAN_ROOT_SNAPSHOT_END", guide, re.S)[1]
        compile(code, "runbook-root-snapshot", "exec")
        for name in NAMES:
            self.assertIn('"' + name + '"', code)

    @unittest.skipUnless(os.name == "posix" and os.geteuid() == 0, "Exact stdlib root runbook block")
    def test_runbook_exact_block_verifies_before_writes_corruption_links_and_collision(self):
        guide = (OPS.parents[1] / "docs/human-isolated-recovery.md").read_text(encoding="utf-8")
        code = re.search(r"# HUMAN_ROOT_SNAPSHOT_BEGIN.*?<<'PY'\n(.*?)\nPY\n# HUMAN_ROOT_SNAPSHOT_END", guide, re.S)[1]
        sha = secrets.token_hex(20)
        target = Path("/root") / ("hfpos-human-code-" + sha)
        self.assertFalse(target.exists(), "Never adopt a preexisting test target")
        manifest = self.bundle / "source.json"
        manifest.write_bytes(self.h.canonical({"source_sha": sha, "code_sha256": self.h.digest_code()}))
        digest = self.h.digest_code()
        def execute(expected=digest):
            return subprocess.run([sys.executable, "-I", "-", str(self.bundle), str(target), sha, expected],
                                  input=code.encode(), capture_output=True, timeout=20)
        try:
            self.assertNotEqual(0, execute("f" * 64).returncode)
            self.assertFalse(target.exists())
            file = self.bundle / "human-recovery-transfer.py"
            before = file.read_bytes()
            file.write_bytes(before + b"\n# synthetic corruption\n")
            self.assertNotEqual(0, execute().returncode)
            self.assertFalse(target.exists())
            file.write_bytes(before)
            good_manifest = manifest.read_bytes()
            for bad in (b'{}', self.h.canonical({"source_sha": "b" * 40, "code_sha256": digest}),
                        b'{"source_sha":"a","source_sha":"a"}'):
                manifest.write_bytes(bad)
                self.assertNotEqual(0, execute().returncode)
                self.assertFalse(target.exists())
            manifest.write_bytes(good_manifest)
            file.unlink()
            file.symlink_to(self.bundle / "human-recovery.py")
            self.assertNotEqual(0, execute().returncode)
            self.assertFalse(target.exists())
            file.unlink()
            file.write_bytes(before)
            file.chmod(0o600)
            self.assertEqual(0, execute().returncode, "Exact block must verify and copy root code")
            self.assertEqual(0o700, stat.S_IMODE(target.stat().st_mode))
            for name in (*NAMES, "source.json"):
                self.assertEqual((self.bundle / name).read_bytes(), (target / name).read_bytes())
                self.assertEqual(0, (target / name).stat().st_uid)
                self.assertEqual(0o600, stat.S_IMODE((target / name).stat().st_mode))
            sentinel = target / "human-recovery.py"
            before = sentinel.read_bytes()
            self.assertNotEqual(0, execute().returncode, "Collision must not overwrite")
            self.assertEqual(before, sentinel.read_bytes())
        finally:
            if target.exists():
                self.assertEqual(Path("/root") / ("hfpos-human-code-" + sha), target)
                shutil.rmtree(target)

    def test_transport_native_prompts_visible_protocol_stdout_captured_no_agent_both_auth_methods(self):
        for key in (None, Path("synthetic-key-reference-only")):
            options = self.w.ssh_options(Path("synthetic-known-hosts"), key)
            self.assertIn("StrictHostKeyChecking=yes", options)
            self.assertIn("IdentityAgent=none", options)
            self.assertIn("ForwardAgent=no", options)
            self.assertEqual(key is None, "PubkeyAuthentication=no" in options)
        with patch.object(self.w.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, b"SPOOL_PREPARED", None)) as run:
            self.assertEqual(b"SPOOL_PREPARED", self.w.interactive_transport(["ssh", "synthetic-only"], 90))
            self.assertIsNone(run.call_args.kwargs["stderr"])
            self.assertEqual(subprocess.PIPE, run.call_args.kwargs["stdout"])
            self.assertEqual(90, run.call_args.kwargs["timeout"])
        with patch.object(self.w.subprocess, "run", side_effect=subprocess.TimeoutExpired("synthetic", 90)):
            with self.assertRaises(subprocess.TimeoutExpired):
                self.w.interactive_transport(["ssh", "synthetic-only"], 90)

    def test_encrypted_input_uses_one_handle_no_plaintext_identity_copy(self):
        source, destination = self.folder / "synthetic-encrypted-identity", self.folder / "copy"
        encrypted = b"age-encryption.org/v1\nSYNTHETIC_ENCRYPTED_IDENTITY"
        source.write_bytes(encrypted)
        real_open = Path.open
        opens = []
        def one_handle(path, *args, **kwargs):
            if path == source:
                opens.append(path)
                if len(opens) > 1:
                    return io.BytesIO(b"SYNTHETIC_PLAINTEXT_PRIVATE_KEY")
            return real_open(path, *args, **kwargs)
        def create(path):
            with real_open(path, "xb"):
                pass
        with patch.object(Path, "open", one_handle), patch.object(self.archive, "private_create", side_effect=create):
            self.w.copy_encrypted(source, destination, self.archive)
        self.assertEqual(1, len(opens))
        self.assertEqual(encrypted, destination.read_bytes())
        self.assertEqual(encrypted, source.read_bytes())
        source.write_bytes(b"SYNTHETIC_PLAINTEXT_PRIVATE_KEY")
        destination.unlink()
        with self.assertRaises(ValueError):
            self.w.copy_encrypted(source, destination, self.archive)
        self.assertFalse(destination.exists())

    @unittest.skipUnless(os.name == "nt", "Native Windows PS5 / ACL / age fake")
    def test_native_windows_exact_entrypoint_success_late_age_failure_and_ssh_failure_cleanup(self):
        parent = self.private_parent()
        original = parent / "originals"
        self.archive.private_create(original, directory=True)
        identity, cipher = original / "encrypted-identity.age", original / "ciphertext.age"
        self.archive.private_write(identity, b"age-encryption.org/v1\nSYNTHETIC_ENCRYPTED_IDENTITY")
        self.archive.private_write(cipher, b"age-encryption.org/v1\nSYNTHETIC_CIPHER")
        self.value["ciphertext_sha256"] = hashlib.sha256(cipher.read_bytes()).hexdigest()
        token = base64.b64encode(self.h.canonical(self.value)).decode()
        tar = parent / "fake-authenticated-input.tar"
        self.archive.private_write(tar, tar_bytes(fixture()))
        real = subprocess.Popen
        def fake_age(args, **kwargs):
            if args[0] != "age":
                return real(args, **kwargs)
            self.assertEqual("--identity", args[2])
            self.assertNotEqual(str(identity), args[3], "Age must use only encrypted private copy")
            code = "import pathlib,sys;sys.stdout.buffer.write(pathlib.Path(sys.argv[1]).read_bytes());sys.exit(" + str(exit_code) + ")"
            return real([sys.executable, "-c", code, str(tar)], **kwargs)
        argv = ["--token", token, "--private-parent", str(parent), "--encrypted-identity", str(identity),
                "--ciphertext", str(cipher), "--host", "human@synthetic.invalid", "--known-hosts", "unused", "--ssh-key", "unused",
                "--remote-code", "/root/synthetic-code"]
        for exit_code, transfer_ok in ((0, True), (1, False), (0, False)):
            def fake_transfer(args, root, payload, value):
                self.assertNotIn(b"SYNTHETIC_SECRET_SENTINEL", payload)
                if not transfer_ok:
                    raise ValueError("synthetic SSH failure")
            with patch.object(self.h, "load_helpers", return_value=(self.ops, self.archive)), patch("builtins.input", return_value="VENTANA HUMANA SIN AGENTES"), \
                 patch.object(self.archive.subprocess, "Popen", side_effect=fake_age), patch.object(self.w, "transfer", side_effect=fake_transfer), \
                 redirect_stdout(io.StringIO()) as output:
                result = self.w.main(argv)
            self.assertEqual(0 if transfer_ok else 1, result)
            self.assertIn("WINDOWS_OWNED_STAGING_REMOVED", output.getvalue())
            self.assertFalse(any(p.name.startswith("human-recovery-") for p in parent.iterdir()))
            self.assertTrue(identity.exists() and cipher.exists() and tar.exists(), "Never delete originals")

    @unittest.skipUnless(os.name == "nt", "Native Windows cleanup helper")
    def test_native_journal_write_failure_cleanup_and_fallback_typed_approval(self):
        parent = self.private_parent()
        root = parent / ("human-recovery-" + secrets.token_hex(16))
        self.archive.private_create(root, directory=True)
        info = root.stat()
        journal = {"parent": str(parent), "nonce": root.name.removeprefix("human-recovery-"), "device": info.st_dev, "inode": info.st_ino}
        self.w.cleanup(root, journal, self.archive)  # Creation journal failure, no sensitive copies yet.
        self.assertFalse(root.exists())
        self.archive.private_create(root, directory=True)
        info = root.stat()
        journal.update(device=info.st_dev, inode=info.st_ino)
        self.archive.private_write(root / "creation.json", self.h.canonical(journal))
        self.archive.private_write(root / "synthetic-plaintext", b"synthetic-only")
        with patch.object(self.h, "load_helpers", return_value=(self.ops, self.archive)), patch("builtins.input", return_value="NO"), redirect_stdout(io.StringIO()):
            self.assertEqual(1, self.w.main(["--cleanup-root", str(root)]))
        self.assertTrue(root.exists())
        with patch.object(self.h, "load_helpers", return_value=(self.ops, self.archive)), patch("builtins.input", return_value="RETIRAR SOLO " + root.name), redirect_stdout(io.StringIO()):
            self.assertEqual(0, self.w.main(["--cleanup-root", str(root)]))
        self.assertFalse(root.exists())

    @unittest.skipUnless(os.name == "posix" and os.geteuid() == 0, "Root-owned Linux scratch")
    def test_workspace_checkpoint_collision_lock_and_mutable_code_denied(self):
        recovery = self.folder / "recovery"
        recovery.mkdir(mode=0o700)
        self.h.RECOVERY = recovery
        target = recovery / self.value["recovery_id"]
        target.mkdir(mode=0o700)
        for name in ("incoming", "postgres-data", "keyring-restored"):
            (target / name).mkdir(mode=0o700)
        info = target.stat()
        self.value.update(workspace_device=info.st_dev, workspace_inode=info.st_ino)
        with patch.object(self.h, "docker", return_value=b""), patch.object(self.h, "image_check", return_value=self.value["image_id"]):
            self.assertEqual(self.value, self.h.preflight(argparse.Namespace(**self.value)))
            (target / "human-checkpoint.json").write_bytes(b"partial")
            with self.assertRaises(ValueError):
                self.h.preflight(argparse.Namespace(**self.value))
        with self.h.workspace_lock(target):
            with self.assertRaises(BlockingIOError):
                with self.h.workspace_lock(target):
                    self.fail("Second writer acquired lock")
        (self.bundle / "human-recovery-transfer.py").chmod(0o666)
        with self.assertRaises(ValueError):
            self.h.load_helpers()


@unittest.skipUnless(os.name == "posix" and os.geteuid() == 0 and os.environ.get("HFPOS_HUMAN_DOCKER_TESTS") == "YES",
                     "Explicit CI-only synthetic Docker rehearsal")
class DockerHumanTests(unittest.TestCase):
    setUp = HumanTests.setUp
    tearDown = HumanTests.tearDown
    payload = HumanTests.payload
    def test_exact_human_console_postgres_keyring_roles_checkpoints_and_probes(self):
        self.assertEqual("true", os.environ.get("GITHUB_ACTIONS"), "Docker E2E is CI-only, never a live-host rehearsal")
        self.assertEqual("github-hosted", os.environ.get("RUNNER_ENVIRONMENT"))
        self.h.native(["/usr/bin/docker", "--host", "unix:///var/run/docker.sock", "--config", str(self.bundle / "docker-empty"),
                       "pull", self.h.IMAGE], timeout=120)  # Explicit synthetic CI acquisition, not an operational pull.
        source = "hfpos-human-synthetic-source-" + secrets.token_hex(8)
        destination = "hfpos-human-recovery-" + self.value["recovery_id"]
        created_parents = []
        created_containers = []
        target = self.h.RECOVERY / self.value["recovery_id"]
        try:
            for path in reversed((self.h.RECOVERY, *self.h.RECOVERY.parents)):
                if not path.exists():
                    path.mkdir(mode=0o700)
                    created_parents.append(path)
            self.h.root_path(self.h.RECOVERY)
            target.mkdir(mode=0o700)
            for folder in ("incoming", "postgres-data", "keyring-restored"):
                (target / folder).mkdir(mode=0o700)
            for name in (source, destination):
                self.assertFalse(self.h.docker("ps", "-a", "--filter", "name=^/" + name + "$", "--format", "{{.ID}}").strip())
            self.h.docker("run", "-d", "--pull=never", "--name", source, "--network", "none", "--read-only", "--user", "999:999",
                          "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--memory", "768m", "--cpus", "0.5",
                          "--pids-limit", "128", "--tmpfs", "/var/lib/postgresql/data:rw,uid=999,gid=999,mode=0700",
                          "--tmpfs", "/var/run/postgresql:rw,uid=999,gid=999", "--tmpfs", "/tmp:rw,mode=1777",
                          "-e", "POSTGRES_USER=synthetic", "-e", "POSTGRES_DB=synthetic", "-e", "POSTGRES_HOST_AUTH_METHOD=trust",
                          "--label", "hfpos.human.test=synthetic-only", self.h.IMAGE)
            created_containers.append(source)
            self.h.bind_helpers(self.ops)
            self.ops.wait_postgres_tcp(source, "synthetic", "synthetic")
            tables = ("__EFMigrationsHistory", "Companies", "Users", "PlatformUsers", "Sales", "SaleItems", "ProductStocks",
                      "Products", "CashSessions", "PurchaseReceipts", "CreditNotes")
            sql = "".join('CREATE TABLE public."' + n + '"(id integer);INSERT INTO public."' + n + '" VALUES(531);' for n in tables)
            sql += "CREATE SEQUENCE public.synthetic_sequence START WITH 531;SELECT setval('public.synthetic_sequence',536,true);"
            self.h.docker("exec", source, "psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", "synthetic", "-d", "synthetic", "-c", sql)
            fingerprint = 'SELECT id FROM public."Companies" ORDER BY id;SELECT last_value,is_called FROM public.synthetic_sequence;'
            baseline = self.h.docker("exec", source, "psql", "-X", "-At", "-U", "synthetic", "-d", "synthetic", "-c", fingerprint)
            dump = self.h.docker("exec", source, "pg_dump", "-U", "synthetic", "-d", "synthetic", "--format=custom", "--no-owner", "--no-privileges")
            payload = self.payload(dump)
            argv = [sys.executable, "-I", "-B", str(self.bundle / "human-recovery.py")]
            args = ["preflight", "--source-sha", self.value["source_sha"], "--code-sha256", self.h.digest_code(),
                    "--recovery-id", self.value["recovery_id"], "--ciphertext-sha256", self.value["ciphertext_sha256"]]
            pre = subprocess.run(argv + args, capture_output=True, timeout=60)
            self.assertEqual(0, pre.returncode, "Human preflight STOP (native outputs withheld)")
            token = pre.stdout.decode().strip().removeprefix("PREFLIGHT_PASS ")
            value = self.h.decode(base64.b64decode(token))
            spool = self.folder / ("human-recovery-" + secrets.token_hex(16))
            spool.mkdir(mode=0o700)
            for name, content in (("payload.tar", payload), ("preflight.json", self.h.canonical(value)),
                                  ("receipt.json", self.h.canonical({"payload_sha256": hashlib.sha256(payload).hexdigest(),
                                   "envelope_sha256": hashlib.sha256(self.h.canonical(value)).hexdigest()}))):
                (spool / name).write_bytes(content)
                (spool / name).chmod(0o600)
            created_containers.append(destination)
            restore_args = ["restore", "--spool", str(spool), "--payload-sha256", hashlib.sha256(payload).hexdigest()]
            restored = subprocess.run(argv + restore_args,
                                      input=("RESTAURAR " + value["recovery_id"] + "\n").encode(), capture_output=True, timeout=800)
            self.assertEqual(0, restored.returncode, "Human exact restore entrypoint STOP (native outputs withheld)")
            self.assertIn(b"CORE_RESTORE_PASS_APP_BLOCKED", restored.stdout)
            self.assertFalse(any((target / "postgres-data").iterdir()), "PG must be tmpfs, never host data")
            verify = argv + ["verify", "--recovery-id", value["recovery_id"]]
            self.assertEqual(0, subprocess.run(verify, capture_output=True, timeout=60).returncode)
            def destination_sql(statement, role="hfpos_recovery_owner"):
                return self.h.docker("exec", destination, "psql", "-X", "-At", "-v", "ON_ERROR_STOP=1", "-U", role,
                                     "-d", "hfpos_recovery", "-c", statement)
            self.assertEqual(baseline, destination_sql(fingerprint))
            destination_sql("CREATE TABLE public.synthetic_future(id integer);CREATE SEQUENCE public.synthetic_future_sequence;")
            self.assertEqual(b"t", destination_sql("SELECT has_table_privilege(current_user,'public.synthetic_future','SELECT,INSERT,UPDATE,DELETE') "
                    "AND has_sequence_privilege(current_user,'public.synthetic_future_sequence','USAGE,SELECT') "
                    "AND NOT has_sequence_privilege(current_user,'public.synthetic_future_sequence','UPDATE');", "hfpos_recovery_runtime").strip())
            # Corrupt each invariant in the synthetic target, then check read-only verify rejects it.
            for mutation, repair, role in (
                ("REVOKE USAGE ON SEQUENCE public.synthetic_sequence FROM hfpos_recovery_runtime;",
                 "GRANT USAGE ON SEQUENCE public.synthetic_sequence TO hfpos_recovery_runtime;", "hfpos_recovery_owner"),
                ("ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE SELECT ON SEQUENCES FROM hfpos_recovery_runtime;",
                 "ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON SEQUENCES TO hfpos_recovery_runtime;", "hfpos_recovery_owner"),
                ("ALTER ROLE hfpos_recovery_runtime BYPASSRLS;", "ALTER ROLE hfpos_recovery_runtime NOBYPASSRLS;", "hfpos_recovery_admin")):
                destination_sql(mutation, role)
                self.assertNotEqual(0, subprocess.run(verify, capture_output=True, timeout=60).returncode)
                destination_sql(repair, role)
            # Official restore script independently refuses the now nonempty DB.
            nonempty = subprocess.run(["/usr/bin/docker", "--host", "unix:///var/run/docker.sock", "--config", str(self.bundle / "docker-empty"),
                         "exec", "-e", "PGUSER=hfpos_recovery_owner", "-e", "PGDATABASE=hfpos_recovery", "-e", "HFPOS_RESTORE_APPROVED=YES",
                         destination, "sh", "/tmp/ops/postgres-restore.sh", "/tmp/" + Path(self.h.transfer_plan(value, payload)[1]["dump"]).name],
                         capture_output=True, timeout=60)
            self.assertNotEqual(0, nonempty.returncode)
            replay = subprocess.run(argv + restore_args, input=("RESTAURAR " + value["recovery_id"] + "\n").encode(),
                                    capture_output=True, timeout=60)
            self.assertNotEqual(0, replay.returncode, "No automatic SQL resume")
            state = target / "human-checkpoint.json"
            saved = self.h.decode(state.read_bytes())
            for stage in ("restore-started", "keyring-started", "unknown"):
                state.write_bytes(self.h.canonical(dict(saved, stage=stage)))
                self.assertNotEqual(0, subprocess.run(verify, capture_output=True, timeout=60).returncode)
            state.write_bytes(self.h.canonical(saved))
            key = target / "keyring-restored/key-synthetic.xml"
            key.write_bytes(b"corrupted")
            self.assertNotEqual(0, subprocess.run(verify, capture_output=True, timeout=60).returncode)
            self.assertEqual(baseline, self.h.docker("exec", source, "psql", "-X", "-At", "-U", "synthetic", "-d", "synthetic", "-c", fingerprint))
        finally:
            for name in created_containers:
                # Only test-created names, confirmed synthetic labels before deletion.
                result = subprocess.run(["/usr/bin/docker", "--host", "unix:///var/run/docker.sock", "--config", str(self.bundle / "docker-empty"),
                                         "inspect", name], capture_output=True, timeout=15)
                if result.returncode == 0:
                    labels = json.loads(result.stdout)[0]["Config"]["Labels"]
                    self.assertTrue(labels.get("hfpos.human.test") == "synthetic-only" or
                                    labels.get("hfpos.human.source") == self.value["source_sha"])
                    self.h.docker("rm", "-f", name)
            if target.exists():
                self.assertEqual(self.h.RECOVERY / self.value["recovery_id"], target)
                shutil.rmtree(target)
            for path in reversed(created_parents):
                path.rmdir()


if __name__ == "__main__":
    unittest.main()
