#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
node_b_stopped=false

cleanup() {
    if [[ "$node_b_stopped" == "true" ]]; then
        docker compose start node-b >/dev/null
    fi
}
trap cleanup EXIT

wait_ready() {
    local url="$1"
    for _ in $(seq 1 30); do
        if curl --fail --silent --max-time 3 "$url" |
            grep --quiet '"status":"ready"'; then
            return 0
        fi
        sleep 1
    done

    echo "FAILURE: timed out waiting for $url" >&2
    return 1
}

peer_object() {
    local payload="$1"
    local node_id="$2"
    printf '%s' "$payload" |
        grep -o "{\"nodeId\":\"${node_id}\"[^}]*}" |
        head -n 1
}

wait_ready "http://localhost:5101/health/ready"
wait_ready "http://localhost:5102/health/ready"
wait_ready "http://localhost:5103/health/ready"

initial="$(curl --fail --silent --max-time 5 http://localhost:5101/mesh/peers)"
reachable_count="$(printf '%s' "$initial" | grep -o '"isReachable":true' | wc -l | tr -d ' ')"
if [[ "$reachable_count" != "2" ]]; then
    echo "FAILURE: node-a did not report two reachable peers." >&2
    exit 1
fi

initial_node_b="$(peer_object "$initial" "node-b")"
old_instance_id="$(printf '%s' "$initial_node_b" |
    sed -n 's/.*"remoteInstanceId":"\([^"]*\)".*/\1/p')"
if [[ -z "$old_instance_id" ]]; then
    echo "FAILURE: node-b did not return an InstanceId." >&2
    exit 1
fi

docker compose stop node-b >/dev/null
node_b_stopped=true

during_failure="$(curl --fail --silent --max-time 5 http://localhost:5101/mesh/peers)"
failed_node_b="$(peer_object "$during_failure" "node-b")"
available_node_c="$(peer_object "$during_failure" "node-c")"
if ! printf '%s' "$failed_node_b" | grep --quiet '"isReachable":false'; then
    echo "FAILURE: node-b was not reported unreachable." >&2
    exit 1
fi
if ! printf '%s' "$failed_node_b" | grep --quiet '"errorCode":"MESH_'; then
    echo "FAILURE: node-b did not expose a neutral mesh error." >&2
    exit 1
fi
if ! printf '%s' "$available_node_c" | grep --quiet '"isReachable":true'; then
    echo "FAILURE: node-c became unreachable." >&2
    exit 1
fi
wait_ready "http://localhost:5101/health/ready"

docker compose start node-b >/dev/null
wait_ready "http://localhost:5102/health/ready"

recovered_node_b=""
for _ in $(seq 1 20); do
    recovered="$(curl --fail --silent --max-time 5 http://localhost:5101/mesh/peers)"
    recovered_node_b="$(peer_object "$recovered" "node-b")"
    if printf '%s' "$recovered_node_b" | grep --quiet '"isReachable":true'; then
        break
    fi
    sleep 1
done

if ! printf '%s' "$recovered_node_b" | grep --quiet '"isReachable":true'; then
    echo "FAILURE: node-b did not become reachable again." >&2
    exit 1
fi

new_instance_id="$(printf '%s' "$recovered_node_b" |
    sed -n 's/.*"remoteInstanceId":"\([^"]*\)".*/\1/p')"
if [[ -z "$new_instance_id" || "$new_instance_id" == "$old_instance_id" ]]; then
    echo "FAILURE: node-b did not receive a new InstanceId." >&2
    exit 1
fi

node_b_stopped=false
echo "Initial node-b InstanceId: $old_instance_id"
echo "During stop: node-b unreachable with neutral error; node-c reachable"
echo "Restarted node-b InstanceId: $new_instance_id"
echo "SUCCESS: direct mesh connectivity, failure isolation, and restart identity verified."
