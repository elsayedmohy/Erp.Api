#!/usr/bin/env bash
# After every deploy: is it up, behind TLS, with the headers, and can the admin log in?
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; source .env.prod; set +a
base="https://${PUBLIC_HOST:-erp.local}"
curl_() { curl -sS --cacert certs/erp.crt --resolve "${PUBLIC_HOST:-erp.local}:443:127.0.0.1" "$@"; }
fail() { echo "FAIL: $*"; exit 1; }

code=$(curl -s -o /dev/null -w '%{http_code}' -H "Host: ${PUBLIC_HOST:-erp.local}" http://127.0.0.1/api/orders)
[ "$code" = "301" ] || fail "http should redirect to https, got $code"

[ "$(curl_ -s -o /dev/null -w '%{http_code}' "$base/health/ready")" = "200" ] || fail "not ready"

headers=$(curl_ -sI "$base/health/live")
grep -qi '^x-content-type-options: nosniff' <<<"$headers" || fail "security headers missing"
grep -qi '^server: kestrel' <<<"$headers" && fail "Kestrel server header leaks"

# Exposed means the spec is served (200). Without a token an unknown path is 401, not 404 (fallback policy).
for path in /swagger/index.html /swagger/v1/swagger.json; do
  code=$(curl_ -s -o /dev/null -w '%{http_code}' "$base$path")
  [ "$code" != "200" ] || fail "swagger is exposed ($path returned 200)"
done

token=$(curl_ -s -X POST "$base/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}" | jq -r .accessToken)
[ -n "$token" ] && [ "$token" != "null" ] || fail "admin login failed"
[ "$(curl_ -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $token" "$base/api/dashboard")" = "200" ] \
  || fail "dashboard failed"

echo "Smoke test passed: $base"