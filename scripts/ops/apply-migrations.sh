#!/bin/sh
set -eu
umask 077
[ "${HFPOS_MIGRATION_APPROVED:-}" = YES ] || { echo 'Migration denied: explicit human approval required.' >&2; exit 1; }
printf 'Migration target environment: %s; release: %s\n' "${ASPNETCORE_ENVIRONMENT:-UNSET}" "${HFPOS_RELEASE_VERSION:-UNSET}"
output=$(mktemp)
working=$(mktemp -d)
trap 'rm -f "$output"; rm -rf "$working"' EXIT HUP INT TERM
# EF CLI writes metadata targets even with --no-build. Run the prebuilt project in tmpfs.
cp -R /app/. "$working/"
cd "$working"
if ! /opt/ef/dotnet-ef migrations list --configuration Release --no-build > "$output" 2>&1; then
    echo 'Migration listing failed; check configuration without exposing secrets.' >&2; exit 1
fi
grep -E '^[0-9]{14}_' "$output" || true
if ! /opt/ef/dotnet-ef database update --configuration Release --no-build > "$output" 2>&1; then
    echo 'Migration failed; human investigation required. No automatic rollback.' >&2; exit 1
fi
echo 'MIGRATION PASS'
