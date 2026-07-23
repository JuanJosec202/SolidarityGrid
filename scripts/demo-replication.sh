#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
deadline=$((SECONDS + 120))
node_b_stopped=false
node_c_stopped=false
temporary_directory="$(mktemp -d)"

cleanup() {
    if [[ "$node_b_stopped" == "true" ]]; then
        docker compose start node-b >/dev/null
    fi
    if [[ "$node_c_stopped" == "true" ]]; then
        docker compose start node-c >/dev/null
    fi
    rm -rf -- "$temporary_directory"
}
trap cleanup EXIT

wait_ready() {
    local port="$1"
    while (( SECONDS < deadline )); do
        if curl --fail --silent --max-time 3 \
            "http://localhost:${port}/health/ready" |
            grep --quiet '"status":"ready"'; then
            return 0
        fi
        sleep 0.5
    done
    echo "FAILURE: node on port $port did not become ready." >&2
    return 1
}

send_payment() {
    local key="$1"
    local correlation_id="$2"
    response_status="$(
        curl --silent --show-error \
            --output "$temporary_directory/body.json" \
            --dump-header "$temporary_directory/headers.txt" \
            --write-out '%{http_code}' \
            --max-time 10 \
            --request POST http://localhost:5101/pay \
            --header "Idempotency-Key: $key" \
            --header "X-Correlation-ID: $correlation_id" \
            --header 'Content-Type: application/json' \
            --data '{"amount":125.50,"currency":"COP"}'
    )"
    response_body="$(tr -d '\r\n' < "$temporary_directory/body.json")"
    response_location="$(
        tr -d '\r' < "$temporary_directory/headers.txt" |
            sed -n 's/^[Ll]ocation:[[:space:]]*//p' |
            tail -n 1
    )"
}

json_field() {
    local payload="$1"
    local field="$2"
    printf '%s' "$payload" |
        sed -n "s/.*\"${field}\":\"\\([^\"]*\\)\".*/\\1/p"
}

get_payment() {
    local port="$1"
    local payment_id="$2"
    curl --fail --silent --max-time 5 \
        "http://localhost:${port}/payments/${payment_id}"
}

assert_replicated() {
    local payload="$1"
    local expected_id="$2"
    printf '%s' "$payload" | grep --quiet "\"paymentId\":\"${expected_id}\""
    printf '%s' "$payload" | grep --quiet '"status":"Replicated"'
    printf '%s' "$payload" | grep --quiet '"version":2'
}

wait_ready 5101
wait_ready 5102
wait_ready 5103

first_key="DEMO-REPLICATION-$(date +%s)-$RANDOM"
send_payment "$first_key" "demo-replication-all-bash"
[[ "$response_status" == "202" ]]
first_id="$(json_field "$response_body" paymentId)"
assert_replicated "$(get_payment 5101 "$first_id")" "$first_id"
assert_replicated "$(get_payment 5102 "$first_id")" "$first_id"
assert_replicated "$(get_payment 5103 "$first_id")" "$first_id"
echo "Payment $first_id is durable on node-a, node-b and node-c."

docker compose stop node-c >/dev/null
node_c_stopped=true
second_key="DEMO-ONE-PEER-$(date +%s)-$RANDOM"
send_payment "$second_key" "demo-replication-one-peer-bash"
[[ "$response_status" == "202" ]]
second_id="$(json_field "$response_body" paymentId)"
assert_replicated "$(get_payment 5101 "$second_id")" "$second_id"
assert_replicated "$(get_payment 5102 "$second_id")" "$second_id"
echo "Payment $second_id reached quorum with node-c stopped."

docker compose stop node-b >/dev/null
node_b_stopped=true
third_key="DEMO-NO-QUORUM-$(date +%s)-$RANDOM"
send_payment "$third_key" "demo-replication-no-quorum-bash"
[[ "$response_status" == "503" ]]
printf '%s' "$response_body" |
    grep --quiet '"code":"PAYMENT_REPLICATION_QUORUM_UNAVAILABLE"'
third_id="${response_location##*/}"
received="$(get_payment 5101 "$third_id")"
printf '%s' "$received" | grep --quiet '"status":"Received"'
printf '%s' "$received" | grep --quiet '"version":1'

docker compose start node-b >/dev/null
wait_ready 5102
send_payment "$third_key" "demo-replication-retry-bash"
[[ "$response_status" == "200" || "$response_status" == "202" ]]
retry_id="$(json_field "$response_body" paymentId)"
[[ "$retry_id" == "$third_id" ]]
assert_replicated "$response_body" "$third_id"
assert_replicated "$(get_payment 5102 "$third_id")" "$third_id"
node_b_stopped=false

echo "No quorum: 503 with local Received; retry preserved $third_id and reached Replicated."
echo "SUCCESS: durable payment replication and quorum 2/3 verified."
