#!/bin/sh
set -eu
[ "$#" = 0 ] || exit 64
umask 077
exec /usr/bin/flock -n /var/lib/hfpos-supervised/process.lock /usr/bin/env -i PATH=/usr/bin:/bin /usr/bin/timeout -k 5 900 /usr/bin/python3 -I /opt/hfpos-supervised/bundle/supervised-operations.py serve
