#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
node_b_stopped=false
deadline=$((SECONDS + 90))

cleanup() {
    if [[ "$node_b_stopped" == "true" ]]; then
        docker compose start node-b >/dev/null
    fi
}
trap cleanup EXIT

check_deadline() {
    if (( SECONDS >= deadline )); then
        echo "FAILURE: global heartbeat demo timeout exceeded." >&2
        exit 1
    fi
}

wait_ready() {
    local url="$1"
    while (( SECONDS < deadline )); do
        if curl --fail --silent --max-time 3 "$url" |
            grep --quiet '"status":"ready"'; then
            return 0
        fi
        sleep 0.5
    done
    echo "FAILURE: timed out waiting for $url" >&2
    return 1
}

node_a_status() {
    check_deadline
    curl --fail --silent --max-time 5 http://localhost:5101/mesh/status
}

peer_object() {
    local payload="$1"
    local node_id="$2"
    printf '%s' "$payload" |
        grep -o "{\"nodeId\":\"${node_id}\"[^}]*}" |
        head -n 1
}

wait_peer_status() {
    local node_id="$1"
    local expected="$2"
    while (( SECONDS < deadline )); do
        local payload peer
        payload="$(node_a_status)"
        peer="$(peer_object "$payload" "$node_id")"
        if printf '%s' "$peer" | grep --quiet "\"status\":\"${expected}\""; then
            printf '%s' "$peer"
            return 0
        fi
        sleep 0.5
    done
    echo "FAILURE: timed out waiting for $node_id to become $expected" >&2
    return 1
}

wait_ready "http://localhost:5101/health/ready"
wait_ready "http://localhost:5102/health/ready"
wait_ready "http://localhost:5103/health/ready"

initial_node_b="$(wait_peer_status node-b Alive)"
wait_peer_status node-c Alive >/dev/null
old_instance_id="$(printf '%s' "$initial_node_b" |
    sed -n 's/.*"instanceId":"\([^"]*\)".*/\1/p')"
old_restart_count="$(printf '%s' "$initial_node_b" |
    sed -n 's/.*"restartCount":\([0-9]*\).*/\1/p')"

docker compose stop node-b >/dev/null
node_b_stopped=true
failure_started=$SECONDS

wait_peer_status node-b Suspected >/dev/null
suspected_after=$((SECONDS - failure_started))
wait_peer_status node-b Unreachable >/dev/null
unreachable_after=$((SECONDS - failure_started))

during_failure="$(node_a_status)"
node_c="$(peer_object "$during_failure" node-c)"
printf '%s' "$node_c" | grep --quiet '"status":"Alive"'
wait_ready "http://localhost:5101/health/ready"
echo "[node-a] Peer node-c remains Alive; local readiness remains ready."

docker compose start node-b >/dev/null
wait_ready "http://localhost:5102/health/ready"
recovered="$(wait_peer_status node-b Alive)"
new_instance_id="$(printf '%s' "$recovered" |
    sed -n 's/.*"instanceId":"\([^"]*\)".*/\1/p')"
new_restart_count="$(printf '%s' "$recovered" |
    sed -n 's/.*"restartCount":\([0-9]*\).*/\1/p')"

if [[ -z "$new_instance_id" || "$new_instance_id" == "$old_instance_id" ]]; then
    echo "FAILURE: node-b InstanceId did not change." >&2
    exit 1
fi
if (( new_restart_count <= old_restart_count )); then
    echo "FAILURE: node-b RestartCount did not increment." >&2
    exit 1
fi

node_b_stopped=false
echo "Timeline: Alive -> Suspected (${suspected_after}s) -> Unreachable (${unreachable_after}s) -> Alive"
echo "Instances: $old_instance_id -> $new_instance_id"
echo "RestartCount: $old_restart_count -> $new_restart_count"
echo "SUCCESS: heartbeat failure detection and recovery verified."
