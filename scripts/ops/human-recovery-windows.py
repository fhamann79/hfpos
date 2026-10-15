#!/usr/bin/env python3
"""Human-only Windows export/SCP. ACL is hygiene, NOT an agent boundary."""
import argparse
import base64
import hashlib
import importlib.util
import io
import os
from pathlib import Path
import re
import secrets
import stat
import subprocess
import sys
import tarfile

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("human_recovery", HERE / "human-recovery.py")
human = importlib.util.module_from_spec(spec)
spec.loader.exec_module(human)


def no_reparse(path):
    for item in (path, *path.parents):
        info = item.lstat()
        human.require(not stat.S_ISLNK(info.st_mode) and not getattr(info, "st_file_attributes", 0) & 1024)


def cleanup(root, journal, archives, validate_journal=True):
    human.require(root.is_absolute() and root.parent == Path(journal["parent"]))
    human.require(root.name == "human-recovery-" + journal["nonce"])
    human.require(root.stat().st_ino == journal["inode"] and root.stat().st_dev == journal["device"])
    if validate_journal and (root / "creation.json").exists():
        human.require(human.decode((root / "creation.json").read_bytes()) == journal)
    # Validate the entire owned tree BEFORE native literal deletion; never follow a junction.
    pending = [root]
    while pending:
        path = pending.pop()
        no_reparse(path)
        if path.is_dir():
            for child in path.iterdir():
                no_reparse(child)
                pending.append(child)
    archives.private_windows(root)
    env = {k: v for k, v in os.environ.items() if k.lower() != "psmodulepath"}
    result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-File",
                             str(HERE / "human-recovery-local.ps1"), "-Action", "Delete", "-LiteralPath", str(root)],
                            capture_output=True, timeout=60, env=env)
    human.require(result.returncode == 0 and not root.exists())


def ssh_options(known_hosts, key):
    # Key is a path handed to native SSH, never read by Python. Existing pin required.
    options = ["-F", "none", "-o", "StrictHostKeyChecking=yes", "-o", "UserKnownHostsFile=" + str(known_hosts),
            "-o", "GlobalKnownHostsFile=none", "-o", "UpdateHostKeys=no", "-o", "IdentityAgent=none",
            "-o", "ForwardAgent=no", "-o", "ClearAllForwardings=yes", "-o", "IdentitiesOnly=yes"]
    if key:
        options += ["-i", str(key)]
    else:
        options += ["-o", "PubkeyAuthentication=no", "-o", "PreferredAuthentications=password,keyboard-interactive"]
    return options


def interactive_transport(command, timeout):
    # Native authentication prompts go to the human console, not captured logs.
    # Remote commands are fixed non-secret spool operations emitting only PASS/STOP.
    result = subprocess.run(command, stdout=subprocess.PIPE, stderr=None, timeout=timeout)
    human.require(result.returncode == 0)
    return result.stdout


def copy_encrypted(source, destination, archives):
    no_reparse(source)
    human.require(source.is_file() and 0 < source.stat().st_size <= human.LIMIT)
    # One source handle: never validate one file then reopen a swapped plaintext identity.
    with source.open("rb") as incoming:
        header = incoming.read(32)
        human.require(header.startswith(b"age-encryption.org/v1\n"))
        archives.private_create(destination)
        with destination.open("wb") as output:
            output.write(header)
            total = len(header)
            for chunk in iter(lambda: incoming.read(65536), b""):
                total += len(chunk)
                human.require(total <= human.LIMIT)
                output.write(chunk)


def transfer(args, root, payload, token):
    human.require(re.fullmatch(r"[A-Za-z_][A-Za-z0-9_-]{0,31}@[A-Za-z0-9][A-Za-z0-9.-]{0,252}", args.host))
    human.require(re.fullmatch(r"/[A-Za-z0-9_./-]+", args.remote_code) and ".." not in args.remote_code)
    human.require(1 <= args.port <= 65535)
    known = Path(args.known_hosts).absolute()
    no_reparse(known)
    human.require(known.is_file() and known.stat().st_size > 0)
    options = ssh_options(known, Path(args.ssh_key).absolute() if args.ssh_key else None)
    nonce = root.name.removeprefix("human-recovery-")
    command = "python3 -I " + args.remote_code + "/human-recovery-transfer.py "
    common = " --nonce " + nonce + " --token " + token
    def ssh(action):
        return interactive_transport(["ssh", *options, "-p", str(args.port), "-T", args.host, command + action + common], timeout=90)
    human.require(ssh("prepare").strip() == b"SPOOL_PREPARED")
    for filename in ("payload.tar", "receipt.json"):
        interactive_transport(["scp", *options, "-P", str(args.port), str(root / filename),
                      args.host + ":human-recovery-" + nonce + "/" + filename], timeout=120)
    digest = hashlib.sha256(payload).hexdigest()
    human.require(ssh("check --payload-sha256 " + digest).strip() == b"TRANSFER_VERIFIED")
    print("TRANSFER_VERIFIED PAYLOAD_SHA256=" + digest + "; Linux --spool ~/human-recovery-" + nonce)


def run(args, archives):
    human.require(os.name == "nt")
    token = args.token.removeprefix("PREFLIGHT_PASS ")
    envelope = human.validate_envelope(human.decode(base64.b64decode(token, validate=True)))
    parent = Path(args.private_parent).absolute()
    no_reparse(parent)
    archives.private_windows(parent)
    human.require(parent.is_dir() and parent != Path.home())
    # Native NTFS check; no network/removable plaintext staging.
    result = human.native(["powershell.exe", "-NoProfile", "-NonInteractive", "-File",
                           str(HERE / "human-recovery-local.ps1"), "-Action", "Ntfs", "-LiteralPath", str(parent)])
    human.require(result.strip() == b"NTFS")
    human.require(input("Escriba VENTANA HUMANA SIN AGENTES: ") == "VENTANA HUMANA SIN AGENTES")
    root = parent / ("human-recovery-" + secrets.token_hex(16))
    journal = None
    journal_ready = False
    try:
        archives.private_create(root, directory=True)
        info = root.stat()
        journal = {"parent": str(parent), "nonce": root.name.removeprefix("human-recovery-"),
                   "device": info.st_dev, "inode": info.st_ino}
        archives.private_write(root / "creation.json", human.canonical(journal))
        journal_ready = True
        for source, filename in ((args.encrypted_identity, "identity.age"), (args.ciphertext, "ciphertext.age")):
            source = Path(source).absolute()
            copy_encrypted(source, root / filename, archives)
        with (root / "identity.age").open("rb") as copied_identity:
            human.require(copied_identity.read(32).startswith(b"age-encryption.org/v1\n"))
        manifest = archives.decrypt_export(root / "ciphertext.age", root / "identity.age", root,
                    root / "export", envelope["recovery_id"], envelope["ciphertext_sha256"])
        blob = io.BytesIO()
        with tarfile.open(fileobj=blob, mode="w", format=tarfile.USTAR_FORMAT) as archive:
            for name in sorted(set(manifest["files"]) | {"transfer.json"}):
                data = (root / "export" / name).read_bytes()
                member = tarfile.TarInfo(name)
                member.mode, member.size = 0o600, len(data)
                archive.addfile(member, io.BytesIO(data))
        payload = blob.getvalue()
        human.require(len(payload) <= human.LIMIT)
        human.transfer_plan(envelope, payload)
        archives.private_write(root / "payload.tar", payload)
        archives.private_write(root / "receipt.json", human.canonical({"payload_sha256": hashlib.sha256(payload).hexdigest(),
                    "envelope_sha256": hashlib.sha256(human.canonical(envelope)).hexdigest()}))
        transfer(args, root, payload, token)
    finally:
        if journal is not None:
            try:
                cleanup(root, journal, archives, validate_journal=journal_ready)
                print("WINDOWS_OWNED_STAGING_REMOVED")
            except BaseException:
                print("LOCAL_CLEANUP_PENDING " + root.name)
                raise


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument("--cleanup-root")
    for name in ("token", "private-parent", "encrypted-identity", "ciphertext", "host", "known-hosts", "ssh-key", "remote-code"):
        parser.add_argument("--" + name)
    parser.add_argument("--port", type=int, default=22)
    args = parser.parse_args(argv)
    try:
        _, archives = human.load_helpers()
        if args.cleanup_root:
            root = Path(args.cleanup_root).absolute()
            no_reparse(root)
            journal = human.decode((root / "creation.json").read_bytes())
            human.require(input("Escriba RETIRAR SOLO " + root.name + ": ") == "RETIRAR SOLO " + root.name)
            cleanup(root, journal, archives)
            print("WINDOWS_OWNED_STAGING_REMOVED")
        else:
            human.require(all(getattr(args, name.replace("-", "_")) for name in
                              ("token", "private-parent", "encrypted-identity", "ciphertext", "host", "known-hosts", "remote-code")))
            run(args, archives)
        return 0
    except (Exception, KeyboardInterrupt):
        print("STOP_APP_BLOCKED; remote partial preserved; inspect local staging if cleanup did not PASS")
        return 1


if __name__ == "__main__":
    sys.exit(main())
