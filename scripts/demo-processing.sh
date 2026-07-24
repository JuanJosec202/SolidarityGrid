#!/usr/bin/env bash
set -euo pipefail

receiver_port="${1:-5101}"
timeout_seconds="${2:-30}"
ports=(5101 5102 5103)

json_string() {
    local json="$1"
    local field="$2"
    printf '%s' "$json" |
        sed -n "s/.*\"${field}\":\"\\([^\"]*\\)\".*/\\1/p"
}

json_number() {
    local json="$1"
    local field="$2"
    printf '%s' "$json" |
        sed -n "s/.*\"${field}\":\\([0-9][0-9]*\\).*/\\1/p"
}

deadline=$((SECONDS + timeout_seconds))
while (( SECONDS < deadline )); do
    ready=true
    for port in "${ports[@]}"; do
        if ! curl --fail --silent "http://localhost:${port}/health/ready" |
            grep --quiet '"status":"ready"'; then
            ready=false
            break
        fi
        if [[ "$(curl --fail --silent "http://localhost:${port}/mesh/status" |
            grep -o '"status":"Alive"' | wc -l | tr -d ' ')" != "2" ]]; then
            ready=false
            break
        fi
    done
    if [[ "$ready" == true ]]; then
        break
    fi
    sleep 0.25
done

if [[ "$ready" != true ]]; then
    echo "FAILURE: nodes are not ready with Alive peers." >&2
    exit 1
fi

key="PROCESS-DEMO-$(date +%s)-${RANDOM}"
headers_file="$(mktemp)"
body_file="$(mktemp)"
cleanup() {
    rm -f "$headers_file" "$body_file"
}
trap cleanup EXIT

curl --silent --show-error \
    --dump-header "$headers_file" \
    --output "$body_file" \
    --request POST "http://localhost:${receiver_port}/pay" \
    --header 'Content-Type: application/json' \
    --header "Idempotency-Key: ${key}" \
    --data '{"amount":777.77,"currency":"COP"}'

status_code="$(head -n 1 "$headers_file" | tr -d '\r' | awk '{print $2}')"
if [[ "$status_code" != "202" ]]; then
    echo "FAILURE: POST /pay returned ${status_code}: $(cat "$body_file")" >&2
    exit 1
fi

created="$(cat "$body_file")"
payment_id="$(json_string "$created" "paymentId")"
if [[ -z "$payment_id" ]]; then
    echo "FAILURE: response did not contain PaymentId." >&2
    exit 1
fi

echo "PaymentId: ${payment_id}"
echo "Receiver port: ${receiver_port}"

seen_replicated=false
[[ "$(json_string "$created" "status")" == "Replicated" ]] &&
    seen_replicated=true
seen_processing=false
owner=""
term=""
completed=false
deadline=$((SECONDS + timeout_seconds))

while (( SECONDS < deadline )); do
    all_completed=true
    current_owner=""
    current_term=""
    for port in "${ports[@]}"; do
        payment="$(curl --fail --silent \
            "http://localhost:${port}/payments/${payment_id}")" || {
            all_completed=false
            continue
        }
        status="$(json_string "$payment" "status")"
        candidate_owner="$(json_string "$payment" "ownerNodeId")"
        candidate_term="$(json_number "$payment" "term")"
        [[ "$status" == "Replicated" ]] && seen_replicated=true
        [[ "$status" == "Processing" ]] && seen_processing=true
        [[ "$status" != "Completed" ]] && all_completed=false

        if [[ -n "$candidate_owner" ]]; then
            if [[ -n "$current_owner" && "$current_owner" != "$candidate_owner" ]]; then
                echo "FAILURE: multiple owners observed." >&2
                exit 1
            fi
            current_owner="$candidate_owner"
            current_term="$candidate_term"
        fi

        if [[ "$status" == "Completed" ]]; then
            [[ "$(json_number "$payment" "attempt")" == "1" ]]
            printf '%s' "$payment" | grep --quiet '"completedAtUtc":"'
            printf '%s' "$payment" | grep --quiet '"leaseExpiresAtUtc":null'
        fi
    done

    [[ -n "$current_owner" ]] && owner="$current_owner"
    [[ -n "$current_term" ]] && term="$current_term"
    if [[ "$all_completed" == true ]]; then
        completed=true
        break
    fi
    sleep 0.25
done

if [[ "$completed" != true || "$seen_replicated" != true ||
    "$seen_processing" != true ]]; then
    echo "FAILURE: lifecycle did not converge observably." >&2
    exit 1
fi

logs="$(docker compose logs --no-color)"
renewals="$(printf '%s' "$logs" |
    grep -c "Lease renewed for payment ${payment_id}" || true)"
completions="$(printf '%s' "$logs" |
    grep -c "Payment ${payment_id} completed successfully" || true)"
if (( renewals < 1 )) || [[ "$completions" != "1" ]]; then
    echo "FAILURE: renewals=${renewals}, completions=${completions}." >&2
    exit 1
fi

echo "Owner: ${owner}"
echo "Term: ${term}"
echo "Attempt: 1"
echo "Lease renewals: ${renewals}"
echo "Completed on node-a, node-b and node-c."
echo "Completion logs: ${completions}"
echo "SUCCESS"
