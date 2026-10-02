#!/bin/sh
set -eu
umask 077
[ "${HFPOS_RESTORE_APPROVED:-}" = YES ] || { echo 'Restore denied: explicit human approval required.' >&2; exit 1; }
[ "$#" = 1 ] || { echo 'Usage: postgres-restore.sh BACKUP.dump' >&2; exit 1; }
sh "$(dirname "$0")/verify-postgres-backup.sh" "$1"
# Reject any user relation outside system schemas, not only ordinary public tables.
count=$(psql -X -At -v ON_ERROR_STOP=1 -c "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema' AND c.relkind IN ('r','p','v','m','S','f');" 2>/dev/null) \
    || { echo 'Cannot validate restore target.' >&2; exit 1; }
[ "$count" = 0 ] || { echo 'Restore denied: target database is not empty.' >&2; exit 1; }
pg_restore --dbname="${PGDATABASE:?PGDATABASE required}" --exit-on-error --single-transaction --no-owner --no-privileges "$1" >/dev/null 2>&1 \
    || { echo 'Restore failed; target investigation required.' >&2; exit 1; }
sh "$(dirname "$0")/verify-restored-db.sh"
echo 'RESTORE PASS'
