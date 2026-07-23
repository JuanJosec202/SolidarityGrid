param(
    [switch]$BuildImage
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot

try {
    dotnet restore .\SolidarityGrid.sln
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet format .\SolidarityGrid.sln --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet build .\SolidarityGrid.sln --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet test .\SolidarityGrid.sln --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    docker compose config
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if ($BuildImage) {
        docker compose build
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
finally {
    Pop-Location
}
