#!/usr/bin/env bash
set -euo pipefail

receiver_port="${1:-5101}"
timeout_seconds="${2:-90}"
declare -A ports=(["node-a"]=5101 ["node-b"]=5102 ["node-c"]=5103)
node_ids=(node-a node-b node-c)
global_deadline=$((SECONDS + timeout_seconds))
phase_name="initialization"
phase_timeout_seconds="$timeout_seconds"
phase_deadline="$global_deadline"

fail() {
    echo "FAILURE: $1" >&2
    exit 1
}

begin_phase() {
    phase_name="$1"
    phase_timeout_seconds="$2"
    phase_deadline=$((SECONDS + phase_timeout_seconds))
    if (( phase_deadline > global_deadline )); then
        phase_deadline="$global_deadline"
    fi
}

check_deadline() {
    if (( SECONDS >= global_deadline )); then
        fail "global timeout of ${timeout_seconds}s exceeded during phase '${phase_name}'"
    fi

    if (( SECONDS >= phase_deadline )); then
        fail "phase '${phase_name}' timeout of ${phase_timeout_seconds}s exceeded"
    fi
}

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

peer_status() {
    local json="$1"
    local peer="$2"
    local object
    object="$(printf '%s' "$json" |
        grep -o "{\"nodeId\":\"${peer}\"[^}]*}" | head -n 1 || true)"
    json_string "$object" status
}

post_payment() {
    local node="$1"
    local prefix="$2"
    local key="${prefix}-$(date +%s)-${RANDOM}"
    local response_file headers_file
    response_file="$(mktemp)"
    headers_file="$(mktemp)"
    curl --silent --show-error \
        --max-time 3 \
        --dump-header "$headers_file" \
        --output "$response_file" \
        --request POST "http://localhost:${ports[$node]}/pay" \
        --header 'Content-Type: application/json' \
        --header "Idempotency-Key: ${key}" \
        --data '{"amount":777.77,"currency":"COP"}'
    POST_STATUS="$(head -n 1 "$headers_file" | tr -d '\r' | awk '{print $2}')"
    POST_BODY="$(cat "$response_file")"
    rm -f "$response_file" "$headers_file"
}

begin_phase "cluster readiness" 30
all_ready=false
while [[ "$all_ready" != true ]]; do
    check_deadline
    all_ready=true
    for node in "${node_ids[@]}"; do
        ready="$(curl --fail --silent --max-time 3 \
            "http://localhost:${ports[$node]}/health/ready" || true)"
        mesh="$(curl --fail --silent --max-time 3 \
            "http://localhost:${ports[$node]}/mesh/status" || true)"
        if [[ "$ready" != *'"status":"ready"'* ]] ||
            [[ "$(printf '%s' "$mesh" |
                grep -o '"status":"Alive"' | wc -l | tr -d ' ')" != "2" ]]; then
            all_ready=false
            break
        fi
    done
    [[ "$all_ready" == true ]] || sleep 0.25
done

receiver=""
for node in "${node_ids[@]}"; do
    [[ "${ports[$node]}" == "$receiver_port" ]] && receiver="$node"
done
[[ -n "$receiver" ]] || fail "receiver port does not identify a node"

post_payment "$receiver" "FAILOVER-DEMO"
[[ "$POST_STATUS" == "202" ]] ||
    fail "first POST /pay returned ${POST_STATUS}: ${POST_BODY}"
payment_id="$(json_string "$POST_BODY" paymentId)"
[[ -n "$payment_id" ]] || fail "first response has no PaymentId"

begin_phase "initial processing" 20
initial=""
while [[ -z "$initial" ]]; do
    check_deadline
    for node in "${node_ids[@]}"; do
        candidate="$(curl --fail --silent --max-time 3 \
            "http://localhost:${ports[$node]}/payments/${payment_id}" || true)"
        if [[ "$(json_string "$candidate" status)" == "Processing" ]]; then
            initial="$candidate"
            break
        fi
    done
    [[ -n "$initial" ]] || sleep 0.15
done

initial_owner="$(json_string "$initial" ownerNodeId)"
initial_term="$(json_number "$initial" term)"
initial_attempt="$(json_number "$initial" attempt)"
[[ "$initial_attempt" == "1" ]] || fail "initial attempt is not 1"

begin_phase "pre-kill lease verification" 10
sleep 3
check_deadline
latest=""
for node in "${node_ids[@]}"; do
    candidate="$(curl --fail --silent --max-time 3 \
        "http://localhost:${ports[$node]}/payments/${payment_id}" || true)"
    if [[ "$(json_string "$candidate" ownerNodeId)" == "$initial_owner" ]] &&
        [[ "$(json_string "$candidate" status)" == "Processing" ]]; then
        latest="$candidate"
        break
    fi
done
[[ -n "$latest" ]] || fail "payment was not Processing before kill"
lease_before_kill="$(json_string "$latest" leaseExpiresAtUtc)"
lease_epoch_ms="$(date -d "$lease_before_kill" +%s%3N)"

owner_container="$(docker compose ps -q "$initial_owner")"
[[ -n "$owner_container" ]] || fail "could not resolve owner container"
kill_epoch_ms="$(date +%s%3N)"
kill_iso="$(date -u +%Y-%m-%dT%H:%M:%S.%3NZ)"
docker kill "$owner_container" >/dev/null
[[ "$(docker inspect --format '{{.State.Running}}' "$owner_container")" == "false" ]] ||
    fail "owner is still running after docker kill"

survivors=()
for node in "${node_ids[@]}"; do
    [[ "$node" != "$initial_owner" ]] && survivors+=("$node")
done

begin_phase "failure detection" 20
unreachable_epoch_ms=""
while [[ -z "$unreachable_epoch_ms" ]]; do
    check_deadline
    all_unreachable=true
    for survivor in "${survivors[@]}"; do
        ready="$(curl --fail --silent --max-time 3 \
            "http://localhost:${ports[$survivor]}/health/ready" || true)"
        [[ "$ready" == *'"status":"ready"'* ]] ||
            fail "${survivor} is not ready"
        mesh="$(curl --fail --silent --max-time 3 \
            "http://localhost:${ports[$survivor]}/mesh/status" || true)"
        [[ "$(peer_status "$mesh" "$initial_owner")" == "Unreachable" ]] ||
            all_unreachable=false

        payment="$(curl --fail --silent --max-time 3 \
            "http://localhost:${ports[$survivor]}/payments/${payment_id}")"
        current_term="$(json_number "$payment" term)"
        now_ms="$(date +%s%3N)"
        if (( current_term > initial_term && now_ms < lease_epoch_ms )); then
            fail "takeover occurred before lease expiration"
        fi
    done
    if [[ "$all_unreachable" == true ]]; then
        unreachable_epoch_ms="$(date +%s%3N)"
    else
        sleep 0.2
    fi
done

begin_phase "ownership takeover" 25
takeover=""
takeover_epoch_ms=""
while [[ -z "$takeover" ]]; do
    check_deadline
    for survivor in "${survivors[@]}"; do
        candidate="$(curl --fail --silent --max-time 3 \
            "http://localhost:${ports[$survivor]}/payments/${payment_id}")"
        candidate_owner="$(json_string "$candidate" ownerNodeId)"
        candidate_term="$(json_number "$candidate" term)"
        candidate_attempt="$(json_number "$candidate" attempt)"
        candidate_status="$(json_string "$candidate" status)"
        if [[ "$candidate_owner" != "$initial_owner" ]] &&
            (( candidate_term > initial_term )) &&
            [[ "$candidate_attempt" == "2" ]] &&
            [[ "$candidate_status" =~ ^(Processing|Completed)$ ]]; then
            takeover="$candidate"
            takeover_epoch_ms="$(date +%s%3N)"
            break
        fi
    done
    [[ -n "$takeover" ]] || sleep 0.15
done

new_owner="$(json_string "$takeover" ownerNodeId)"
new_term="$(json_number "$takeover" term)"
post_payment "${survivors[0]}" "FAILOVER-QUORUM"
second_status="$POST_STATUS"
second_body="$POST_BODY"
[[ "$second_status" == "202" ]] ||
    fail "second POST /pay returned ${second_status}: ${second_body}"
second_payment_id="$(json_string "$second_body" paymentId)"

begin_phase "payment completion" 25
completed=false
while [[ "$completed" != true ]]; do
    check_deadline
    completed=true
    for survivor in "${survivors[@]}"; do
        payment="$(curl --fail --silent --max-time 3 \
            "http://localhost:${ports[$survivor]}/payments/${payment_id}")"
        if [[ "$(json_string "$payment" status)" != "Completed" ]] ||
            [[ "$(json_string "$payment" ownerNodeId)" != "$new_owner" ]] ||
            [[ "$(json_number "$payment" term)" != "$new_term" ]] ||
            [[ "$(json_number "$payment" attempt)" != "2" ]] ||
            [[ "$payment" != *'"leaseExpiresAtUtc":null'* ]] ||
            [[ "$payment" != *'"completedAtUtc":"'* ]]; then
            completed=false
            break
        fi
    done
    [[ "$completed" == true ]] || sleep 0.2
done

[[ "$(docker inspect --format '{{.State.Running}}' "$owner_container")" == "false" ]] ||
    fail "old owner restarted before verification"
logs="$(docker compose logs --no-color)"
completion_count="$(printf '%s' "$logs" |
    grep -c "Payment ${payment_id} completed successfully" || true)"
[[ "$completion_count" == "1" ]] ||
    fail "expected one PaymentCompleted event, observed ${completion_count}"

unreachable_ms=$((unreachable_epoch_ms - kill_epoch_ms))
lease_ms=$((lease_epoch_ms - kill_epoch_ms))
(( lease_ms < 0 )) && lease_ms=0
takeover_ms=$((takeover_epoch_ms - kill_epoch_ms))

echo "PaymentId: ${payment_id}"
echo "Receiver: ${receiver}"
echo "Initial owner: ${initial_owner}"
echo "Initial term: ${initial_term}"
echo "Initial attempt: ${initial_attempt}"
echo "Kill moment: ${kill_iso}"
echo "Time until Unreachable: $((unreachable_ms / 1000)).$((unreachable_ms % 1000)) seconds"
echo "Time until lease expiration: $((lease_ms / 1000)).$((lease_ms % 1000)) seconds"
echo "Time until takeover: $((takeover_ms / 1000)).$((takeover_ms % 1000)) seconds"
echo "New owner: ${new_owner}"
echo "New term: ${new_term}"
echo "New attempt: 2"
echo "Survivor ${survivors[0]}: Completed"
echo "Survivor ${survivors[1]}: Completed"
echo "PaymentCompleted events: ${completion_count}"
echo "Second payment: HTTP ${second_status} (${second_payment_id})"
echo "Old owner running: false"
echo "SUCCESS"
