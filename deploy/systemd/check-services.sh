#!/usr/bin/env bash
set -Eeuo pipefail

SERVICES=(
  rustmgrapi.service
  rustopsagent.service
  opssteambot.service
)

for svc in "${SERVICES[@]}"; do
  echo "== $svc =="
  systemctl is-enabled "$svc" 2>/dev/null || true
  systemctl is-active "$svc" 2>/dev/null || true
  systemctl --no-pager --full status "$svc" || true
  echo
done
