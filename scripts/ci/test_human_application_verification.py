"""Synthetic-only contracts and optional github-hosted actual-image rehearsal."""
import argparse
import base64
import copy
from contextlib import redirect_stdout
import hashlib
import importlib.util
import io
import json
import os
import re
from pathlib import Path
import secrets
import shutil
import subprocess
import sys
import tarfile
import tempfile
import types
import unittest
import warnings
from unittest.mock import patch

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
OPS = ROOT / "scripts/ops"


def load(path):
    spec = importlib.util.spec_from_file_location("test_" + path.stem.replace("-", "_"), path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def synthetic_private_keyring_archive(data, archive_tools, tar_bytes):
    # The Compose fixture's '.' can be 0755. Do not let TAR directory metadata
    # replace the human core fixture's already-private 0700 destination.
    archive_tools.validate_keyring(data)
    with tarfile.open(fileobj=io.BytesIO(data), mode="r:") as archive:
        files = {member.name: archive.extractfile(member).read() for member in archive if member.isfile()}
    result = tar_bytes(files)  # Existing synthetic factory emits only regular 0600 files.
    archive_tools.validate_keyring(result)
    with tarfile.open(fileobj=io.BytesIO(result), mode="r:") as archive:
        restored = {member.name: archive.extractfile(member).read() for member in archive if member.isfile()}
    if files != restored:
        raise AssertionError("Synthetic keyring bytes changed")
    return result


class ApplicationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.folder = Path(self.temp.name)
        self.app = load(OPS / "human-application-verification.py")
        self.id = "a" * 32
        self.db_id = "b" * 64
        self.saved = {"stage": "core-restored-app-blocked", "container_id": self.db_id,
            "labels": {"hfpos.human.source": "a" * 40, "hfpos.human.code": "c" * 64}}
        (self.folder / "human-checkpoint.json").write_bytes(self.app.canonical(self.saved))
        self.core = types.SimpleNamespace(root_path=lambda p, *a, **kw: p, inspect_manual=lambda *a: self.db_id,
            digest_code=lambda: "c" * 64, IMAGE="synthetic-postgres", decode=self.app.decode)
        self.proof = {"network": "PASS_IPV4_IPV6", "keyring": "PASS_BYTES", "backend": "PASS_HTTP",
            "platform": "PASS_HTTP_LOGIN_ME", "tenant": "PASS_HTTP_LOGIN_ME", "protectedValue": "PASS_EXISTING_SMTP",
            "smtp": "PASS_EXISTING_SMTP", "certificate": "NOT_PRESENT",
            "worker": "REGISTERED_DB_CLAIMS_DENIED", "authWrites": "NONE_ALLOWED", "businessRead": "PASS_EXISTING_SALE"}
        self.calls = []

    def tearDown(self):
        self.temp.cleanup()

    def run_fixture(self, counts=(1, 1, 1, 1), payloads=("synthetic-ciphertext",), fingerprints=("same", "same"), deny_role=False, certificates=()):
        app = self.app
        def docker(*args, **kwargs):
            self.calls.append(args)
            if args[:2] == ("inspect", "hfpos-human-recovery-" + self.id):
                return app.canonical([{"State": {"Running": True}, "Config": {"Image": self.core.IMAGE}, "Image": "db-image"}])
            if args[:2] == ("image", "inspect"):
                return app.canonical([{"Id": "db-image"}])
            if args[0] == "create":
                return b"d" * 64
            if args[0] == "start":
                return subprocess.CompletedProcess([], 0, app.canonical(self.proof), b"SYNTHETIC_RAW_LOG_SENTINEL")
            return b""
        def sql(db, statement, *args, **kw):
            self.calls.append(("sql", statement))
            if "json_build_array" in statement:
                return app.canonical(counts)
            if '"CompanySriCertificates"' in statement:
                return app.canonical(certificates)
            if "json_agg" in statement:
                return app.canonical(payloads)
            return b""
        with patch.object(app, "docker", side_effect=docker), patch.object(app, "sql", side_effect=sql), \
                patch.object(app, "image_identity", return_value="app-image"), \
                patch.object(app, "key_snapshot", return_value=({"key-synthetic.xml": "encoded"}, {"identity": 1})), \
                patch.object(app, "create_reader"), patch.object(app, "verify_reader", side_effect=ValueError("STOP") if deny_role else None), \
                patch.object(app, "fingerprint", side_effect=fingerprints), \
                patch.object(app, "probe_environment", return_value={"HFPOS_VERIFY_PROBE_BASE64": "cHVibGlj", "HFPOS_VERIFY_PROBE_SHA256": "f" * 64}), \
                patch.object(app, "business_sample", return_value={"id": 1} if counts[1] else None), \
                patch.object(app.os, "fchmod", create=True), \
                patch.object(app, "inspect_app", side_effect=[{"State": {"Status": "created"}}, {"State": {"Status": "exited", "ExitCode": 0}}]), \
                patch("builtins.input", side_effect=["HFPOS", "VERIFICAR APP " + self.id]), \
                patch.object(app.getpass, "getpass", return_value="SYNTHETIC_PASSWORD_SENTINEL"), redirect_stdout(io.StringIO()) as output:
            app.run_locked(argparse.Namespace(recovery_id=self.id), self.core, self.folder,
                {"source_sha": "a" * 40}, {"source_sha": "e" * 40, "probe_sha256": "f" * 64})
            return output.getvalue()

    def test_actual_lifecycle_create_inspect_before_start_and_success_owned_cleanup(self):
        output = self.run_fixture()
        self.assertIn("APP_VERIFICATION_PASS", output)
        self.assertNotIn("PASSWORD_SENTINEL", output)
        self.assertNotIn("RAW_LOG_SENTINEL", output)
        create = next(c for c in self.calls if c[0] == "create")
        self.assertIn("--pull=never", create)
        self.assertIn("container:" + self.db_id, create)
        self.assertIn("--log-driver", create)
        self.assertLess(self.calls.index(create), next(i for i, c in enumerate(self.calls) if c[0] == "start"))
        self.assertEqual([("rm", "d" * 64)], [c for c in self.calls if c[0] == "rm"])
        self.assertFalse(any(c[0] in ("run", "pull", "stop", "restart", "cp") for c in self.calls))
        self.assertEqual("/bin/sh", create[create.index("--entrypoint") + 1])
        self.assertEqual(self.app.probe_command(), list(create[-2:]))
        self.assertEqual(self.saved, self.app.decode((self.folder / "human-checkpoint.json").read_bytes()))

    def test_empty_tenant_and_payload_are_partial_never_canary_success(self):
        self.proof.update(tenant="EMPTY", protectedValue="NOT_PRESENT", smtp="NOT_PRESENT", businessRead="EMPTY")
        output = self.run_fixture(counts=(1, 0, 0, 0), payloads=())
        self.assertIn("APP_VERIFICATION_PARTIAL_HUMAN_GATE", output)
        self.assertNotIn("APP_VERIFICATION_PASS", output)
        state = self.app.decode((self.folder / "application-checkpoint.json").read_bytes())
        self.assertEqual("EMPTY", state["history"])

    def test_existing_certificate_can_prove_dp_without_fabricating_smtp(self):
        self.proof.update(protectedValue="PASS_EXISTING_CERTIFICATE", certificate="PASS_EXISTING_CERTIFICATE", smtp="NOT_PRESENT")
        output = self.run_fixture(payloads=(), certificates=({"Certificate": "synthetic-cipher-bytes", "Password": "synthetic-cipher-password"},))
        self.assertIn("APP_VERIFICATION_PASS", output)
        self.assertIn('"smtp":"NOT_PRESENT"', output)
        self.assertTrue(any('"EncryptedCertificateBytes"' in str(c) and "LIMIT 129" in str(c) for c in self.calls))

    def test_rc6_identity_is_closed_on_digest_revision_version_and_never_pulls(self):
        app = self.app
        image = {"Os": "linux", "Architecture": "amd64", "RepoDigests": [app.IMAGE], "Id": "synthetic-id",
                 "Config": {"Labels": {"org.opencontainers.image.revision": app.REVISION,
                 "org.opencontainers.image.version": app.RELEASE}, "User": "1654:1654", "Entrypoint": ["dotnet", "Pos.Backend.Api.dll"]}}
        self.assertEqual("52403ce0805ddd98ad04139c0a72419b2df90195", app.REVISION)
        self.assertTrue(app.IMAGE.endswith("dc2d30192fc65b9c204a3e4a1d38c2535ca7a597029aaa8f3a714e826aaffcb7"))
        with patch.object(app, "docker", return_value=app.canonical([image])) as docker:
            self.assertEqual("synthetic-id", app.image_identity())
            self.assertEqual(("image", "inspect", app.IMAGE), docker.call_args.args)
        for field, bad in (("org.opencontainers.image.revision", "a" * 40), ("org.opencontainers.image.version", "v0.1.0-rc.1")):
            changed = copy.deepcopy(image)
            changed["Config"]["Labels"][field] = bad
            with patch.object(app, "docker", return_value=app.canonical([changed])), self.assertRaises(ValueError):
                app.image_identity()

    def test_seal_checks_all_three_exact_sources_before_creating_output(self):
        app = self.app
        sources = ("scripts/ops/human-application-verification.py", app.SOURCE + "Program.cs", app.SOURCE + "RestoredApplicationProbe.csproj")
        binary = self.folder / "probe.dll"
        binary.write_bytes(b"synthetic-public-dll")
        sha = "a" * 40
        for changed in sources:
            def native(args):
                if args[-2:] == ["rev-parse", "HEAD"]:
                    return (sha + "\n").encode()
                name = args[-1].split(":", 1)[1]
                return (ROOT / name).read_bytes().replace(b"\r\n", b"\n") + (b"\n# tampered" if name == changed else b"")
            out = self.folder / "sealed"
            with patch.object(app, "native", side_effect=native), self.assertRaises(ValueError):
                app.seal(argparse.Namespace(source_sha=sha, probe_dll=str(binary), output=str(out)))
            self.assertFalse(out.exists())

    def test_business_or_sequence_change_stops_without_destructive_cleanup(self):
        with self.assertRaises(ValueError):
            self.run_fixture(fingerprints=("before", "sequence-changed"))
        self.assertFalse(any(c[0] == "rm" for c in self.calls))
        self.assertEqual("created-no-retry", self.app.decode((self.folder / "application-checkpoint.json").read_bytes())["stage"])

    def test_effective_role_denial_is_before_container_create(self):
        with self.assertRaises(ValueError):
            self.run_fixture(deny_role=True)
        self.assertFalse(any(c[0] in ("create", "start", "rm") for c in self.calls))

    def test_unexpected_proof_or_partial_checkpoint_stops_no_retry(self):
        self.proof["worker"] = "DISABLED_NONEXISTENT_SWITCH"
        with self.assertRaises(ValueError):
            self.run_fixture()
        self.assertFalse(any(c[0] == "rm" for c in self.calls))
        self.calls.clear()
        with self.assertRaises(FileExistsError):
            self.run_fixture()
        self.assertFalse(any(c[0] == "create" for c in self.calls))

    def test_effective_readonly_and_actual_lock_permission_negative(self):
        denied = subprocess.CompletedProcess([], 3, b"", b"ERROR: 42501")
        with patch.object(self.app, "sql", side_effect=[b"t", denied]) as sql:
            self.app.verify_reader("synthetic", "synthetic_role")
        self.assertIn("has_any_column_privilege", sql.call_args_list[0].args[1])
        self.assertIn("has_sequence_privilege", sql.call_args_list[0].args[1])
        self.assertIn("FOR UPDATE", sql.call_args_list[1].args[1])
        self.assertIn("default_transaction_read_only=off", sql.call_args_list[1].args[1])
        for result in (b"f", b"", b"t\nt"):
            with patch.object(self.app, "sql", return_value=result), self.assertRaises(ValueError):
                self.app.verify_reader("synthetic", "synthetic_role")

    def test_fingerprint_includes_every_relation_and_logical_sequence_state(self):
        with patch.object(self.app, "sql", side_effect=[b'[["public","ElectronicIssuingJobs","r"],["public","seq","S"]]',
                b"a" * 32 + b"\n" + b"b" * 32 + b"\n"]) as sql:
            self.assertEqual(64, len(self.app.fingerprint("synthetic")))
        statement = sql.call_args.args[1]
        self.assertIn("BEGIN READ ONLY", statement)
        self.assertIn('ONLY "public"."ElectronicIssuingJobs"', statement)
        self.assertIn("last_value,is_called", statement)
        self.assertNotIn("nextval", statement)

    def test_configuration_is_explicit_synthetic_and_no_production_import(self):
        env = self.app.configuration("synthetic_role", "HFPOS")
        self.assertEqual("false", env["SeedDemoData"])
        self.assertEqual("false", env["PlatformBootstrap__Enabled"])
        self.assertEqual("Production", env["ASPNETCORE_ENVIRONMENT"])
        self.assertTrue(all("sri.invalid" in value for key, value in env.items() if key.startswith("Sri__") and key.endswith("Url")))
        self.assertNotEqual(self.app.configuration("synthetic_role", "HFPOS")["Jwt__Key"], env["Jwt__Key"])
        self.assertFalse(any("Enabled" in key and "Issuing" in key for key in env))

    def test_restored_key_snapshot_limits_match_archive_one_mib_member_four_mib_total(self):
        folder = self.folder / "keyring-restored"
        folder.mkdir()
        key = folder / "key-synthetic.xml"
        key.write_bytes(b"x" * 1048576)
        values, _ = self.app.key_snapshot(self.core, self.folder)
        self.assertEqual(1048576, len(base64.b64decode(values[key.name])))
        key.write_bytes(b"x" * 1048577)
        with self.assertRaises(ValueError):
            self.app.key_snapshot(self.core, self.folder)
        key.write_bytes(b"x" * 1048576)
        for suffix in ("a", "b", "c", "d"):
            (folder / ("key-" + suffix + ".xml")).write_bytes(key.read_bytes())
        with self.assertRaises(ValueError):
            self.app.key_snapshot(self.core, self.folder)

    def test_probe_bootstrap_uses_only_hash_bound_public_env_and_private_tmpfs_without_copy(self):
        content = b"synthetic-public-dll; $(never-execute)"
        file = self.folder / "RestoredApplicationProbe.dll"
        file.write_bytes(content)
        digest = hashlib.sha256(content).hexdigest()
        with patch.object(self.app, "HERE", self.folder), patch.object(self.app, "docker") as docker:
            env = self.app.probe_environment(digest)
            self.assertEqual(content, base64.b64decode(env["HFPOS_VERIFY_PROBE_BASE64"], validate=True))
            self.assertEqual(digest, env["HFPOS_VERIFY_PROBE_SHA256"])
            docker.assert_not_called()
            with self.assertRaises(ValueError):
                self.app.probe_environment("f" * 64)
            file.write_bytes(b"x" * 65537)
            with self.assertRaises(ValueError):
                self.app.probe_environment(hashlib.sha256(file.read_bytes()).hexdigest())
        command = self.app.probe_command()
        self.assertEqual("-c", command[0])
        self.assertIn('"$HFPOS_VERIFY_PROBE_BASE64"', command[1])
        self.assertIn("/tmp/RestoredApplicationProbe.dll", command[1])
        self.assertIn('"$HFPOS_VERIFY_PROBE_SHA256" | /usr/bin/sha256sum --check --status', command[1])
        self.assertLess(command[1].index("sha256sum --check"), command[1].index("exec dotnet exec"))
        self.assertNotIn("--depsfile", command[1])
        self.assertNotIn("/probe/", command[1])
        self.assertNotIn("never-execute", command[1])

    def test_keyboard_interrupt_is_redacted_stop(self):
        with patch.object(self.app, "verify", side_effect=KeyboardInterrupt), redirect_stdout(io.StringIO()) as output:
            self.assertEqual(1, self.app.main(["verify", "--core-code", "synthetic", "--recovery-id", self.id]))
        self.assertEqual("STOP_APP_VERIFICATION_NO_RETRY_PRESERVE_CORE\n", output.getvalue())

    def test_echo_fallback_warning_stops_before_reading_any_credentials(self):
        from unittest.mock import Mock
        consumed = Mock()
        def fallback(prompt):
            warnings.warn("SYNTHETIC_ECHO_FALLBACK", self.app.getpass.GetPassWarning)
            consumed()
            return "must-never-be-read"
        with patch.object(self.app.getpass, "getpass", side_effect=fallback), self.assertRaises(self.app.getpass.GetPassWarning):
            self.app.hidden_prompt("synthetic-hidden-prompt")
        consumed.assert_not_called()

    def test_business_permission_missing_is_partial_despite_counts_and_stable_fingerprint(self):
        self.proof["businessRead"] = "PENDING_PERMISSION"
        output = self.run_fixture()
        self.assertIn("APP_VERIFICATION_PARTIAL_HUMAN_GATE", output)
        self.assertNotIn("APP_VERIFICATION_PASS", output)

    def test_timeout_interrupt_or_error_kills_only_verified_new_id_not_database(self):
        app = self.app
        for error in (subprocess.TimeoutExpired("synthetic-client", 180), KeyboardInterrupt(), ValueError("STOP")):
            with self.subTest(error=type(error).__name__), \
                    patch.object(app, "inspect_app", side_effect=[{"State": {"Status": "created", "Running": False}},
                        {"State": {"Running": True}}, {"State": {"Running": False}}]), \
                    patch.object(app, "docker", side_effect=[error, b""]) as docker:
                with self.assertRaises(type(error)):
                    app.execute_app("new-name", "new-id", "new-image", "db-id", {"owned": "nonce"}, {}, b"synthetic", lambda: None)
                self.assertEqual(("kill", "--signal", "KILL", "new-id"), docker.call_args.args)
                self.assertFalse(any("db-id" in call.args for call in docker.call_args_list))

    def test_timeout_identity_mismatch_refuses_kill_and_preserves_core(self):
        app = self.app
        old = (self.folder / "human-checkpoint.json").read_bytes()
        with patch.object(app, "inspect_app", side_effect=[{"State": {"Status": "created"}}, ValueError("STOP")]), \
                patch.object(app, "docker", side_effect=subprocess.TimeoutExpired("synthetic", 180)) as docker:
            with self.assertRaises(ValueError):
                app.execute_app("new-name", "new-id", "new-image", "db-id", {}, {}, b"", lambda: None)
            self.assertFalse(any(call.args[0] in ("kill", "rm", "stop", "restart") for call in docker.call_args_list))
        self.assertEqual(old, (self.folder / "human-checkpoint.json").read_bytes())
        with self.assertRaises(ValueError):
            app.stop_owned_app("db-id", "image", "db-id", {}, {})

    def test_created_container_exact_guards_reject_identity_network_and_privilege_drift(self):
        app = self.app
        labels, env = {"hfpos.application.source": "a" * 40}, app.configuration("synthetic", "HFPOS")
        value = {"Id": "container", "Image": "image", "Mounts": [], "Config": {"Image": app.IMAGE, "User": "1654:1654",
            "Labels": labels, "Env": [k + "=" + v for k, v in env.items()], "Entrypoint": ["/bin/sh"], "Cmd": app.probe_command()},
            "HostConfig": {"NetworkMode": "container:database", "ReadonlyRootfs": True, "Privileged": False,
                "PortBindings": {}, "CapDrop": ["ALL"], "SecurityOpt": ["no-new-privileges"], "LogConfig": {"Type": "none"},
                "RestartPolicy": {"Name": "no"}, "Memory": 805306368, "MemorySwap": 805306368, "PidsLimit": 128,
                "NanoCpus": 500000000, "Tmpfs": {"/keys": "rw,noexec,nosuid,size=16m,uid=1654,gid=1654,mode=0700",
                    "/tmp": "rw,noexec,nosuid,size=32m,uid=1654,gid=1654,mode=0700"}}}
        def inspect(item):
            with patch.object(app, "docker", side_effect=[app.canonical([item]), app.canonical([{"Config": {}}])]):
                return app.inspect_app("synthetic", "container", "image", "database", labels, env)
        self.assertEqual(value, inspect(value))
        for section, key, bad in ((None, "Image", "other"), (None, "Id", "other"),
                ("HostConfig", "NetworkMode", "bridge"), ("HostConfig", "Privileged", True),
                ("HostConfig", "Binds", ["/unsafe:/keys"]), ("HostConfig", "PortBindings", {"8080/tcp": [{}]}),
                ("HostConfig", "LogConfig", {"Type": "json-file"}), ("HostConfig", "ReadonlyRootfs", False),
                ("HostConfig", "Tmpfs", {}), ("Config", "User", "0"), ("Config", "Cmd", ["Pos.Backend.Api.dll"]),
                ("Config", "Entrypoint", ["dotnet"]), ("Config", "Env", value["Config"]["Env"] + ["UNEXPECTED=denied"])):
            with self.subTest(key=key):
                changed = copy.deepcopy(value)
                (changed[section] if section else changed)[key] = bad
                with self.assertRaises(ValueError):
                    inspect(changed)

    def test_root_bootstrap_is_fixed_list_and_valid_python(self):
        guide = (ROOT / "docs/human-restored-application-verification.md").read_text()
        code = re.search(r"# APP_ROOT_SNAPSHOT_BEGIN.*?<<'PY'\n(.*?)\nPY", guide, re.S)[1]
        compile(code, "app-root-snapshot", "exec")
        for name in self.app.FILES:
            self.assertIn("'" + name + "'", code)
        self.assertIn("O_NOFOLLOW", code)

    def test_synthetic_keyring_archive_preserves_xml_without_overriding_private_destination_mode(self):
        old = load(ROOT / "scripts/ci/test_human_recovery.py")
        tools = load(OPS / "supervised-archive.py")
        stream = io.BytesIO()
        xml = b"<key>synthetic-only-identical-bytes</key>"
        with tarfile.open(fileobj=stream, mode="w", format=tarfile.USTAR_FORMAT) as archive:
            directory = tarfile.TarInfo(".")
            directory.type, directory.mode = tarfile.DIRTYPE, 0o755
            archive.addfile(directory)
            member = tarfile.TarInfo("./key-synthetic.xml")
            member.mode, member.size = 0o644, len(xml)
            archive.addfile(member, io.BytesIO(xml))
        with patch.object(tools, "validate_keyring", wraps=tools.validate_keyring) as validate:
            result = synthetic_private_keyring_archive(stream.getvalue(), tools, old.tar_bytes)
            self.assertEqual(2, validate.call_count)
        with tarfile.open(fileobj=io.BytesIO(result)) as archive:
            self.assertEqual(["./key-synthetic.xml"], archive.getnames())
            self.assertEqual(0o600, archive.getmembers()[0].mode)
            self.assertEqual(xml, archive.extractfile(archive.getmembers()[0]).read())
            destination = self.folder / "private-keyring"
            destination.mkdir(mode=0o700)
            archive.extractall(destination, filter="data")
        if os.name == "posix":
            import stat
            self.assertEqual(0o700, stat.S_IMODE(destination.stat().st_mode))

    @unittest.skipUnless(os.name == "posix" and os.geteuid() == 0, "Linux root bootstrap synthetic scratch")
    def test_root_bootstrap_hash_collision_and_symlink_rejection_before_privileged_writes(self):
        app = self.app
        with tempfile.TemporaryDirectory(prefix="app-root-test-", dir="/root") as directory:
            source = Path(directory)
            sha = secrets.token_hex(20)
            target = Path("/root") / ("hfpos-app-code-" + sha)
            files = {app.FILES[0]: b"# synthetic-public-code\n", app.FILES[1]: b"synthetic-public-dll"}
            expected = app.digest(files)
            probe = hashlib.sha256(files[app.FILES[1]]).hexdigest()
            for name, data in {**files, "source.json": app.canonical({"source_sha": sha, "code_sha256": expected, "probe_sha256": probe})}.items():
                (source / name).write_bytes(data)
                (source / name).chmod(0o600)
            code = re.search(r"# APP_ROOT_SNAPSHOT_BEGIN.*?<<'PY'\n(.*?)\nPY",
                (ROOT / "docs/human-restored-application-verification.md").read_text(), re.S)[1]
            def run(digest=expected):
                return subprocess.run([sys.executable, "-I", "-", str(source), str(target), sha, digest, probe],
                    input=code.encode(), capture_output=True, timeout=15)
            try:
                self.assertFalse(target.exists())
                self.assertNotEqual(0, run("f" * 64).returncode)
                self.assertFalse(target.exists())
                file = source / app.FILES[1]
                file.unlink()
                file.symlink_to(source / app.FILES[0])
                self.assertNotEqual(0, run().returncode)
                self.assertFalse(target.exists())
                file.unlink()
                file.write_bytes(files[app.FILES[1]])
                file.chmod(0o600)
                self.assertEqual(0, run().returncode)
                self.assertEqual(files[app.FILES[1]], (target / app.FILES[1]).read_bytes())
                self.assertNotEqual(0, run().returncode)
            finally:
                if target.exists():
                    self.assertEqual(Path("/root") / ("hfpos-app-code-" + sha), target)
                    shutil.rmtree(target)


@unittest.skipUnless(os.name == "posix" and os.environ.get("HFPOS_APPLICATION_DOCKER_TESTS") == "YES", "CI-only actual-image E2E")
class DockerApplicationTests(unittest.TestCase):
    def test_actual_rc_backend_with_restored_database_keyring_and_queued_job(self):
        self.assertEqual("true", os.environ.get("GITHUB_ACTIONS"))
        self.assertEqual("github-hosted", os.environ.get("RUNNER_ENVIRONMENT"))
        self.assertEqual(0, os.geteuid())
        old = load(ROOT / "scripts/ci/test_human_recovery.py")
        fixture = old.HumanTests()
        fixture.setUp()
        self.assertEqual(fixture.h.RECOVERY, fixture.ops.RECOVERY)
        app = load(OPS / "human-application-verification.py")
        created = []
        parents = []
        target = fixture.h.RECOVERY / fixture.value["recovery_id"]
        try:
            app_bundle = fixture.folder / "app-code"
            app_bundle.mkdir(mode=0o700)
            for name in app.FILES:
                data = (OPS / name).read_bytes() if name.endswith(".py") else Path(os.environ["HFPOS_APPLICATION_PROBE_DLL"]).read_bytes()
                (app_bundle / name).write_bytes(data)
                (app_bundle / name).chmod(0o600)
            (app_bundle / "docker-empty").mkdir(mode=0o700)
            app.HERE = app_bundle
            source = {"source_sha": "e" * 40, "probe_sha256": hashlib.sha256((app_bundle / app.FILES[1]).read_bytes()).hexdigest()}
            # Public image acquisition is CI-only; the operational helper has no pull path.
            app.docker("pull", app.IMAGE, timeout=180)
            app.image_identity()
            tools = app.docker("run", "--rm", "--pull=never", "--network", "none", "--read-only", "--user", "1654:1654",
                "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--log-driver", "none", "--entrypoint", "/bin/sh", app.IMAGE,
                "-c", "test -x /usr/bin/base64 && test -x /usr/bin/chmod && test -x /usr/bin/sha256sum && command -v dotnet >/dev/null "
                "&& test -r /app/Pos.Backend.Api.runtimeconfig.json && test -r /app/Pos.Backend.Api.dll && printf BOOTSTRAP_TOOLS_PASS")
            self.assertEqual(b"BOOTSTRAP_TOOLS_PASS", tools)
            suffix = hashlib.sha256(str(ROOT).lower().encode()).hexdigest()[:12]
            project = "hfpos-dev531-" + suffix
            pg = app.docker("ps", "--filter", "label=com.docker.compose.project=" + project,
                            "--filter", "label=com.docker.compose.service=postgres", "--format", "{{.ID}}").decode().strip()
            self.assertRegex(pg, r"^[a-f0-9]{12,64}$")
            dump = app.docker("exec", pg, "sh", "-c", "cat /backups/hfpos-*.dump")
            keys = app.docker("exec", pg, "sh", "-c", "tar -cf - -C /backups/restored-keys .")
            keys = synthetic_private_keyring_archive(keys, fixture.archive, old.tar_bytes)
            worker = load(OPS / "hourly-backup/backup.py")
            with tarfile.open(fileobj=io.BytesIO(keys), mode="r:") as keytar:
                key_files = {m.name.removeprefix("./"): keytar.extractfile(m).read() for m in keytar if m.isfile()}
            values = old.fixture(dump)
            db_files = {Path(n).name: data for n, data in values.items() if n.startswith("db/")}
            archive = fixture.folder / "actual-synthetic.tar"
            archive.write_bytes(worker.make_package(db_files, key_files,
                {"config/synthetic.env": b"OFFLINE_CONFIG_SENTINEL=synthetic-only\n"}, b"-- no passwords", b"-- grants map", [], 1))
            identity = fixture.folder / "synthetic-age-key"
            subprocess.run(["age-keygen", "-o", str(identity)], check=True, capture_output=True, timeout=15)
            recipient = subprocess.run(["age-keygen", "-y", str(identity)], check=True, capture_output=True, timeout=15).stdout.decode().strip()
            cipher = fixture.folder / "actual-synthetic.age"
            subprocess.run(["age", "-r", recipient, "-o", str(cipher), str(archive)], check=True, capture_output=True, timeout=15)
            fixture.value["ciphertext_sha256"] = hashlib.sha256(cipher.read_bytes()).hexdigest()
            export = fixture.folder / "actual-export"
            offline = fixture.folder / "offline-only"
            manifest = fixture.archive.decrypt_export(cipher, identity, fixture.folder, export,
                fixture.value["recovery_id"], fixture.value["ciphertext_sha256"], offline_config_output=offline)
            with tarfile.open(offline / "host-config.tar") as config:
                self.assertEqual(b"OFFLINE_CONFIG_SENTINEL=synthetic-only\n", config.extractfile("config/synthetic.env").read())
            payload = old.tar_bytes({name: (export / name).read_bytes() for name in (*manifest["files"], "transfer.json")})
            self.assertNotIn(b"OFFLINE_CONFIG_SENTINEL", payload)
            for path in reversed((fixture.h.RECOVERY, *fixture.h.RECOVERY.parents)):
                if not path.exists():
                    path.mkdir(mode=0o700)
                    parents.append(path)
            target.mkdir(mode=0o700)
            for name in ("incoming", "postgres-data", "keyring-restored"):
                (target / name).mkdir(mode=0o700)
            info = target.stat()
            fixture.value.update(workspace_device=info.st_dev, workspace_inode=info.st_ino, image_id=fixture.h.image_check())
            db = "hfpos-human-recovery-" + fixture.value["recovery_id"]
            created.append(db)
            fixture.h.restore(fixture.value, payload, fixture.ops)
            # Requeue one synthetic historical job before the invariant baseline, never in human mode.
            app.sql(db, 'UPDATE public."ElectronicIssuingJobs" SET "AutomaticRequested"=true,"State"=0,"Phase"=0,'
                '"NextAttemptAt"=now()-interval \'1 minute\',"LeaseToken"=NULL,"LeaseExpiresAt"=NULL;')
            self.assertEqual(b"t", app.sql(db, 'SELECT count(*)>0 FROM public."ElectronicIssuingJobs" WHERE "AutomaticRequested" AND "State"=0;').strip())
            synthetic = dict(line.split("=", 1) for line in (ROOT / "deploy/compose/smoke-runtime/smoke.env").read_text().splitlines())
            responses = ["smoke-platform", synthetic["SMOKE_PLATFORM_PASSWORD"], "pilot-tenant", "Synthetic-only-531-tenant"]
            baseline = app.fingerprint(db)
            checkpoint = (target / "human-checkpoint.json").read_bytes()
            with patch("builtins.input", side_effect=["HFPOS-SMOKE-ONLY", "VERIFICAR APP " + fixture.value["recovery_id"]]), \
                    patch.object(app.getpass, "getpass", side_effect=responses), redirect_stdout(io.StringIO()) as output:
                app.run_locked(argparse.Namespace(recovery_id=fixture.value["recovery_id"]), fixture.h, target,
                    {"source_sha": fixture.value["source_sha"]}, source)
            self.assertIn("APP_VERIFICATION_PASS", output.getvalue())
            self.assertEqual(baseline, app.fingerprint(db))
            self.assertEqual(checkpoint, (target / "human-checkpoint.json").read_bytes())
            fixture.h.verify_completed(argparse.Namespace(recovery_id=fixture.value["recovery_id"]), fixture.ops)
            # An absent SMTP is independent; the existing restored certificate must prove DP.
            app.sql(db, 'UPDATE public."CompanyEmailSettings" SET "SmtpPasswordProtected"=NULL;')
            (target / "application-checkpoint.json").unlink()  # Test-owned fixture ONLY, not operational retry.
            with patch("builtins.input", side_effect=["HFPOS-SMOKE-ONLY", "VERIFICAR APP " + fixture.value["recovery_id"]]), \
                    patch.object(app.getpass, "getpass", side_effect=responses), redirect_stdout(io.StringIO()) as output:
                app.run_locked(argparse.Namespace(recovery_id=fixture.value["recovery_id"]), fixture.h, target,
                    {"source_sha": fixture.value["source_sha"]}, source)
            self.assertIn("APP_VERIFICATION_PASS", output.getvalue())
            self.assertIn('"smtp":"NOT_PRESENT"', output.getvalue())
            self.assertIn('"certificate":"PASS_EXISTING_CERTIFICATE"', output.getvalue())
            # Both wrong discriminator and damaged stored bytes fail on the actual RC6 probe.
            original_passwords = app.decode(app.sql(db, '''SELECT json_agg(json_build_array("Id",encode("EncryptedPassword",'hex')))
                FROM public."CompanySriCertificates";'''))
            self.assertTrue(original_passwords)
            for wrong_application, corrupt in ((True, False), (False, True)):
                (target / "application-checkpoint.json").unlink()
                if corrupt:
                    app.sql(db, 'UPDATE public."CompanySriCertificates" SET "EncryptedPassword"=decode(\'00\',\'hex\');')
                with patch("builtins.input", side_effect=["SYNTHETIC-WRONG" if wrong_application else "HFPOS-SMOKE-ONLY",
                        "VERIFICAR APP " + fixture.value["recovery_id"]]), patch.object(app.getpass, "getpass", side_effect=responses):
                    with self.assertRaises(ValueError):
                        app.run_locked(argparse.Namespace(recovery_id=fixture.value["recovery_id"]), fixture.h, target,
                            {"source_sha": fixture.value["source_sha"]}, source)
                for cert_id, encrypted in original_passwords:
                    self.assertRegex(encrypted, r"^[a-f0-9]+$")
                    app.sql(db, 'UPDATE public."CompanySriCertificates" SET "EncryptedPassword"=decode(\'' + encrypted +
                        '\',\'hex\') WHERE "Id"=' + str(int(cert_id)) + ';')
            # No protected evidence: partial, without seeding a substitute.
            app.sql(db, 'DELETE FROM public."CompanySriCertificates";')
            (target / "application-checkpoint.json").unlink()
            with patch("builtins.input", side_effect=["HFPOS-SMOKE-ONLY", "VERIFICAR APP " + fixture.value["recovery_id"]]), \
                    patch.object(app.getpass, "getpass", side_effect=responses), redirect_stdout(io.StringIO()) as output:
                app.run_locked(argparse.Namespace(recovery_id=fixture.value["recovery_id"]), fixture.h, target,
                    {"source_sha": fixture.value["source_sha"]}, source)
            self.assertIn("APP_VERIFICATION_PARTIAL_HUMAN_GATE", output.getvalue())
            self.assertNotIn("APP_VERIFICATION_PASS", output.getvalue())
            # A client timeout must terminate ONLY the new app namespace, not its DB/network owner.
            (target / "application-checkpoint.json").unlink()  # Fresh test-only attempt.
            real_docker = app.docker
            def timeout_client(*args, **kwargs):
                if args[0] == "start":
                    real_docker("start", args[-1])
                    raise subprocess.TimeoutExpired("synthetic-docker-client", 180)
                return real_docker(*args, **kwargs)
            baseline = app.fingerprint(db)
            with patch("builtins.input", side_effect=["HFPOS-SMOKE-ONLY", "VERIFICAR APP " + fixture.value["recovery_id"]]), \
                    patch.object(app.getpass, "getpass", side_effect=responses), patch.object(app, "docker", side_effect=timeout_client):
                with self.assertRaises(subprocess.TimeoutExpired):
                    app.run_locked(argparse.Namespace(recovery_id=fixture.value["recovery_id"]), fixture.h, target,
                        {"source_sha": fixture.value["source_sha"]}, source)
            failed = app.decode((target / "application-checkpoint.json").read_bytes())
            self.assertFalse(app.decode(app.docker("inspect", failed["new_app_id"]))[0]["State"]["Running"])
            self.assertTrue(app.decode(app.docker("inspect", db))[0]["State"]["Running"])
            self.assertEqual(baseline, app.fingerprint(db))
            self.assertEqual(checkpoint, (target / "human-checkpoint.json").read_bytes())
        finally:
            # Only names belonging to this isolated synthetic fixture; never a real-host cleanup path.
            for name in app.docker("ps", "-aq", "--filter", "label=hfpos.application.source=" + "e" * 40).decode().split():
                app.docker("rm", "-f", name)
            for name in created:
                if app.docker("inspect", name, check=False).returncode == 0:
                    item = app.decode(app.docker("inspect", name))[0]
                    self.assertEqual(fixture.value["source_sha"], item["Config"]["Labels"]["hfpos.human.source"])
                    app.docker("rm", "-f", name)
            if target.exists():
                self.assertEqual(fixture.h.RECOVERY / fixture.value["recovery_id"], target)
                shutil.rmtree(target)
            for path in reversed(parents):
                path.rmdir()
            fixture.tearDown()


if __name__ == "__main__":
    unittest.main()
