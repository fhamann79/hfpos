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

spec = importlib.util.spec_from_file_location("supervised_ops", Path(__file__).with_name("supervised-operations.py"))
ops = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ops)
MAX_TOTAL = 128 * 1024 * 1024
MAX_FILE = 64 * 1024 * 1024
MAX_MEMBERS = 128


@contextmanager
def private_temporary(parent):
    # Python 3.13 mkdir(0700) on Windows installs an explicit Administrators ACE;
    # inherit the already verified human-only parent's ACL instead.
    folder = Path(parent) / ("supervised-" + secrets.token_hex(16))
    folder.mkdir(mode=0o755 if os.name == "nt" else 0o700)
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
        ops.require(re.fullmatch(r"(?:db|keyring|metadata|snapshot)(?:/[A-Za-z0-9_-][A-Za-z0-9_.-]{0,119})?", name))
        ops.require(member.isdir() == ("/" not in name))
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
    ops.require(files > 0)


def parse_hashes(data):
    ops.require(len(data) <= 65536)
    result = {}
    for line in data.decode("ascii").splitlines():
        match = re.fullmatch(r"([a-f0-9]{64})  (?:\./)?((?:db|keyring|metadata|snapshot)/[A-Za-z0-9_-][A-Za-z0-9_.-]{0,119})", line)
        ops.require(match and match[2] not in result)
        result[match[2]] = match[1]
    ops.require(result)
    return result


def filtered_export(authenticated_tar, output, recovery_id, ciphertext_sha256):
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
            keep = not name.startswith("snapshot/")
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
            if name == "metadata/internal-sha256.txt":
                hash_manifest = selected[name]
    ops.require(hash_manifest is not None)
    expected = parse_hashes(hash_manifest)
    # Exact coverage, not picking the first dump or trusting an arbitrary basename.
    ops.require(set(expected) == set(all_hashes) - {"metadata/internal-sha256.txt"})
    ops.require(all(all_hashes[name] == value for name, value in expected.items()))
    dumps = [name for name in selected if name.startswith("db/") and name.endswith(".dump")]
    keyrings = [name for name in selected if name.startswith("keyring/") and name.endswith(".tar")]
    ops.require(len(dumps) == 1 and len(keyrings) == 1)
    dump, keyring = dumps[0], keyrings[0]
    ops.require(re.fullmatch(r"db/hfpos-[0-9]{8}T[0-9]{6}Z-[0-9]+\.dump", dump))
    ops.require(re.fullmatch(r"keyring/keyring-[0-9]{8}T[0-9]{6}Z-[0-9]+\.tar", keyring))
    ops.require(set(name for name in selected if name.startswith("db/")) == {dump, dump + ".sha256", dump + ".manifest"})
    ops.require(set(name for name in selected if name.startswith("keyring/")) == {keyring, keyring + ".sha256"})
    for name in (dump, keyring):
        ops.require(selected[name + ".sha256"].decode("ascii").strip() ==
                    all_hashes[name] + "  " + Path(name).name)
    validate_keyring(selected[keyring])
    output.mkdir(mode=0o755 if os.name == "nt" else 0o700)
    if os.name == "nt":
        private_windows(output)
    for name, content in selected.items():
        destination = output / name
        destination.parent.mkdir(mode=0o755 if os.name == "nt" else 0o700, exist_ok=True)
        with destination.open("xb") as file:
            file.write(content)
        destination.chmod(0o600)
    manifest = {"version": 1, "recovery_id": recovery_id, "ciphertext_sha256": ciphertext_sha256,
                "files": {name: all_hashes[name] for name in selected}, "dump": dump, "keyring": keyring}
    (output / "transfer.json").write_bytes(ops.canonical(manifest))
    (output / "transfer.json").chmod(0o600)
    return manifest


def decrypt_export(ciphertext, identity, private_staging, output, recovery_id, expected_hash, age="age"):
    ops.require(ops.hex_value(expected_hash, 64))
    # Complete authentication before any tar parsing/export/transfer. Partial plaintext
    # from a late age failure is kept private and never treated as verified.
    with private_temporary(private_staging) as staging:
        snapshot = Path(staging) / "ciphertext.age"
        digest = hashlib.sha256()
        total = 0
        with ciphertext.open("rb") as original, snapshot.open("xb") as copied:
            snapshot.chmod(0o600)
            for chunk in iter(lambda: original.read(65536), b""):
                total += len(chunk)
                ops.require(total <= MAX_TOTAL)
                digest.update(chunk)
                copied.write(chunk)
        ops.require(digest.hexdigest() == expected_hash)
        authenticated = Path(staging) / "authenticated.tar"
        process = subprocess.Popen([age, "--decrypt", "--identity", str(identity), str(snapshot)],
                                   stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        try:
            with authenticated.open("xb") as file:
                authenticated.chmod(0o600)
                total = 0
                while True:
                    chunk = process.stdout.read(65536)
                    if not chunk:
                        break
                    total += len(chunk)
                    ops.require(total <= MAX_TOTAL)
                    file.write(chunk)
            ops.require(process.wait(timeout=15) == 0)
            return filtered_export(authenticated, output, recovery_id, expected_hash)
        finally:
            if process.poll() is None:
                process.kill()
            process.wait()
            process.stdout.close()


def main():
    parser = argparse.ArgumentParser()
    for name in ("ciphertext", "identity", "staging", "output", "recovery-id", "ciphertext-sha256"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        for path in (args.identity, args.staging):
            private_windows(path)
        output = Path(os.path.abspath(args.output))
        private_windows(output.parent)
        decrypt_export(Path(args.ciphertext), Path(args.identity), Path(args.staging), output,
                       args.recovery_id, args.ciphertext_sha256)
        print("FILTERED_EXPORT_PASS_LOCAL_ONLY")
        return 0
    except Exception:
        print("FILTERED_EXPORT_DENIED")
        return 1


if __name__ == "__main__":
    sys.exit(main())
