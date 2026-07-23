#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

dotnet restore ./SolidarityGrid.sln
dotnet format ./SolidarityGrid.sln --verify-no-changes --no-restore
dotnet build ./SolidarityGrid.sln --no-restore
dotnet test ./SolidarityGrid.sln --no-build
docker compose config

if [[ "${1:-}" == "--build-image" ]]; then
    docker compose build
fi
