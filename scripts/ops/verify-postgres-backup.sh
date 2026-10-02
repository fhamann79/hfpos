#!/bin/sh
set -eu
[ "$#" = 1 ] || { echo 'Usage: verify-postgres-backup.sh BACKUP.dump' >&2; exit 1; }
backup=$1
fail() { echo 'Backup verification failed.' >&2; exit 1; }
[ -s "$backup" ] && [ -f "$backup.sha256" ] && [ -f "$backup.manifest" ] || fail
name=$(basename "$backup")
expected=$(sed -n 's/^sha256=//p' "$backup.manifest")
case "$expected" in ''|*[!a-f0-9]*) fail;; esac
[ "${#expected}" = 64 ] || fail
[ "$(cat "$backup.sha256")" = "$expected  $name" ] || fail
[ "$(sha256sum "$backup" | cut -d ' ' -f 1)" = "$expected" ] || fail
grep -qx 'format_version=1' "$backup.manifest" || fail
grep -qx 'postgres_client_version=16' "$backup.manifest" || fail
grep -Eq '^created_utc=[0-9]{8}T[0-9]{6}Z$' "$backup.manifest" || fail
grep -Eq '^app_release=[A-Za-z0-9_.+-]+$' "$backup.manifest" || fail
grep -Eq '^database_name=[A-Za-z0-9_-]+$' "$backup.manifest" || fail
[ "$(wc -l < "$backup.manifest" | tr -d ' ')" = 6 ] || fail
pg_restore --list "$backup" >/dev/null 2>&1 || fail
echo 'BACKUP VERIFY PASS'
