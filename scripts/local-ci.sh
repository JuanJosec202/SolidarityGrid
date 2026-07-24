#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"

dotnet tool restore
dotnet restore ./SolidarityGrid.sln
dotnet format ./SolidarityGrid.sln --verify-no-changes --no-restore
dotnet build ./SolidarityGrid.sln --no-restore
dotnet tool run dotnet-ef migrations list \
    --project ./src/SolidarityGrid.Infrastructure \
    --startup-project ./src/SolidarityGrid.Node \
    --context SolidarityGridDbContext \
    --no-build
dotnet tool run dotnet-ef migrations has-pending-model-changes \
    --project ./src/SolidarityGrid.Infrastructure \
    --startup-project ./src/SolidarityGrid.Node \
    --context SolidarityGridDbContext \
    --no-build
dotnet test ./SolidarityGrid.sln --no-build
docker compose config
docker compose build
