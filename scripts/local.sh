#!/bin/bash
set -euo pipefail
if [[ "${1:-}" =~ ^(verify|restore-check)$ && "${2:-}" != /* ]]; then
  set -- "$1" "$PWD/${2:?Specify backup file}"
fi
cd "$(dirname "$0")/.."
export PATH="/usr/local/bin:/opt/homebrew/bin:$PATH"
compose=(docker compose --env-file .env.local)
case "${1:-start}" in
 start) python3 scripts/setup_local.py; "${compose[@]}" up -d --wait ;;
 stop) "${compose[@]}" stop ;;
 status) "${compose[@]}" ps ;;
 demo) python3 scripts/demo.py ;;
 admin) python3 scripts/create_admin.py ;;
 backup|verify|restore-check) python3 scripts/backup.py "$@" ;;
 test) "${compose[@]}" exec -T api dotnet test UniStay.slnx ;;
 *) echo 'Usage: local.sh start|stop|status|admin|test'; exit 2 ;;
esac
