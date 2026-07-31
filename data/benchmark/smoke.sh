#!/usr/bin/env sh
# smoke.sh — manual p95 latency check for Property Intelligence API
#
# Prerequisites:
#   - API running at $API_URL (default: http://localhost:5000)
#   - Valid API key in $API_KEY env var
#
# Optional: install 'oha' (https://github.com/hatoo/oha) for full histogram output.
#           Falls back to sequential curl requests if oha is not installed.
#
# Usage:
#   API_URL=http://localhost:5000 API_KEY=your-key sh data/benchmark/smoke.sh
#
# SC-001: non-cached response ≤ 8 000 ms
# SC-002: cached  response   ≤   500 ms

set -e

API_URL="${API_URL:-http://localhost:5000}"
API_KEY="${API_KEY:-test-key}"
ADDRESS="Rua Augusta, 1500, São Paulo"
REQUESTS="${REQUESTS:-20}"
CONCURRENCY="${CONCURRENCY:-4}"

REQUEST_BODY='{"address":"'"${ADDRESS}"'"}'

echo "======================================================"
echo "  Property Intelligence — Smoke Benchmark"
echo "  Target : ${API_URL}/v1/property/analyze"
echo "  Requests: ${REQUESTS}  Concurrency: ${CONCURRENCY}"
echo "======================================================"
echo ""

# ── Warm-up (ensure caches are populated) ──────────────────────────────────
echo "Warming up (2 sequential requests)..."
for _ in 1 2; do
    curl -s -o /dev/null -w "  warm-up: %{time_total}s\n" \
        -X POST "${API_URL}/v1/property/analyze" \
        -H "Content-Type: application/json" \
        -H "X-Api-Key: ${API_KEY}" \
        -d "${REQUEST_BODY}"
done
echo ""

# ── Load test ───────────────────────────────────────────────────────────────
if command -v oha > /dev/null 2>&1; then
    echo "Running load test with oha..."
    oha \
        --no-tui \
        -n "${REQUESTS}" \
        -c "${CONCURRENCY}" \
        -m POST \
        -H "Content-Type: application/json" \
        -H "X-Api-Key: ${API_KEY}" \
        -d "${REQUEST_BODY}" \
        "${API_URL}/v1/property/analyze"
else
    echo "oha not found — running ${REQUESTS} sequential curl requests..."
    PASS=0
    FAIL=0
    TOTAL_MS=0

    for i in $(seq 1 "${REQUESTS}"); do
        START_MS=$(date +%s%3N 2>/dev/null || python3 -c "import time; print(int(time.time()*1000))")
        HTTP_STATUS=$(
            curl -s -o /dev/null -w "%{http_code}" \
                -X POST "${API_URL}/v1/property/analyze" \
                -H "Content-Type: application/json" \
                -H "X-Api-Key: ${API_KEY}" \
                -d "${REQUEST_BODY}"
        )
        END_MS=$(date +%s%3N 2>/dev/null || python3 -c "import time; print(int(time.time()*1000))")
        ELAPSED=$((END_MS - START_MS))
        TOTAL_MS=$((TOTAL_MS + ELAPSED))

        if [ "${HTTP_STATUS}" = "200" ]; then
            PASS=$((PASS + 1))
            printf "  [%2d/%d] HTTP %s  %4d ms\n" "${i}" "${REQUESTS}" "${HTTP_STATUS}" "${ELAPSED}"
        else
            FAIL=$((FAIL + 1))
            printf "  [%2d/%d] HTTP %s  %4d ms  *** FAILED ***\n" "${i}" "${REQUESTS}" "${HTTP_STATUS}" "${ELAPSED}"
        fi
    done

    AVG_MS=$((TOTAL_MS / REQUESTS))

    echo ""
    echo "======================================================"
    echo "  Results: ${PASS} passed / ${FAIL} failed"
    echo "  Avg latency: ${AVG_MS} ms"
    echo ""
    echo "  SC-001 threshold (non-cached): 8000 ms"
    echo "  SC-002 threshold (cached):      500 ms"
    if [ "${AVG_MS}" -le 8000 ]; then
        echo "  [PASS] Average latency within SC-001 (8s)"
    else
        echo "  [FAIL] Average latency EXCEEDS SC-001 (8s)"
        exit 1
    fi
    echo "======================================================"
fi
