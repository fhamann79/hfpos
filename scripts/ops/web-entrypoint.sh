#!/bin/sh
set -eu
case "${HFPOS_HTTPS_PORT:-443}" in ''|*[!0-9]*) echo 'Invalid HTTPS port' >&2; exit 1;; esac
envsubst '${HFPOS_HTTPS_PORT}' < /etc/nginx/default.conf.template > /tmp/default.conf
exec nginx -g 'daemon off;'
