#!/usr/bin/env bash
set -euo pipefail

base_url="${1:-http://localhost:5101}"
curl --fail --silent --show-error "${base_url}/health/ready" >/dev/null

idempotency_key="DEMO-$(date +%s)-$$"
payload='{"amount":150000,"currency":"COP"}'

created_response="$(curl --silent --show-error \
    --write-out $'\n%{http_code}' \
    --request POST "${base_url}/pay" \
    --header "Content-Type: application/json" \
    --header "Idempotency-Key: ${idempotency_key}" \
    --data "${payload}")"
created_status="${created_response##*$'\n'}"
created_body="${created_response%$'\n'*}"
[[ "$created_status" == "202" ]] || {
    echo "Expected 202, received ${created_status}." >&2
    exit 1
}

payment_id="$(printf '%s' "$created_body" |
    sed -n 's/.*"paymentId":"\([^"]*\)".*/\1/p')"
[[ -n "$payment_id" ]] || {
    echo "The creation response did not contain paymentId." >&2
    exit 1
}

replay_response="$(curl --silent --show-error \
    --write-out $'\n%{http_code}' \
    --request POST "${base_url}/pay" \
    --header "Content-Type: application/json" \
    --header "Idempotency-Key: ${idempotency_key}" \
    --data "${payload}")"
replay_status="${replay_response##*$'\n'}"
replay_body="${replay_response%$'\n'*}"
replay_id="$(printf '%s' "$replay_body" |
    sed -n 's/.*"paymentId":"\([^"]*\)".*/\1/p')"
[[ "$replay_status" == "200" && "$replay_id" == "$payment_id" ]] || {
    echo "Replay did not preserve the payment ID." >&2
    exit 1
}

curl --fail --silent --show-error \
    "${base_url}/payments/${payment_id}" >/dev/null

conflict_status="$(curl --silent --show-error \
    --output /dev/null \
    --write-out '%{http_code}' \
    --request POST "${base_url}/pay" \
    --header "Content-Type: application/json" \
    --header "Idempotency-Key: ${idempotency_key}" \
    --data '{"amount":150001,"currency":"COP"}')"
[[ "$conflict_status" == "409" ]] || {
    echo "Expected 409, received ${conflict_status}." >&2
    exit 1
}

echo "SolidarityGrid API demo succeeded."
echo "Node: ${base_url}"
echo "Idempotency-Key: ${idempotency_key}"
echo "PaymentId: ${payment_id}"
echo "Created: 202; Replay: 200; GET: 200; Conflict: 409"
