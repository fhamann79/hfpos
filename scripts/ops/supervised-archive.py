#!/usr/bin/env python3
"""Local human-only age export; authenticated plaintext never leaves private staging."""
import argparse
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
import subprocess
import sys
import tarfile
import tempfile
import threading

spec = importlib.util.spec_from_file_location("supervised_ops", Path(__file__).with_name("supervised-operations.py"))
ops = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ops)
MAX_TOTAL = 128 * 1024 * 1024
MAX_FILE = 64 * 1024 * 1024
MAX_MEMBERS = 128
METADATA = {"metadata/release.txt", "metadata/capture.json", "metadata/roles.sql",
            "metadata/schema-ownership-grants.sql"}
PRIVATE_METADATA = {"metadata/containers.private.json"}
FORMATS = {"internal-sha256.txt", "metadata/internal-sha256.txt", "metadata/format.json"}


@contextmanager
def private_temporary(parent):
    # Explicit owner and ACL avoid Administrators ownership under elevated Python.
    folder = Path(parent) / ("supervised-" + secrets.token_hex(16))
    private_create(folder, directory=True)
    try:
        if os.name == "nt":
            private_windows(folder)
        yield folder
    finally:
        ops.require(folder.parent == Path(parent) and re.fullmatch(r"supervised-[a-f0-9]{32}", folder.name))
        shutil.rmtree(folder)


def private_windows(path):
    ops.require(os.name == "nt")
    ops.native(["powershell.exe", "-NoProfile", "-NonInteractive", "-File",
                str(Path(__file__).with_name("supervised-local-acl.ps1")),
                "-LiteralPath", str(path)], timeout=15)


def private_create(path, directory=False):
    if os.name == "nt":
        ops.native(["powershell.exe", "-NoProfile", "-NonInteractive", "-File",
                    str(Path(__file__).with_name("supervised-local-acl.ps1")), "-LiteralPath", str(path),
                    "-CreateDirectory" if directory else "-CreateFile"], timeout=15)
    elif directory:
        path.mkdir(mode=0o700)
    else:
        with path.open("xb"):
            pass
        path.chmod(0o600)


def private_write(path, content, replace=False):
    if replace and path.exists():
        if os.name == "nt":
            private_windows(path)
    else:
        private_create(path)
    path.write_bytes(content)


def member_name(member, keyring=False):
    ops.require(not member.pax_headers and member.type in (tarfile.REGTYPE, tarfile.DIRTYPE))
    name = member.name
    if name.startswith("./"):
        name = name[2:]
    name = name.rstrip("/") if member.isdir() else name
    ops.require("\\" not in name and len(name) <= 160)
    if member.isdir() and name in ("", "."):
        return "."
    if keyring:
        ops.require((member.isdir() and name in ("", ".")) or
                    re.fullmatch(r"[A-Za-z0-9_-][A-Za-z0-9_.-]{0,119}\.xml", name))
    else:
        ops.require(name == "internal-sha256.txt" or
                    re.fullmatch(r"(?:db|keyring|metadata|snapshot)(?:/[A-Za-z0-9_-][A-Za-z0-9_.-]{0,119})?", name))
        ops.require(member.isdir() == (name in ("db", "keyring", "metadata", "snapshot")))
    ops.require(0 <= member.size <= (1024 * 1024 if keyring else MAX_FILE))
    return name


def validate_keyring(data):
    ops.require(0 < len(data) <= 4 * 1024 * 1024)
    seen = set()
    files = 0
    with tarfile.open(fileobj=io.BytesIO(data), mode="r:") as archive:
        for member in archive:
            name = member_name(member, keyring=True)
            ops.require(name not in seen and len(seen) < MAX_MEMBERS)
            seen.add(name)
            if member.isfile():
                files += 1
        zero_tail(archive)
    ops.require(files > 0)


def zero_tail(archive):
    archive.fileobj.seek(archive.offset)
    for chunk in iter(lambda: archive.fileobj.read(65536), b""):
        ops.require(not chunk.strip(b"\0"))


def parse_hashes(data):
    ops.require(len(data) <= 65536)
    result = {}
    for line in data.decode("ascii").splitlines():
        match = re.fullmatch(r"([a-f0-9]{64})  (?:\./)?((?:db|keyring|metadata|snapshot)/[A-Za-z0-9_-][A-Za-z0-9_.-]{0,119})", line)
        ops.require(match and match[2] not in result)
        result[match[2]] = match[1]
    ops.require(result)
    return result


def validate_config(data):
    seen, total = set(), 0
    with tarfile.open(fileobj=io.BytesIO(data), mode="r:") as archive:
        for member in archive:
            name = member.name
            ops.require(member.type == tarfile.REGTYPE and not member.pax_headers and not member.issparse())
            ops.require(re.fullmatch(r"[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)+", name))
            ops.require(all(part not in (".", "..") for part in name.split("/")))
            ops.require(name not in seen and len(seen) < 4096 and 0 <= member.size <= MAX_FILE)
            seen.add(name)
            total += member.size
            ops.require(total <= MAX_FILE)
        zero_tail(archive)
    ops.require(seen)


def filtered_export(authenticated_tar, output, recovery_id, ciphertext_sha256, offline_config_output=None):
    ops.require(ops.hex_value(recovery_id, 32) and ops.hex_value(ciphertext_sha256, 64))
    ops.require(authenticated_tar.stat().st_size <= MAX_TOTAL)
    ops.require(not output.exists())
    selected = {}
    all_hashes = {}
    seen = set()
    total = 0
    hash_manifest = None
    # Parse and validate all members before publishing ANY transfer material.
    with tarfile.open(authenticated_tar, mode="r:") as archive:
        for member in archive:
            name = member_name(member)
            ops.require(name not in seen and len(seen) < MAX_MEMBERS)
            seen.add(name)
            total += member.size
            ops.require(total <= MAX_TOTAL)
            if member.isdir():
                continue
            stream = archive.extractfile(member)
            digest = hashlib.sha256()
            chunks = []
            keep = name not in PRIVATE_METADATA
            while True:
                chunk = stream.read(65536)
                if not chunk:
                    break
                digest.update(chunk)
                if keep:
                    chunks.append(chunk)
            all_hashes[name] = digest.hexdigest()
            if keep:
                selected[name] = b"".join(chunks)
            if name in ("metadata/internal-sha256.txt", "internal-sha256.txt"):
                ops.require(hash_manifest is None)
                hash_manifest = selected[name]
        zero_tail(archive)
    ops.require(hash_manifest is not None)
    expected = parse_hashes(hash_manifest)
    # Exact coverage, not picking the first dump or trusting an arbitrary basename.
    manifest_name = "internal-sha256.txt" if "internal-sha256.txt" in all_hashes else "metadata/internal-sha256.txt"
    ops.require(set(expected) == set(all_hashes) - {manifest_name})
    ops.require(all(all_hashes[name] == value for name, value in expected.items()))
    version = "legacy"
    if "metadata/format.json" in selected:
        format_value = ops.decode(selected["metadata/format.json"], 1024)
        ops.require(format_value == {"format": "hfpos-auto", "version": 2} and type(format_value["version"]) is int)
        ops.require(manifest_name == "metadata/internal-sha256.txt")
        version = "auto-v2"
    elif manifest_name == "internal-sha256.txt":
        version = "auto-v1"
    if version != "legacy":
        ops.require(set(n for n in all_hashes if n.startswith("metadata/")) <= METADATA | PRIVATE_METADATA | FORMATS)
        ops.require(set(n for n in all_hashes if n.startswith("snapshot/")) == {"snapshot/host-config.tar"})
        ops.require(METADATA - {"metadata/release.txt"} <= set(all_hashes))
        ops.require("metadata/containers.private.json" in all_hashes)
        validate_config(selected["snapshot/host-config.tar"])
    dumps = [name for name in selected if name.startswith("db/") and name.endswith(".dump")]
    keyrings = [name for name in selected if name.startswith("keyring/") and name.endswith(".tar")]
    ops.require(len(dumps) == 1 and len(keyrings) == 1)
    dump, keyring = dumps[0], keyrings[0]
    ops.require(re.fullmatch(r"db/hfpos-[0-9]{8}T[0-9]{6}Z-[0-9]+\.dump", dump))
    ops.require(keyring == "keyring/keyring.tar" if version == "auto-v1" else
                re.fullmatch(r"keyring/keyring-[0-9]{8}T[0-9]{6}Z-[0-9]+\.tar", keyring))
    ops.require(set(name for name in selected if name.startswith("db/")) == {dump, dump + ".sha256", dump + ".manifest"})
    ops.require(set(name for name in selected if name.startswith("keyring/")) == {keyring, keyring + ".sha256"})
    for name in (dump, keyring):
        ops.require(selected[name + ".sha256"].decode("ascii").strip() ==
                    all_hashes[name] + "  " + Path(name).name)
    validate_keyring(selected[keyring])
    if offline_config_output is not None:
        ops.require(version != "legacy" and not offline_config_output.exists() and offline_config_output != output)
        # Separate private offline destination; never part of transfer.json or app environment.
        private_create(offline_config_output, directory=True)
        for name in ("snapshot/host-config.tar", "metadata/roles.sql", "metadata/schema-ownership-grants.sql"):
            private_write(offline_config_output / Path(name).name, selected[name])
        private_write(offline_config_output / "source.json", ops.canonical({"ciphertext_sha256": ciphertext_sha256,
            "recovery_id": recovery_id, "format": version,
            "files": {n: all_hashes[n] for n in all_hashes if n.startswith("snapshot/") or n in METADATA}}))
    selected = {n: v for n, v in selected.items() if not n.startswith("snapshot/") and
                (not n.startswith("metadata/") or n in METADATA | FORMATS)}
    # Normalize only verified bytes; preserve the identity of the original ciphertext.
    if version == "auto-v1":
        canonical_key = "keyring/keyring-" + Path(dump).name[6:-5] + ".tar"
        selected[canonical_key] = selected.pop(keyring)
        selected.pop(keyring + ".sha256")
        selected[canonical_key + ".sha256"] = (all_hashes[keyring] + "  " + Path(canonical_key).name + "\n").encode()
        keyring = canonical_key
    selected.pop(manifest_name)
    selected.pop("metadata/format.json", None)
    # Neither raw inspect Env nor executable roles/grants cross into the DB/app workspace.
    for name in ("metadata/roles.sql", "metadata/schema-ownership-grants.sql"):
        selected.pop(name, None)
    selected["metadata/internal-sha256.txt"] = "".join(
        hashlib.sha256(data).hexdigest() + "  " + name + "\n" for name, data in sorted(selected.items())).encode()
    private_create(output, directory=True)
    for name, content in selected.items():
        destination = output / name
        if not destination.parent.exists():
            private_create(destination.parent, directory=True)
        private_write(destination, content)
    manifest = {"version": 1, "recovery_id": recovery_id, "ciphertext_sha256": ciphertext_sha256,
                "files": {name: hashlib.sha256(data).hexdigest() for name, data in selected.items()}, "dump": dump, "keyring": keyring}
    private_write(output / "transfer.json", ops.canonical(manifest))
    return manifest


def decrypt_export(ciphertext, identity, private_staging, output, recovery_id, expected_hash, age="age", offline_config_output=None):
    ops.require(ops.hex_value(expected_hash, 64))
    if os.name == "nt":
        private_windows(ciphertext)
        private_windows(identity)
    # Complete authentication before any tar parsing/export/transfer. Partial plaintext
    # from a late age failure is kept private and never treated as verified.
    with private_temporary(private_staging) as staging:
        snapshot = Path(staging) / "ciphertext.age"
        digest = hashlib.sha256()
        total = 0
        private_create(snapshot)
        with ciphertext.open("rb") as original, snapshot.open("wb") as copied:
            for chunk in iter(lambda: original.read(65536), b""):
                total += len(chunk)
                ops.require(total <= MAX_TOTAL)
                digest.update(chunk)
                copied.write(chunk)
        ops.require(digest.hexdigest() == expected_hash)
        authenticated = Path(staging) / "authenticated.tar"
        private_create(authenticated)
        process = subprocess.Popen([age, "--decrypt", "--identity", str(identity), str(snapshot)],
                                   stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        # A blocked pipe read must also be bounded, not only wait() after EOF.
        watchdog = threading.Timer(120, process.kill)
        watchdog.start()
        try:
            with authenticated.open("wb") as file:
                total = 0
                while True:
                    chunk = process.stdout.read(65536)
                    if not chunk:
                        break
                    total += len(chunk)
                    ops.require(total <= MAX_TOTAL)
                    file.write(chunk)
            ops.require(process.wait(timeout=15) == 0)
            return filtered_export(authenticated, output, recovery_id, expected_hash, offline_config_output)
        finally:
            watchdog.cancel()
            if process.poll() is None:
                process.kill()
            process.wait()
            process.stdout.close()


def main():
    parser = argparse.ArgumentParser()
    for name in ("ciphertext", "identity", "staging", "output", "recovery-id", "ciphertext-sha256"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--offline-config-output")
    args = parser.parse_args()
    try:
        for path in (args.identity, args.staging):
            private_windows(path)
        output = Path(os.path.abspath(args.output))
        private_windows(output.parent)
        offline = Path(os.path.abspath(args.offline_config_output)) if args.offline_config_output else None
        if offline:
            private_windows(offline.parent)
        decrypt_export(Path(args.ciphertext), Path(args.identity), Path(args.staging), output,
                       args.recovery_id, args.ciphertext_sha256, offline_config_output=offline)
        print("FILTERED_EXPORT_PASS_LOCAL_ONLY")
        return 0
    except Exception:
        print("FILTERED_EXPORT_DENIED")
        return 1


if __name__ == "__main__":
    sys.exit(main())
