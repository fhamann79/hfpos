#!/bin/sh
set -eu
umask 077
[ "$#" = 1 ] || { echo 'Usage: postgres-backup.sh DESTINATION_DIRECTORY' >&2; exit 1; }
: "${PGDATABASE:?PGDATABASE required}"
case "$PGDATABASE" in ''|*[!A-Za-z0-9_-]*) echo 'Use a non-sensitive database identifier.' >&2; exit 1;; esac
release=${HFPOS_RELEASE_VERSION:-unknown}
case "$release" in *[!A-Za-z0-9_.+-]*) echo 'Invalid release identifier.' >&2; exit 1;; esac
pg_dump --version | grep -q ' 16\.' || { echo 'PostgreSQL client 16 required.' >&2; exit 1; }
mkdir -p "$1"
destination=$(cd "$1" && pwd)
temporary=$(mktemp -d "$destination/.backup-XXXXXX")
trap 'rm -rf "$temporary"' EXIT HUP INT TERM
timestamp=$(date -u +%Y%m%dT%H%M%SZ)
name="hfpos-$timestamp-$$.dump"
if ! pg_dump --format=custom --no-owner --no-privileges --file="$temporary/$name" 2>/dev/null; then
    echo 'Backup failed; no complete artifact published.' >&2; exit 1
fi
[ -s "$temporary/$name" ] || { echo 'Empty backup rejected.' >&2; exit 1; }
hash=$(sha256sum "$temporary/$name" | cut -d ' ' -f 1)
printf '%s  %s\n' "$hash" "$name" > "$temporary/$name.sha256"
printf 'format_version=1\ncreated_utc=%s\npostgres_client_version=16\napp_release=%s\ndatabase_name=%s\nsha256=%s\n' \
    "$timestamp" "$release" "$PGDATABASE" "$hash" > "$temporary/$name.manifest"
[ ! -e "$destination/$name" ] || { echo 'Backup already exists.' >&2; exit 1; }
mv "$temporary/$name" "$temporary/$name.sha256" "$temporary/$name.manifest" "$destination/"
echo 'BACKUP CREATED'
