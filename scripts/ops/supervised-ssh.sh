#!/bin/sh
set -eu
[ "$#" = 0 ] && [ "${SSH_ORIGINAL_COMMAND:-}" = hfpos-supervised-v1 ] || exit 64
exec /usr/bin/sudo -n -- /usr/local/sbin/hfpos-supervised-root
