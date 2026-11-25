#!/usr/bin/env bash
# Proves a backup can be restored: loads it into a throwaway Postgres (not the live one) and counts rows.
# A backup nobody has restored is a hope, not a backup.
set -euo pipefail
dump="${1:?usage: restore-check.sh backups/erp-....dump}"
name="erp-restore-check-$$"
trap 'docker rm -f "$name" >/dev/null 2>&1 || true' EXIT

docker run -d --name "$name" -e POSTGRES_PASSWORD=check -e POSTGRES_DB=erp_db postgres:17 >/dev/null
until docker exec "$name" pg_isready -U postgres -d erp_db >/dev/null 2>&1; do sleep 1; done

docker cp "$dump" "$name:/tmp/erp.dump"
docker exec "$name" pg_restore --no-owner --no-acl -U postgres -d erp_db /tmp/erp.dump

docker exec "$name" psql -U postgres -d erp_db -At -c "
  SELECT 'migrations ' || count(*) FROM \"__EFMigrationsHistory\"
  UNION ALL SELECT 'users      ' || count(*) FROM \"Users\"
  UNION ALL SELECT 'showrooms  ' || count(*) FROM \"Showrooms\"
  UNION ALL SELECT 'orders     ' || count(*) FROM \"Orders\"
  UNION ALL SELECT 'invoices   ' || count(*) FROM \"Invoices\";"
echo "Restore OK: $dump"
