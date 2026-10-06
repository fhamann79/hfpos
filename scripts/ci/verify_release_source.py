#!/usr/bin/env python3
"""Fail closed for stable/RC tags not bound to a commit already on origin/main."""

import re
import subprocess
import sys


NUMBER = r"(?:0|[1-9][0-9]*)"
RELEASE = re.compile(rf"v{NUMBER}\.{NUMBER}\.{NUMBER}(?:-rc\.[1-9][0-9]*)?")


def git(*arguments):
    return subprocess.run(["git", *arguments], check=True, capture_output=True,
                          text=True).stdout.strip()


def main(arguments):
    if len(arguments) != 2 or not RELEASE.fullmatch(arguments[0]):
        print("Release refused: require vX.Y.Z or vX.Y.Z-rc.N without leading zeros; N >= 1.", file=sys.stderr)
        return 1
    release, commit = arguments
    if not re.fullmatch(r"[0-9a-fA-F]{40}", commit):
        print("Release refused: exact commit SHA required.", file=sys.stderr)
        return 1
    try:
        if git("rev-parse", "--verify", f"refs/tags/{release}^{{commit}}") != commit.lower():
            raise ValueError("tag mismatch")
        git("fetch", "--no-tags", "origin", "+refs/heads/main:refs/remotes/origin/main")
        git("merge-base", "--is-ancestor", commit, "refs/remotes/origin/main")
    except (OSError, subprocess.CalledProcessError, ValueError):
        print("Release refused: tag/commit/main verification failed.", file=sys.stderr)
        return 1
    print(f"RELEASE SOURCE PASS version={release} commit={commit.lower()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
