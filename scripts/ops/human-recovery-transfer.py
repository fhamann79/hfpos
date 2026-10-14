#!/usr/bin/env python3
"""Unprivileged human SCP spool only. Never sudo, Docker, import or restore."""
import argparse
import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import sys


def require(value):
    if not value:
        raise ValueError("STOP")


def private(path):
    uid = os.geteuid()
    require(uid != 0)
    for item in (path, *path.parents):
        info = item.lstat()
        require(not stat.S_ISLNK(info.st_mode) and not info.st_mode & 0o022 and info.st_uid in (0, uid))
    require(path.stat().st_uid == uid and stat.S_IMODE(path.stat().st_mode) == 0o700)


def main(argv=None):
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("prepare", "check"))
    parser.add_argument("--nonce", required=True)
    parser.add_argument("--token", required=True)
    parser.add_argument("--payload-sha256")
    args = parser.parse_args(argv)
    try:
        require(re.fullmatch(r"[a-f0-9]{32}", args.nonce))
        require(len(args.token) <= 4096 and re.fullmatch(r"[A-Za-z0-9+/=]+", args.token))
        token = base64.b64decode(args.token, validate=True)
        require(len(token) <= 4096 and isinstance(json.loads(token), dict))
        require(os.geteuid() != 0)
        here = Path(__file__).resolve().parent
        spec = importlib.util.spec_from_file_location("human_code", here / "human-recovery.py")
        code = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(code)
        code.validate_envelope(code.decode(token))
        require(code.decode((here / "source.json").read_bytes()) == {"source_sha": json.loads(token)["source_sha"],
                                                                  "code_sha256": code.digest_code()})
        path = Path.home() / ("human-recovery-" + args.nonce)
        os.umask(0o077)
        if args.action == "prepare":
            # Validate ancestors before creating even a non-secret spool.
            for ancestor in (path.parent, *path.parent.parents):
                info = ancestor.lstat()
                require(not stat.S_ISLNK(info.st_mode) and not info.st_mode & 0o022 and info.st_uid in (0, os.geteuid()))
            require(os.geteuid() != 0 and not path.exists())
            path.mkdir(mode=0o700)
            private(path)
            with (path / "preflight.json").open("xb") as file:
                file.write(token)
            print("SPOOL_PREPARED")
        else:
            private(path)
            require({f.name for f in path.iterdir()} == {"preflight.json", "payload.tar", "receipt.json"})
            for file in path.iterdir():
                info = file.lstat()
                require(stat.S_ISREG(info.st_mode) and info.st_uid == os.geteuid() and stat.S_IMODE(info.st_mode) == 0o600)
                require(info.st_size <= 128 * 1024 * 1024)
            require((path / "preflight.json").read_bytes() == token)
            digest = hashlib.sha256((path / "payload.tar").read_bytes()).hexdigest()
            receipt = json.loads((path / "receipt.json").read_bytes())
            require(digest == args.payload_sha256 and receipt == {"payload_sha256": digest,
                    "envelope_sha256": hashlib.sha256(token).hexdigest()})
            print("TRANSFER_VERIFIED")
        return 0
    except (Exception, KeyboardInterrupt):
        print("TRANSFER_STOP_NO_RETRY")
        return 1


if __name__ == "__main__":
    sys.exit(main())
