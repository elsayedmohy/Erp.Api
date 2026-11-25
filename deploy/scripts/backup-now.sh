#!/usr/bin/env bash
# A backup right now, e.g. before a deploy. Same format as the daily one.
set -euo pipefail
cd "$(dirname "$0")/.."
file="erp-$(date -u +%Y%m%dT%H%M%SZ)-manual.dump"
docker compose -f compose.prod.yaml --env-file .env.prod exec -T backup \
  bash -c "pg_dump --format=custom --file=/backups/$file.part && mv /backups/$file.part /backups/$file"
echo "backups/$file"
