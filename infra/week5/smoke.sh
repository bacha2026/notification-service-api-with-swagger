#!/usr/bin/env bash
set -Eeuo pipefail

api_base=${API_BASE_URL:-http://127.0.0.1:8080}
web_base=${WEB_BASE_URL:-http://127.0.0.1:3000}

require_ok() {
  local url=$1
  curl --fail --silent --show-error --max-time 10 "$url" >/dev/null
}

require_ok "$api_base/health/live"
require_ok "$api_base/health/ready"
require_ok "$api_base/health/version"
require_ok "$api_base/swagger/v2/swagger.json"
require_ok "$web_base/health/version"
for mode in ssg ssr isr csr; do require_ok "$web_base/rendering/$mode"; done

idempotency_key="week5-$(date -u +%Y%m%dT%H%M%S)-$RANDOM-$RANDOM"
response=$(curl --fail --silent --show-error --max-time 15 \
  -X POST "$api_base/api/v2/notifications/bulk" \
  -H 'Content-Type: application/json' \
  -H "X-Idempotency-Key: $idempotency_key" \
  --data '{"notifications":[{"recipientEmail":"week5-smoke@example.test","channel":1,"subject":"Week 5 smoke","body":"Outbox-to-worker health verification.","orderId":null}]}')
job_id=$(sed -n 's/.*"jobId":"\([^"]*\)".*/\1/p' <<<"$response")
[[ -n "$job_id" ]] || { echo "Bulk smoke response did not contain a jobId." >&2; exit 1; }

status=''
for _ in $(seq 1 30); do
  status_response=$(curl --fail --silent --show-error --max-time 10 \
    "$api_base/api/v2/notifications/bulk/$job_id")
  status=$(sed -n 's/.*"status":"\([^"]*\)".*/\1/p' <<<"$status_response")
  [[ "$status" == Completed || "$status" == CompletedWithErrors ]] && break
  sleep 2
done

[[ "$status" == Completed ]] || {
  echo "Bulk smoke job $job_id did not complete successfully; final status: ${status:-unknown}." >&2
  exit 1
}

echo "Week 5 smoke passed: API, frontend rendering modes, Outbox relay, RabbitMQ, and worker completion."
