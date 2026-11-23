#!/usr/bin/env bash
# Times GET /api/dashboard against the running API: 2 warm-up calls, then RUNS timed calls.
# Usage (from the repo root, API running on :5013 with demo data seeded):
#   ./measure-dashboard.sh            # 20 runs
#   RUNS=50 ./measure-dashboard.sh
set -euo pipefail

BASE="${BASE:-http://localhost:5013}"
RUNS="${RUNS:-20}"
set -a; source .env; set +a
EMAIL="${ADMIN_EMAIL:-admin@company.com}"

TOKEN=$(curl -s -X POST "$BASE/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r .accessToken)
[ -n "$TOKEN" ] && [ "$TOKEN" != "null" ] || { echo "Login failed"; exit 1; }

call() { curl -s -o /dev/null -w '%{http_code} %{time_total}\n' -H "Authorization: Bearer $TOKEN" "$BASE/api/dashboard"; }

call >/dev/null; call >/dev/null   # warm-up: JIT, connection pool, EF query cache

results=$(for _ in $(seq "$RUNS"); do call; done)
bad=$(echo "$results" | awk '$1 != 200' | wc -l | tr -d ' ')
[ "$bad" = "0" ] || echo "$bad calls did not return 200"

# Works with the awk that ships with macOS (no gawk needed): sort first, then index.
echo "$results" | awk '$1 == 200 { printf "%.0f\n", $2 * 1000 }' | sort -n | awk '
  { ms[++n] = $1 }
  END {
    p95 = int(n * 0.95); if (p95 < 1) p95 = 1
    printf "runs=%d  min=%dms  median=%dms  p95=%dms  max=%dms\n", n, ms[1], ms[int((n + 1) / 2)], ms[p95], ms[n]
  }'
