#!/bin/sh
set -eu
umask 077
[ "${HFPOS_RESTORE_APPROVED:-}" = YES ] || { echo 'Keyring restore denied: approval required.' >&2; exit 1; }
[ "$#" = 2 ] || { echo 'Usage: keyring-restore.sh ARCHIVE DESTINATION_DIRECTORY' >&2; exit 1; }
[ -s "$1" ] && [ -f "$1.sha256" ] || { echo 'Keyring archive missing.' >&2; exit 1; }
hash=$(sha256sum "$1" | cut -d ' ' -f 1)
[ "$(cat "$1.sha256")" = "$hash  $(basename "$1")" ] || { echo 'Keyring checksum failed.' >&2; exit 1; }
mkdir -p "$2"
[ -z "$(ls -A "$2")" ] || { echo 'Keyring target must be empty.' >&2; exit 1; }
# Reject symlinks, special files, nested paths and path traversal before extraction.
tar -tf "$1" | grep -Ev '^\./([A-Za-z0-9_.-]+)?$' | grep -q . && { echo 'Unsafe keyring archive path.' >&2; exit 1; }
tar -tvf "$1" | grep -Ev '^[-d]' | grep -q . && { echo 'Unsafe keyring archive member.' >&2; exit 1; }
tar -tf "$1" | grep -E '^\./\.\.?$' | grep -q . && { echo 'Unsafe keyring archive path.' >&2; exit 1; }
tar -xf "$1" -C "$2" --no-same-owner
echo 'KEYRING RESTORE PASS'
