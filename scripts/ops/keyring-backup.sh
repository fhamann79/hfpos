#!/bin/sh
set -eu
umask 077
[ "$#" = 2 ] || { echo 'Usage: keyring-backup.sh SOURCE_DIRECTORY DESTINATION_DIRECTORY' >&2; exit 1; }
[ -d "$1" ] && [ -n "$(ls -A "$1")" ] || { echo 'Keyring source missing or empty.' >&2; exit 1; }
for file in "$1"/* "$1"/.[!.]* "$1"/..?*; do
    [ -e "$file" ] || continue
    [ -f "$file" ] && [ ! -L "$file" ] || { echo 'Keyring must contain regular flat files only.' >&2; exit 1; }
    case "$(basename "$file")" in *[!A-Za-z0-9_.-]*) echo 'Invalid keyring filename.' >&2; exit 1;; esac
done
mkdir -p "$2"
temporary=$(mktemp -d "$2/.keyring-XXXXXX")
trap 'rm -rf "$temporary"' EXIT HUP INT TERM
name="keyring-$(date -u +%Y%m%dT%H%M%SZ)-$$.tar"
tar -C "$1" -cf "$temporary/$name" .
hash=$(sha256sum "$temporary/$name" | cut -d ' ' -f 1)
printf '%s  %s\n' "$hash" "$name" > "$temporary/$name.sha256"
[ ! -e "$2/$name" ] || { echo 'Keyring backup exists.' >&2; exit 1; }
mv "$temporary/$name" "$temporary/$name.sha256" "$2/"
echo 'KEYRING BACKUP PASS'
