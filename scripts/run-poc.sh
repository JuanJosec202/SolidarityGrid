#!/usr/bin/env bash
set -Eeuo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
services=(node-a node-b node-c)
keep_running=false
timeout_seconds=180
started_at=$SECONDS
build_duration=0
startup_duration=0
demo_duration=0

usage() {
    echo "Usage: $0 [--keep-running]"
}

for argument in "$@"; do
    case "$argument" in
        --keep-running)
            keep_running=true
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            usage >&2
            echo "Unknown argument: $argument" >&2
            exit 2
            ;;
    esac
done

remaining_seconds() {
    local remaining=$((timeout_seconds - (SECONDS - started_at)))
    if (( remaining <= 0 )); then
        echo "Global timeout of ${timeout_seconds}s was exceeded." >&2
        return 1
    fi

    printf '%s' "$remaining"
}

cleanup() {
    local status=$?
    trap - EXIT

    if [[ "$keep_running" == true ]]; then
        echo "KeepRunning enabled: containers and volumes were left for inspection."
        echo "Cleanup command: docker compose down -v"
    else
        echo "Cleaning up containers, network and volumes..."
        if ! docker compose down -v; then
            echo "Docker cleanup failed." >&2
            status=1
        fi
    fi

    exit "$status"
}

wait_cluster_healthy() {
    echo "Waiting for all three nodes to become healthy..."

    while true; do
        remaining_seconds >/dev/null
        local healthy=0
        local service container_id status

        for service in "${services[@]}"; do
            container_id="$(docker compose ps -q "$service")"
            if [[ -z "$container_id" ]]; then
                continue
            fi

            status="$(docker inspect \
                --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' \
                "$container_id")"
            if [[ "$status" == "healthy" ]]; then
                ((healthy += 1))
            fi
        done

        if (( healthy == ${#services[@]} )); then
            echo "Cluster healthy: ${healthy}/${#services[@]} nodes."
            return
        fi

        sleep 0.5
    done
}

trap cleanup EXIT
cd "$repository_root"

echo "Checking Docker availability..."
docker version --format '{{.Server.Version}}' >/dev/null

echo "Removing previous PoC resources..."
docker compose down -v

echo "Building the shared node image..."
phase_started=$SECONDS
docker compose build
build_duration=$((SECONDS - phase_started))
remaining_seconds >/dev/null

echo "Starting the three-node cluster..."
phase_started=$SECONDS
docker compose up -d
wait_cluster_healthy
startup_duration=$((SECONDS - phase_started))

echo "Running abrupt-owner failover demo..."
phase_started=$SECONDS
bash ./scripts/demo-failover.sh 5101 "$(remaining_seconds)"
demo_duration=$((SECONDS - phase_started))

total_duration=$((SECONDS - started_at))
echo
echo "SolidarityGrid PoC completed successfully."
printf 'Build:    %d s\n' "$build_duration"
printf 'Startup:  %d s\n' "$startup_duration"
printf 'Failover: %d s\n' "$demo_duration"
printf 'Total:    %d s\n' "$total_duration"
