#!/usr/bin/env bash
# Self-signed certificate for local use. A real server gets one from Let's Encrypt instead.
set -euo pipefail
host="${1:-erp.local}"
dir="$(cd "$(dirname "$0")/.." && pwd)/certs"
mkdir -p "$dir"
openssl req -x509 -newkey rsa:2048 -nodes -days 365 \
  -keyout "$dir/erp.key" -out "$dir/erp.crt" \
  -subj "/CN=$host" \
  -addext "subjectAltName=DNS:$host,DNS:localhost,IP:127.0.0.1"
chmod 600 "$dir/erp.key"
echo "Certificate for $host written to $dir"
