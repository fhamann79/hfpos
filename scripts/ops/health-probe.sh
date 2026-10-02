#!/bin/bash
set -eu
exec 3<>/dev/tcp/127.0.0.1/8080
printf 'GET /health/ready HTTP/1.1\r\nHost: %s\r\nConnection: close\r\n\r\n' "${HFPOS_HEALTH_HOST:-localhost}" >&3
IFS= read -r status <&3
[[ "$status" == *' 200 '* ]]
