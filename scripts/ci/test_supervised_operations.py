#!/usr/bin/env python3
"""Synthetic-only native protocol/archive/Windows ACL tests; Linux root tests use owned scratch."""
import copy
from contextlib import nullcontext
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
OPS = ROOT / "scripts/ops"


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, OPS / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


ops = load("supervised_ops_test", "supervised-operations.py")
archives = load("supervised_archive_test", "supervised-archive.py")
bridge = load("supervised_bridge_test", "supervised-bridge.py")
REQUEST = {"version": 1, "repository": ops.REPOSITORY, "action": "preflight",
           "source_sha": "a" * 40, "bundle_sha256": ops.digest_bundle(OPS),
           "recovery_id": "b" * 32, "ciphertext_sha256": "c" * 64,
           "run_id": "123456", "run_attempt": 1}


def plan():
    return dict(REQUEST, expires=int(time.time()) + 300, nonce="d" * 32, transfer_size=0,
                transfer_sha256=hashlib.sha256(b"").hexdigest(), transfer_manifest_sha256="0" * 64)


def tar_bytes(files, root=False):
    buffer = io.BytesIO()
    with tarfile.open(fileobj=buffer, mode="w", format=tarfile.USTAR_FORMAT) as archive:
        if root:
            item = tarfile.TarInfo("./")
            item.type = tarfile.DIRTYPE
            archive.addfile(item)
        for name, data in files.items():
            item = tarfile.TarInfo(name)
            item.size = len(data)
            item.mode = 0o600
            archive.addfile(item, io.BytesIO(data))
    return buffer.getvalue()


def fixture(dump=b"synthetic-only-dump"):
    dump_name = "db/hfpos-20261009T170757Z-1.dump"
    key_name = "keyring/keyring-20261009T170757Z-1.tar"
    key = tar_bytes({"./key-synthetic.xml": b"<key>synthetic-only</key>"}, root=True)
    digest = hashlib.sha256(dump).hexdigest()
    values = {dump_name: dump, key_name: key,
              dump_name + ".sha256": (digest + "  " + Path(dump_name).name + "\n").encode(),
              dump_name + ".manifest": ("format_version=1\ncreated_utc=20261009T170757Z\npostgres_client_version=16\n"
                                         "app_release=synthetic\ndatabase_name=synthetic\nsha256=" + digest + "\n").encode(),
              key_name + ".sha256": (hashlib.sha256(key).hexdigest() + "  " + Path(key_name).name + "\n").encode(),
              "metadata/release.txt": b"synthetic-release-only\n", "snapshot/config.tar": b"SYNTHETIC_SECRET_SENTINEL"}
    values["metadata/internal-sha256.txt"] = "".join(hashlib.sha256(data).hexdigest() + "  " + name + "\n"
                                                   for name, data in values.items()).encode()
    return values


def private_fixture(folder):
    if os.name != "nt":
        return folder
    folder = folder / "private synthetic workspace"
    folder.mkdir(mode=0o755)
    setup = folder / "setup-acl.ps1"
    setup.write_text("param([string]$Path)\n$ErrorActionPreference='Stop'\n"
                     "$a=New-Object Security.AccessControl.DirectorySecurity;$a.SetAccessRuleProtection($true,$false);"
                     "$u=[Security.Principal.WindowsIdentity]::GetCurrent().User;$a.SetOwner($u);"
                     "$r=New-Object Security.AccessControl.FileSystemAccessRule($u,'FullControl','ContainerInherit,ObjectInherit','None','Allow');"
                     "$a.SetAccessRule($r);Set-Acl -LiteralPath $Path -AclObject $a")
    env = {key: value for key, value in os.environ.items() if key.lower() != "psmodulepath"}
    result = subprocess.run(["powershell.exe", "-NoProfile", "-File", str(setup), "-Path", str(folder)], capture_output=True, text=True, env=env)
    if result.returncode:
        raise AssertionError("Synthetic ACL setup failed: " + result.stderr)
    return folder


def synthetic_source_sql(name, sql):
    ops.require(name.startswith("hfpos-supervised-source-hfpos-supervised-test-"))
    result = subprocess.run(["/usr/bin/docker", "exec", name, "psql", "-X", "-h", "127.0.0.1",
                             "-v", "ON_ERROR_STOP=1", "-v", "VERBOSITY=sqlstate", "-U", "synthetic",
                             "-d", "synthetic", "-c", sql], capture_output=True, timeout=30)
    if result.returncode:
        # CI-only disposable fixture diagnostics; never print native text or SQL.
        error = result.stderr[:8192]
        category = "other"
        for label, token in (("connection_refused", b"connection refused"),
                             ("server_shutdown", b"shutting down"),
                             ("server_starting", b"starting up"),
                             ("database_missing", b'database "synthetic" does not exist'),
                             ("role_missing", b'role "synthetic" does not exist')):
            if token in error.lower():
                category = label
                break
        match = re.search(rb"(?:ERROR|FATAL|PANIC):\s+([0-9A-Z]{5})(?:\s|$)", error)
        states = {b"42601", b"42P01", b"42501", b"3D000", b"28000", b"57P01", b"57P02", b"57P03",
                  b"08000", b"08001", b"08006", b"XX000"}
        state = match[1].decode("ascii") if match and match[1] in states else "other"
        raise AssertionError("Synthetic source SQL failed: exit=" + str(result.returncode) +
                             "; category=" + category + "; sqlstate=" + state)


class ReadinessTests(unittest.TestCase):
    def test_tcp_wait_rejects_temporary_socket_server_then_accepts_final(self):
        attempts = []
        def server(*args, **kwargs):
            attempts.append((args, kwargs))
            if "-h" not in args:
                return b"socket accepting connections"
            self.assertEqual("127.0.0.1", args[args.index("-h") + 1])
            if len(attempts) < 3:
                raise ops.Denied()
            return b"127.0.0.1 accepting connections"
        with patch.object(ops, "docker", side_effect=server), patch.object(ops.time, "sleep") as sleep:
            ops.wait_postgres_tcp("synthetic-only", "synthetic", "synthetic")
        self.assertEqual(3, len(attempts))
        self.assertEqual(2, sleep.call_count)
        for args, kwargs in attempts:
            self.assertEqual(("exec", "synthetic-only", "pg_isready", "-h", "127.0.0.1", "-p", "5432", "-t", "1",
                              "-U", "synthetic", "-d", "synthetic"), args)
            self.assertEqual({"timeout": 5}, kwargs)

    def test_tcp_wait_exhaustion_denies_instead_of_falling_through(self):
        with patch.object(ops, "docker", side_effect=ops.Denied) as docker, patch.object(ops.time, "sleep") as sleep:
            with self.assertRaises(ops.Denied):
                ops.wait_postgres_tcp("synthetic-only", "synthetic", "synthetic")
        self.assertEqual(30, docker.call_count)
        self.assertEqual(30, sleep.call_count)

    def test_runtime_readiness_failure_precedes_probes_and_restore(self):
        with tempfile.TemporaryDirectory() as folder:
            workspace = Path(folder)
            for name in ("postgres-data", "keyring-restored"):
                (workspace / name).mkdir()
            with patch.object(ops, "validate_import", return_value={}), \
                    patch.object(ops, "docker", return_value=b""), \
                    patch.object(ops, "write_checkpoint"), \
                    patch.object(ops, "inspect_isolation", return_value="f" * 64), \
                    patch.object(ops, "wait_postgres_tcp", side_effect=ops.Denied) as ready, \
                    patch.object(ops, "probe_egress") as probe, \
                    patch.object(ops, "stream_container_files") as transfer:
                with self.assertRaises(ops.Denied):
                    ops.restore_core(plan(), workspace, OPS)
                ready.assert_called_once_with("hfpos-recovery-" + REQUEST["recovery_id"],
                                              "hfpos_recovery_admin", "hfpos_recovery")
                probe.assert_not_called()
                transfer.assert_not_called()

    def test_synthetic_source_diagnostics_never_publish_native_text(self):
        for error, category, state in ((b"connection refused SYNTHETIC_SECRET_SENTINEL", "connection_refused", "other"),
                                       (b"ERROR: 42601\nSYNTHETIC_SECRET_SENTINEL", "other", "42601"),
                                       (b"ERROR: ABCDE\nSYNTHETIC_SECRET_SENTINEL", "other", "other")):
            result = subprocess.CompletedProcess([], 2, stdout=b"SYNTHETIC_SECRET_SENTINEL", stderr=error)
            with patch.object(subprocess, "run", return_value=result) as command, self.assertRaises(AssertionError) as failure:
                synthetic_source_sql("hfpos-supervised-source-hfpos-supervised-test-fixture", "synthetic SQL")
            self.assertEqual("Synthetic source SQL failed: exit=2; category=" + category + "; sqlstate=" + state,
                             str(failure.exception))
            self.assertIn("127.0.0.1", command.call_args.args[0])


class ProtocolTests(unittest.TestCase):
    def test_fixed_container_stream_ustar_allowlist_and_hashes(self):
        files = {"ops/postgres-restore.sh": b"reviewed-synthetic-script",
                 "hfpos-20261009T170757Z-1.dump": b"synthetic-only-dump"}
        def streamed(*args, **kwargs):
            if "tar" in args:
                self.assertEqual(("exec", "-i", "--user", "0", "synthetic-only", "tar", "-xf", "-", "-C", "/tmp", "--no-same-owner"), args)
                with tarfile.open(fileobj=io.BytesIO(kwargs["data"]), mode="r:") as archive:
                    self.assertEqual(set(files), set(archive.getnames()))
                    for member in archive:
                        self.assertTrue(member.isfile() and not member.pax_headers)
                        self.assertEqual(files[member.name], archive.extractfile(member).read())
                return b""
            self.assertEqual("sha256sum", args[2])
            return hashlib.sha256(files[args[3].removeprefix("/tmp/")]).hexdigest().encode() + b"  synthetic"
        with patch.object(ops, "docker", side_effect=streamed) as docker:
            ops.stream_container_files("synthetic-only", files)
            self.assertEqual(3, docker.call_count)
        for filename in ("../evil", "/tmp/evil", "ops/uploaded-script.sh", "hfpos.dump"):
            with patch.object(ops, "docker") as docker, self.assertRaises(ops.Denied):
                ops.stream_container_files("synthetic-only", {filename: b"never-upload"})
            docker.assert_not_called()

    def test_required_ci_native_age_is_available(self):
        if os.environ.get("HFPOS_SUPERVISED_NATIVE_AGE") == "YES":
            self.assertTrue(shutil.which("age") and shutil.which("age-keygen"), "CI native age test must not silently skip")

    def test_bounded_payload_reader_short_extra_and_exact(self):
        self.assertEqual(b"abc", ops.read_payload(io.BytesIO(b"abc"), 3))
        for data in (b"ab", b"abcd"):
            with self.assertRaises(ops.TransferAborted):
                ops.read_payload(io.BytesIO(data), 3)

    def test_ipv6_modes_and_loopback_only_routes(self):
        for values, expected in (([b"no", b""], "kernel-disabled"),
                                 ([b"yes", b"0", b"1", b""], "namespace-disabled"),
                                 ([b"yes", b"0", b"0", b""], "unconfigured-loopback"),
                                 ([b"yes", b"0", b"0", ("0" * 31 + "1 01 80 10 80 lo\n").encode()], "operational-loopback")):
            with patch.object(ops, "docker", side_effect=values):
                self.assertEqual(expected, ops.ipv6_mode("synthetic-only"))
        ops.loopback_routes(b"::1 dev lo proto kernel\n")
        ops.loopback_routes(b"127.0.0.0/8 dev lo\n")
        for route in (b"default via 192.0.2.1 dev eth0", b"2001:db8::/64 dev lo", b"::1 dev eth0"):
            with self.assertRaises((ops.Denied, ValueError)):
                ops.loopback_routes(route)

    def test_disabled_ipv6_does_not_skip_real_client_negative_probes(self):
        with patch.object(ops, "ipv6_mode", return_value="namespace-disabled"), \
                patch.object(ops, "docker", return_value=b"1") as positive, \
                patch.object(ops.subprocess, "run", return_value=subprocess.CompletedProcess([], 2)) as negative:
            self.assertEqual("namespace-disabled", ops.probe_egress("synthetic-only"))
            self.assertEqual(1, positive.call_count)
            self.assertIn("127.0.0.1", positive.call_args.args)
            self.assertEqual(2, negative.call_count)
            self.assertTrue(all(call.args[0][0] == "/usr/bin/docker" and "psql" in call.args[0] for call in negative.call_args_list))

    def test_strict_bounds_and_duplicates(self):
        self.assertEqual(REQUEST, ops.validate_request(copy.deepcopy(REQUEST)))
        cases = {"action": ["start-app", "restore;id", "../preflight"], "source_sha": ["main", "A" * 40],
                 "recovery_id": ["../prod", "a" * 33], "run_id": ["0", "1;id", "9" * 21],
                 "run_attempt": [2, True], "version": [True, 2], "ciphertext_sha256": ["f" * 63]}
        for key, values in cases.items():
            for value in values:
                with self.subTest(key=key, value=value), self.assertRaises(ops.Denied):
                    ops.validate_request(dict(REQUEST, **{key: value}))
        with self.assertRaises(ops.Denied):
            ops.decode(b'{"a":1,"a":2}')
        with self.assertRaises(ops.Denied):
            ops.validate_request(dict(REQUEST, command="id"))
        for expires in (int(time.time()) - 1, int(time.time()) + 601, True):
            with self.assertRaises(ops.Denied):
                ops.validate_plan(dict(plan(), expires=expires))

    def test_native_signature_wrong_namespace_and_tamper(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            key = folder / "signing"
            subprocess.run(["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(key)], check=True, capture_output=True)
            signed = folder / "plan"
            signed.write_bytes(ops.canonical(plan()))
            signers = folder / "allowed_signers"
            signers.write_text('fernando namespaces="hf-one-r4" ' + Path(str(key) + ".pub").read_text())
            subprocess.run(["ssh-keygen", "-Y", "sign", "-f", str(key), "-n", ops.NAMESPACE, str(signed)], check=True, capture_output=True)
            signature = Path(str(signed) + ".sig").read_text()
            ops.verify_signature(ops.decode(signed.read_bytes()), signature, signers, folder)
            with self.assertRaises(ops.Denied):
                ops.verify_signature(dict(ops.decode(signed.read_bytes()), action="isolated-restore"), signature, signers, folder)
            signers.write_text('fernando namespaces="wrong-namespace" ' + Path(str(key) + ".pub").read_text())
            with self.assertRaises(ops.Denied):
                ops.verify_signature(ops.decode(signed.read_bytes()), signature, signers, folder)

    def test_transport_pinning_and_public_schema(self):
        config = {"port": 22, "host": "synthetic.example.invalid", "transport_key": "/private/synthetic-key",
                  "known_hosts": "/private/synthetic-hosts"}
        command = bridge.ssh_command(config)
        self.assertIn("StrictHostKeyChecking=yes", command)
        self.assertIn("IdentityAgent=none", command)
        self.assertIn("ClearAllForwardings=yes", command)
        self.assertEqual("hfpos-supervised-v1", command[-1])
        expected = ops.report(REQUEST, "PREFLIGHT_PASS")
        self.assertEqual(expected, bridge.validate_report(expected, REQUEST))
        with self.assertRaises(bridge.ops.Denied):
            bridge.validate_report(dict(expected, stderr="SYNTHETIC_SECRET_SENTINEL"), REQUEST)

    def test_native_ssh_config_with_spaced_known_hosts_without_connection(self):
        with tempfile.TemporaryDirectory(prefix="supervised spaces ") as folder:
            hosts = Path(folder) / "known hosts synthetic"
            hosts.write_text("synthetic.example.invalid ssh-ed25519 SYNTHETIC-ONLY\n")
            config = {"port": 22, "host": "synthetic.example.invalid", "transport_key": str(Path(folder) / "synthetic key"),
                      "known_hosts": str(hosts)}
            command = bridge.ssh_command(config)
            result = subprocess.run([command[0], "-G", *command[1:]], capture_output=True, text=True)
            self.assertEqual(0, result.returncode, "ssh -G rejected spaced pinned path")
            self.assertIn("stricthostkeychecking true", result.stdout.lower())
            self.assertIn("known hosts synthetic", result.stdout)
            self.assertIn("clearallforwardings yes", result.stdout.lower())

    def test_ref_fork_attempt_and_sha_rejection(self):
        run = {"event": "workflow_dispatch", "head_branch": "main", "path": bridge.WORKFLOW,
               "head_sha": REQUEST["source_sha"], "run_attempt": 1, "status": "completed", "conclusion": "success",
               "repository": {"full_name": ops.REPOSITORY}, "head_repository": {"full_name": ops.REPOSITORY}}
        bridge.validate_run(run, REQUEST["source_sha"])
        for key, value in (("head_branch", "feature"), ("run_attempt", 2), ("head_sha", "e" * 40),
                           ("event", "pull_request"), ("path", ".github/workflows/untrusted.yml"),
                           ("head_repository", {"full_name": "fork/hfpos"})):
            with self.assertRaises(bridge.ops.Denied):
                bridge.validate_run(dict(run, **{key: value}), REQUEST["source_sha"])

    def test_no_decrypt_or_transport_before_human_approval(self):
        request = dict(REQUEST, action="isolated-restore")
        with patch("builtins.input", return_value="NO"), patch.object(bridge.archives, "decrypt_export") as decrypt, \
                patch.object(bridge.subprocess, "run") as transport, patch("builtins.print"):
            with self.assertRaises(bridge.ops.Denied):
                bridge.operate(request, {}, Path("unused"))
            decrypt.assert_not_called()
            transport.assert_not_called()

    def test_transport_timeout_is_uncertain_never_retried(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            config = {"signing_key": "synthetic-only", "port": 22, "host": "synthetic.example.invalid",
                      "transport_key": "synthetic-only", "known_hosts": "synthetic-only"}
            executions = []
            def native(args, **kwargs):
                if args[0] == "ssh-keygen":
                    return subprocess.CompletedProcess(args, 0, stdout=b"-----BEGIN SSH SIGNATURE-----\nSYNTHETIC\n-----END SSH SIGNATURE-----\n")
                executions.append(args)
                raise subprocess.TimeoutExpired(args, 1)
            def approve(prompt):
                if "AUTORIZAR" in prompt:
                    return "AUTORIZAR " + REQUEST["run_id"]
                return "FIRMAR " + prompt.split("FIRMAR ", 1)[1].split(":", 1)[0]
            with patch.object(bridge.archives, "private_temporary", return_value=nullcontext(folder)), \
                    patch("builtins.input", side_effect=approve), patch("builtins.print"), \
                    patch.object(bridge.subprocess, "run", side_effect=native):
                result = bridge.operate(REQUEST, config, folder)
            self.assertEqual("UNCERTAIN", result["status"])
            self.assertEqual(1, len(executions))

    def test_artifact_no_path_or_zip_bomb(self):
        def zipped(name, data):
            buffer = io.BytesIO()
            with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as archive:
                archive.writestr(name, data)
            return buffer.getvalue()
        self.assertEqual(REQUEST, bridge.read_request(zipped("request.json", ops.canonical(REQUEST))))
        for name, data in (("../request.json", b"{}"), ("request.json", b"x" * 4097)):
            with self.assertRaises(bridge.ops.Denied):
                bridge.read_request(zipped(name, data))


class ArchiveTests(unittest.TestCase):
    def test_filtered_gnu_root_hashes_and_no_snapshot(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            folder = private_fixture(folder)
            tar = folder / "fixture.tar"
            tar.write_bytes(tar_bytes(fixture(), root=True))
            result = archives.filtered_export(tar, folder / "export", REQUEST["recovery_id"], REQUEST["ciphertext_sha256"])
            self.assertEqual(REQUEST["ciphertext_sha256"], result["ciphertext_sha256"])
            self.assertFalse((folder / "export/snapshot").exists())
            self.assertTrue(all(not name.startswith("snapshot/") for name in result["files"]))
            blob, digest = bridge.transfer_blob(folder / "export")
            self.assertEqual(digest, hashlib.sha256(ops.canonical(result)).hexdigest())
            self.assertNotIn(b"SYNTHETIC_SECRET_SENTINEL", blob)

    def test_bad_hash_and_ambiguous_dump_denied(self):
        for mutation in ("hash", "multiple"):
            values = fixture()
            if mutation == "hash":
                values["db/hfpos-20261009T170757Z-1.dump"] += b"tampered"
            else:
                values["db/hfpos-20261009T170757Z-2.dump"] = b"extra"
            with tempfile.TemporaryDirectory() as folder:
                folder = Path(folder)
                tar = folder / "bad.tar"
                tar.write_bytes(tar_bytes(values))
                with self.assertRaises(archives.ops.Denied):
                    archives.filtered_export(tar, folder / "export", REQUEST["recovery_id"], REQUEST["ciphertext_sha256"])
                self.assertFalse((folder / "export").exists())

    def test_links_pax_duplicates_traversal_and_bombs(self):
        for name, kind, size, pax in (("../db/x", tarfile.REGTYPE, 0, {}),
                                      ("db/../../x", tarfile.REGTYPE, 0, {}),
                                      ("db/x", tarfile.SYMTYPE, 0, {}), ("db/x", tarfile.LNKTYPE, 0, {}),
                                      ("db/x", tarfile.REGTYPE, archives.MAX_FILE + 1, {}),
                                      ("db/x", tarfile.REGTYPE, 0, {"path": "../x"}),
                                      (".", tarfile.REGTYPE, 0, {}), ("db\\x", tarfile.REGTYPE, 0, {})):
            member = tarfile.TarInfo(name)
            member.type, member.size, member.pax_headers = kind, size, pax
            with self.subTest(name=name, kind=kind), self.assertRaises(archives.ops.Denied):
                archives.member_name(member)
        data = io.BytesIO()
        with tarfile.open(fileobj=data, mode="w") as tar:
            for _ in range(2):
                member = tarfile.TarInfo("./key-synthetic.xml")
                tar.addfile(member, io.BytesIO(b""))
        with self.assertRaises(archives.ops.Denied):
            archives.validate_keyring(data.getvalue())

    def test_age_late_authentication_failure_never_publishes(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            folder = private_fixture(folder)
            ciphertext = folder / "ciphertext.age"
            archives.private_write(ciphertext, b"synthetic-ciphertext")
            archives.private_write(folder / "synthetic-key", b"synthetic-only")
            digest = hashlib.sha256(ciphertext.read_bytes()).hexdigest()
            # Real subprocess emits plaintext then fails; no crypto or real key used.
            command = [sys.executable, "-c", "import sys;sys.stdout.buffer.write(b'partial-plaintext');sys.exit(1)"]
            real = subprocess.Popen
            calls = []
            def emit(args, **kwargs):
                if args[0] != "age":
                    return real(args, **kwargs)
                calls.append(args)
                return real(command, **kwargs)
            with patch.object(archives.subprocess, "Popen", side_effect=emit):
                with self.assertRaises(archives.ops.Denied):
                    archives.decrypt_export(ciphertext, folder / "synthetic-key", folder, folder / "export",
                                            REQUEST["recovery_id"], digest)
            self.assertEqual(1, len(calls), "Late-auth fixture must actually emit plaintext")
            self.assertFalse((folder / "export").exists())

    def test_ciphertext_swap_cannot_change_authenticated_snapshot(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            folder = private_fixture(folder)
            ciphertext = folder / "ciphertext.age"
            content = tar_bytes(fixture(), root=True)
            archives.private_write(ciphertext, content)
            archives.private_write(folder / "synthetic-key", b"synthetic-only")
            digest = hashlib.sha256(content).hexdigest()
            real = subprocess.Popen
            def swap(args, **kwargs):
                if args[0] != "age":
                    return real(args, **kwargs)
                ciphertext.write_bytes(b"attacker-replacement")
                command = [sys.executable, "-c", "import pathlib,sys;sys.stdout.buffer.write(pathlib.Path(sys.argv[1]).read_bytes())", args[-1]]
                return real(command, **kwargs)
            with patch.object(archives.subprocess, "Popen", side_effect=swap):
                result = archives.decrypt_export(ciphertext, folder / "synthetic-key", folder, folder / "export",
                                                 REQUEST["recovery_id"], digest)
            self.assertEqual(digest, result["ciphertext_sha256"])


@unittest.skipUnless(os.name == "nt", "Windows-only native ACL helper")
class WindowsAclTests(unittest.TestCase):
    def test_private_creation_owner_current_and_never_adopts_existing(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = private_fixture(Path(folder))
            child = folder / "owned nested"
            archives.private_create(child, directory=True)
            file = child / "owned file"
            archives.private_write(file, b"synthetic-only")
            archives.private_windows(child)
            archives.private_windows(file)
            with self.assertRaises(archives.ops.Denied):
                archives.private_create(file)
            self.assertEqual(b"synthetic-only", file.read_bytes())

    def test_shared_agent_sid_denied_before_key_access(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            config = {"version": 1, "source_sha": REQUEST["source_sha"], "bundle_sha256": REQUEST["bundle_sha256"],
                      "agent_sid": "S-1-5-21-531", "human_sid": "S-1-5-21-531", "host": "synthetic.example.invalid", "port": 22,
                      "transport_key": "unused", "signing_key": "unused", "known_hosts": "unused", "age_identity": "unused",
                      "ciphertext_directory": "unused"}
            (folder / "bridge.json").write_bytes(ops.canonical(config))
            with patch.object(bridge.archives, "private_windows") as acl, \
                    patch.object(bridge.ops, "native", return_value=b"S-1-5-21-531"):
                with self.assertRaises(bridge.ops.Denied):
                    bridge.validate_local_installation(folder)
                self.assertEqual(2, acl.call_count, "No key/ciphertext ACL/access before identity gate")

    def test_real_file_helper_space_path_and_junction(self):
        with tempfile.TemporaryDirectory(prefix="supervised synthetic spaces ") as folder:
            folder = Path(folder)
            private = folder / "private with spaces"
            private.mkdir()
            private = private_fixture(private)
            archives.private_windows(private)
            key = private / "synthetic key only"
            archives.private_write(key, b"synthetic-only")
            archives.private_windows(key)
            with self.assertRaises(archives.ops.Denied):
                archives.private_windows(folder)
            junction = private / "junction"
            subprocess.run(["powershell.exe", "-NoProfile", "-Command",
                            "New-Item -ItemType Junction -Path '" + str(junction).replace("'", "''") +
                            "' -Target '" + str(folder).replace("'", "''") + "' | Out-Null"], check=True, capture_output=True,
                           env={key: value for key, value in os.environ.items() if key.lower() != "psmodulepath"})
            try:
                with self.assertRaises(archives.ops.Denied):
                    archives.private_windows(junction / "private with spaces/private synthetic workspace/setup-acl.ps1")
            finally:
                junction.rmdir()


@unittest.skipUnless(os.name == "posix" and os.geteuid() == 0, "Linux root-owned synthetic scratch")
class RootProtocolTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="hfpos-supervised-test-", dir="/var/lib")
        self.folder = Path(self.temporary.name)
        self.folder.chmod(0o700)
        self.state = self.folder / "state"
        for path in (self.state, self.state / "consumed", self.state / "audit", self.folder / "recovery"):
            path.mkdir(mode=0o700)
        self.workspace = self.folder / "recovery" / REQUEST["recovery_id"]
        self.workspace.mkdir(mode=0o700)
        for name in ("incoming", "postgres-data", "keyring-restored"):
            (self.workspace / name).mkdir(mode=0o700)
        self.key = self.folder / "synthetic-key"
        subprocess.run(["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(self.key)], check=True, capture_output=True)
        signers = self.state / "allowed_signers"
        signers.write_text('fernando namespaces="hf-one-r4" ' + Path(str(self.key) + ".pub").read_text())
        signers.chmod(0o600)
        self.config = {"source_sha": REQUEST["source_sha"], "bundle_sha256": REQUEST["bundle_sha256"]}

    def tearDown(self):
        self.temporary.cleanup()

    def signed(self, value):
        result = subprocess.run(["ssh-keygen", "-Y", "sign", "-f", str(self.key), "-n", ops.NAMESPACE],
                                input=ops.canonical(value), check=True, capture_output=True)
        return {"plan": value, "signature": result.stdout.decode("ascii")}

    def execute(self, envelope, payload=b""):
        return ops.execute_envelope(envelope, self.config, self.state, self.folder / "recovery", OPS, payload)

    def test_real_signed_preflight_replay_and_nonce_nonrepeat(self):
        envelope = self.signed(plan())
        self.assertEqual("PREFLIGHT_PASS", self.execute(envelope)["status"])
        with self.assertRaises(FileExistsError):
            self.execute(envelope)
        next_plan = dict(plan(), run_id="123457")
        with self.assertRaises(FileExistsError):
            self.execute(self.signed(next_plan))
        self.assertEqual(1, len(list((self.state / "audit").iterdir())))

    def test_unsigned_expired_modified_bundle_and_unsafe_path(self):
        envelope = self.signed(plan())
        with self.assertRaises(ops.Denied):
            self.execute(dict(envelope, signature="not-signed"))
        with self.assertRaises(ops.Denied):
            self.execute(self.signed(dict(plan(), expires=int(time.time()) - 1, nonce="e" * 32)))
        wrong = dict(self.config, bundle_sha256="e" * 64)
        with self.assertRaises(ops.Denied):
            ops.execute_envelope(envelope, wrong, self.state, self.folder / "recovery", OPS)
        (self.workspace / "incoming").rmdir()
        (self.workspace / "incoming").symlink_to(self.folder / "recovery", target_is_directory=True)
        self.assertEqual("FAILED", self.execute(envelope)["status"])

    def test_signed_receive_checkpoint_and_corruption(self):
        archive = self.folder / "fixture.tar"
        archive.write_bytes(tar_bytes(fixture(), root=True))
        archives.filtered_export(archive, self.folder / "export", REQUEST["recovery_id"], REQUEST["ciphertext_sha256"])
        blob, manifest_hash = bridge.transfer_blob(self.folder / "export")
        value = dict(plan(), action="isolated-restore", transfer_size=len(blob),
                     transfer_sha256=hashlib.sha256(blob).hexdigest(), transfer_manifest_sha256=manifest_hash)
        ops.receive_transfer(value, blob, self.workspace)
        ops.receive_transfer(value, blob, self.workspace)
        self.assertFalse((self.workspace / "incoming/snapshot").exists())
        with self.assertRaises(ops.Denied):
            ops.receive_transfer(value, blob + b"tamper", self.workspace)
        dump = self.workspace / "incoming/db/hfpos-20261009T170757Z-1.dump"
        dump.write_bytes(b"changed")
        with self.assertRaises(ops.Denied):
            ops.receive_transfer(value, blob, self.workspace)

    def test_forced_command_rejects_shell_sftp_and_arguments(self):
        for command in ("", "bash", "sftp", "hfpos-supervised-v1;id", "hfpos-supervised-v1 anything"):
            result = subprocess.run(["/bin/sh", str(OPS / "supervised-ssh.sh")],
                                    env={"SSH_ORIGINAL_COMMAND": command}, capture_output=True)
            self.assertEqual(64, result.returncode)
        result = subprocess.run(["/bin/sh", str(OPS / "supervised-root.sh"), "anything"], capture_output=True)
        self.assertEqual(64, result.returncode)

    def test_actual_process_lock_prevents_concurrent_execution(self):
        command = [sys.executable, "-c", "import fcntl,sys,time;f=open(sys.argv[1],'a+b');"
                   "fcntl.flock(f,fcntl.LOCK_EX);print('locked',flush=True);time.sleep(20)", str(self.state / "lock")]
        process = subprocess.Popen(command, stdout=subprocess.PIPE)
        try:
            self.assertEqual(b"locked\n", process.stdout.readline())
            with self.assertRaises(BlockingIOError):
                ops.execute_envelope(self.signed(plan()), self.config, self.state, self.folder / "recovery", OPS,
                                     payload_stream=io.BytesIO(b"must-not-read"))
            self.assertFalse(list((self.state / "consumed").iterdir()))
        finally:
            process.terminate()
            process.wait()
            process.stdout.close()

    def test_bad_signature_and_replay_never_touch_blob(self):
        class Unreadable:
            def read(self, count):
                raise AssertionError("Unauthorized blob was read")
            def fileno(self):
                raise AssertionError("Unauthorized blob was accessed")
        envelope = self.signed(plan())
        for changed in (dict(envelope, signature="invalid"),
                        dict(envelope, plan=dict(envelope["plan"], transfer_size=128 * 1024 * 1024, action="isolated-restore"))):
            with self.assertRaises(ops.Denied):
                ops.execute_envelope(changed, self.config, self.state, self.folder / "recovery", OPS, payload_stream=Unreadable())
        self.assertFalse(list((self.state / "consumed").iterdir()))
        self.execute(envelope)
        with self.assertRaises(FileExistsError):
            ops.execute_envelope(envelope, self.config, self.state, self.folder / "recovery", OPS, payload_stream=Unreadable())

    def test_consumed_before_blob_and_short_transfer_uncertain(self):
        value = dict(plan(), action="isolated-restore", transfer_size=10)
        parent = self
        class ShortStream(io.BytesIO):
            def read(self, count):
                parent.assertTrue((parent.state / "consumed" / ("run-" + value["run_id"])).exists())
                parent.assertTrue((parent.state / "consumed" / ("nonce-" + value["nonce"])).exists())
                return super().read(count)
        envelope = self.signed(value)
        result = ops.execute_envelope(envelope, self.config, self.state, self.folder / "recovery", OPS,
                                      payload_stream=ShortStream(b"short"))
        self.assertEqual("UNCERTAIN", result["status"])
        self.assertFalse(list((self.workspace / "incoming").iterdir()))
        with self.assertRaises(FileExistsError):
            self.execute(envelope)

    def test_native_pipe_transfer_timeout(self):
        read, write = os.pipe()
        try:
            with os.fdopen(read, "rb", buffering=0) as source:
                with self.assertRaises(ops.TransferAborted):
                    ops.read_payload(source, 1, timeout=0.05)
        finally:
            os.close(write)


@unittest.skipUnless(shutil.which("age") and shutil.which("age-keygen"), "Native age tool unavailable locally")
class NativeAgeTests(unittest.TestCase):
    def test_real_synthetic_roundtrip_and_truncated_ciphertext(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = private_fixture(Path(folder))
            identity = folder / "synthetic-age-key"
            # Capture test-only key material privately, never include it in diagnostics.
            generated = subprocess.run(["age-keygen"], check=True, capture_output=True)
            archives.private_write(identity, generated.stdout)
            public = subprocess.run(["age-keygen", "-y", str(identity)], check=True, capture_output=True).stdout.decode().strip()
            encrypted = subprocess.run(["age", "--encrypt", "--recipient", public], input=tar_bytes(fixture(), root=True),
                                       check=True, capture_output=True).stdout
            ciphertext = folder / "synthetic.age"
            archives.private_write(ciphertext, encrypted)
            digest = hashlib.sha256(encrypted).hexdigest()
            manifest = archives.decrypt_export(ciphertext, identity, folder, folder / "export", REQUEST["recovery_id"], digest)
            self.assertEqual(digest, manifest["ciphertext_sha256"])
            self.assertFalse((folder / "export/snapshot").exists())
            truncated = folder / "truncated.age"
            archives.private_write(truncated, encrypted[:-16])
            with self.assertRaises(archives.ops.Denied):
                archives.decrypt_export(truncated, identity, folder, folder / "bad-export", REQUEST["recovery_id"],
                                        hashlib.sha256(encrypted[:-16]).hexdigest())
            self.assertFalse((folder / "bad-export").exists())


@unittest.skipUnless(os.name == "posix" and os.geteuid() == 0 and os.environ.get("HFPOS_SUPERVISED_DOCKER_TESTS") == "YES",
                     "CI-only disposable Docker E2E; local engine unavailable")
class DockerCoreTests(unittest.TestCase):
    setUp = RootProtocolTests.setUp
    tearDown = RootProtocolTests.tearDown
    signed = RootProtocolTests.signed
    def test_signed_fixed_serve_full_core_restore_and_resume(self):
        fixed_paths = [ops.BUNDLE.parent, ops.STATE, Path("/srv/hf-one"),
                       Path("/usr/local/libexec/hfpos-supervised-ssh"), Path("/usr/local/sbin/hfpos-supervised-root")]
        self.assertTrue(all(not path.exists() for path in fixed_paths), "Refuse preexisting host resources")
        source_name = "hfpos-supervised-source-" + self.folder.name
        recovery_id = hashlib.sha256(self.folder.name.encode()).hexdigest()[:32]
        destination_name = "hfpos-recovery-" + recovery_id
        created = []
        try:
            for name in (source_name, destination_name):
                self.assertFalse(ops.docker("ps", "-a", "--filter", "name=^/" + name + "$", "--format", "{{.ID}}").strip(),
                                 "Refuse preexisting test container")
            ops.docker("run", "-d", "--name", source_name, "--network", "none", "--read-only", "--user", "70:70",
                       "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--memory", "768m", "--cpus", "0.5",
                       "--pids-limit", "128", "--tmpfs", "/var/lib/postgresql/data:rw,uid=70,gid=70,mode=0700",
                       "--tmpfs", "/var/run/postgresql:rw,uid=70,gid=70", "--tmpfs", "/tmp:rw,mode=1777",
                       "-e", "POSTGRES_USER=synthetic", "-e", "POSTGRES_DB=synthetic", "-e", "POSTGRES_HOST_AUTH_METHOD=trust",
                       "--label", "hfpos.supervised.test=synthetic-only", ops.PG_IMAGE)
            created.append(source_name)
            try:
                ops.wait_postgres_tcp(source_name, "synthetic", "synthetic")
            except ops.Denied:
                self.fail("Synthetic source final PostgreSQL TCP readiness exhausted")
            tables = ("__EFMigrationsHistory", "Companies", "Users", "PlatformUsers", "Sales", "SaleItems", "ProductStocks",
                      "Products", "CashSessions", "PurchaseReceipts", "CreditNotes")
            sql = "".join('CREATE TABLE public."' + name + '"(id integer);INSERT INTO public."' + name + '" VALUES(531);' for name in tables)
            sql += "CREATE SEQUENCE public.synthetic_sequence START WITH 531;SELECT setval('public.synthetic_sequence',536,true);"
            synthetic_source_sql(source_name, sql)
            fingerprint_sql = ('SELECT md5(string_agg(id::text,\',\' ORDER BY id)) FROM public."Companies";'
                               'SELECT last_value,is_called FROM public.synthetic_sequence;')
            before = ops.docker("exec", source_name, "psql", "-X", "-At", "-U", "synthetic", "-d", "synthetic", "-c", fingerprint_sql)
            dumped = ops.docker("exec", source_name, "pg_dump", "-U", "synthetic", "-d", "synthetic", "--format=custom", "--no-owner",
                                "--no-privileges")
            self.assertTrue(dumped.startswith(b"PGDMP"), "Synthetic custom dump header invalid")
            dump_path = self.folder / "source.dump"
            dump_path.write_bytes(dumped)
            outer = self.folder / "outer.tar"
            outer.write_bytes(tar_bytes(fixture(dump_path.read_bytes()), root=True))
            archives.filtered_export(outer, self.folder / "export", recovery_id, REQUEST["ciphertext_sha256"])
            blob, manifest_hash = bridge.transfer_blob(self.folder / "export")
            value = dict(plan(), action="isolated-restore", recovery_id=recovery_id, transfer_size=len(blob), transfer_sha256=hashlib.sha256(blob).hexdigest(),
                         transfer_manifest_sha256=manifest_hash)
            for path in (ops.BUNDLE.parent, ops.BUNDLE, ops.STATE, ops.STATE / "consumed", ops.STATE / "audit",
                         ops.RECOVERY, ops.RECOVERY / recovery_id):
                path.mkdir(parents=True, mode=0o700, exist_ok=True)
                path.chmod(0o700)
            workspace = ops.RECOVERY / recovery_id
            for name in ("incoming", "postgres-data", "keyring-restored"):
                (workspace / name).mkdir(mode=0o700)
            for name in ops.BUNDLE_FILES:
                target = ops.BUNDLE / name
                target.write_bytes((OPS / name).read_bytes().replace(b"\r\n", b"\n"))
                target.chmod(0o600)
            for source, target in (("supervised-ssh.sh", fixed_paths[3]), ("supervised-root.sh", fixed_paths[4])):
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes((ops.BUNDLE / source).read_bytes())
                target.chmod(0o755)
            for name in ("allowed_signers",):
                shutil.copyfile(self.state / name, ops.STATE / name)
                (ops.STATE / name).chmod(0o600)
            (ops.STATE / "config.json").write_bytes(ops.canonical(self.config))
            (ops.STATE / "config.json").chmod(0o600)
            # Genuine Docker creation followed by an injected pre-SQL failure:
            # the next signed serve must reuse this exact isolated container.
            ops.receive_transfer(value, blob, workspace)
            created.append(destination_name)
            with patch.object(ops, "probe_egress", side_effect=ops.Denied):
                with self.assertRaises(ops.Denied):
                    ops.restore_core(value, workspace, ops.BUNDLE)
            partial = ops.decode((workspace / "core-checkpoint.json").read_bytes())
            self.assertEqual("container-created", partial["stage"])
            command = [sys.executable, "-I", str(ops.BUNDLE / "supervised-operations.py"), "serve"]
            envelope = self.signed(value)
            completed = subprocess.run(command, input=ops.canonical(envelope) + blob, capture_output=True, timeout=800)
            self.assertEqual(0, completed.returncode, "Fixed serve failed: " + completed.stdout.decode(errors="replace"))
            self.assertEqual("CORE_RESTORE_PASS_APP_BLOCKED", ops.decode(completed.stdout)["status"])
            self.assertEqual(partial["container_id"], ops.decode((workspace / "core-checkpoint.json").read_bytes())["container_id"])
            def sql_destination(statement, role="hfpos_recovery_owner"):
                return ops.docker("exec", destination_name, "psql", "-X", "-At", "-v", "ON_ERROR_STOP=1",
                                  "-U", role, "-d", "hfpos_recovery", "-c", statement)
            self.assertEqual(b"536|t", sql_destination("SELECT last_value,is_called FROM public.synthetic_sequence;").strip())
            sql_destination("CREATE TABLE public.synthetic_future(id integer);CREATE SEQUENCE public.synthetic_future_sequence;")
            self.assertEqual(b"t", sql_destination(
                "SELECT has_table_privilege(current_user,'public.synthetic_future','SELECT') "
                "AND has_table_privilege(current_user,'public.synthetic_future','INSERT') "
                "AND has_table_privilege(current_user,'public.synthetic_future','UPDATE') "
                "AND has_table_privilege(current_user,'public.synthetic_future','DELETE') "
                "AND has_sequence_privilege(current_user,'public.synthetic_future_sequence','USAGE') "
                "AND has_sequence_privilege(current_user,'public.synthetic_future_sequence','SELECT') "
                "AND NOT has_sequence_privilege(current_user,'public.synthetic_future_sequence','UPDATE');",
                "hfpos_recovery_runtime").strip())
            self.assertEqual(before, ops.docker("exec", source_name, "psql", "-X", "-At", "-U", "synthetic", "-d", "synthetic", "-c", fingerprint_sql))
            replay = subprocess.run(command, input=ops.canonical(envelope) + blob, capture_output=True, timeout=30)
            self.assertNotEqual(0, replay.returncode)
            value = dict(value, run_id="123458", nonce="f" * 32, expires=int(time.time()) + 300)
            resumed = subprocess.run(command, input=ops.canonical(self.signed(value)) + blob, capture_output=True, timeout=60)
            self.assertEqual(0, resumed.returncode, "Safe resume failed")
            for index, revoke, repair in (
                (0, "REVOKE USAGE ON SEQUENCE public.synthetic_sequence FROM hfpos_recovery_runtime;",
                 "GRANT USAGE ON SEQUENCE public.synthetic_sequence TO hfpos_recovery_runtime;"),
                (1, "ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE SELECT ON SEQUENCES FROM hfpos_recovery_runtime;",
                 "ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON SEQUENCES TO hfpos_recovery_runtime;")):
                sql_destination(revoke)
                changed = dict(value, run_id=str(123460 + index), nonce=str(index + 2) * 32)
                denied = subprocess.run(command, input=ops.canonical(self.signed(changed)) + blob, capture_output=True, timeout=60)
                self.assertNotEqual(0, denied.returncode, "Resume accepted corrupted sequence/default privileges")
                sql_destination(repair)
            (workspace / "keyring-restored/key-synthetic.xml").write_bytes(b"corrupted")
            value = dict(value, run_id="123459", nonce="1" * 32)
            corrupted = subprocess.run(command, input=ops.canonical(self.signed(value)) + blob, capture_output=True, timeout=60)
            self.assertNotEqual(0, corrupted.returncode, "Corrupt restored keyring must fail")
        finally:
            for name in created:
                subprocess.run(["/usr/bin/docker", "rm", "-f", name], capture_output=True, timeout=30)
            for path in reversed(fixed_paths):
                if path.is_dir():
                    shutil.rmtree(path)
                elif path.exists():
                    path.unlink()


if __name__ == "__main__":
    unittest.main()
