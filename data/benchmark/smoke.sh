#!/usr/bin/env bash
# Property Intelligence — API Benchmark Script (T082)
# Usage: ./smoke.sh [endpoint-url] [api-key] [payload.json]
# Requires: `oha` (https://github.com/hatoo/oha)

set -euo pipefail

echo "== Property Intelligence API Benchmark (oha) =="

ENDPOINT="${1:-http://localhost:9000/v1/property/analyze}"
# Sempre utilize variável de ambiente para segurança (recomendado)
API_KEY="${API_KEY:-}"
PAYLOAD="${3:-payload.json}"
REQS=${REQS:-20}
CONC=${CONC:-4}

if ! command -v oha >/dev/null; then
  echo "\nERROR: 'oha' is not installed. See: https://github.com/hatoo/oha#installation" >&2
  exit 1
fi

if [ -z "$API_KEY" ]; then
  echo "\nUsage: $0 [endpoint-url] [api-key] [payload.json]"
  echo "You can also set export API_KEY=..."
  exit 2
fi

if [ ! -f "$PAYLOAD" ]; then
  cat > "$PAYLOAD" <<EOF
{ "address": "Rua Augusta, 1500, São Paulo" }
EOF
  echo "No payload.json found. Generated default: $PAYLOAD"
fi

echo "--- Benchmark: $REQS requests, $CONC concurrency ---"
echo "Endpoint: $ENDPOINT"
echo "API_KEY: ${API_KEY:0:4}..."
echo "Payload: $PAYLOAD"
echo

echo "== 1. COLD RUN (non-cached) =="
oha -n "$REQS" -c "$CONC" -H "X-Api-Key: $API_KEY" --body "$PAYLOAD" "$ENDPOINT" | tee oha_cold.txt

sleep 1
echo

echo "== 2. HOT RUN (cache warm) =="
oha -n "$REQS" -c "$CONC" -H "X-Api-Key: $API_KEY" --body "$PAYLOAD" "$ENDPOINT" | tee oha_hot.txt

echo

echo "== Results Summary =="
awk '/Latency distribution:|Status code distribution:/,0' oha_cold.txt
awk '/Latency distribution:|Status code distribution:/,0' oha_hot.txt

exit 0
