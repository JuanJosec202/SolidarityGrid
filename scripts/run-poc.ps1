[CmdletBinding()]
param(
    [switch]$KeepRunning,
    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$services = @("node-a", "node-b", "node-c")
$overall = [System.Diagnostics.Stopwatch]::StartNew()
$buildDuration = [TimeSpan]::Zero
$startupDuration = [TimeSpan]::Zero
$demoDuration = [TimeSpan]::Zero
$exitCode = 0

function Assert-NativeSuccess([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

function Get-RemainingSeconds {
    $remaining = $TimeoutSeconds - [Math]::Floor($overall.Elapsed.TotalSeconds)
    if ($remaining -le 0) {
        throw "Global timeout of $TimeoutSeconds seconds was exceeded."
    }

    return [int]$remaining
}

function Wait-ClusterHealthy {
    Write-Host "Waiting for all three nodes to become healthy..."

    while ($true) {
        Get-RemainingSeconds | Out-Null
        $healthy = 0

        foreach ($service in $services) {
            $containerId = docker compose ps -q $service
            Assert-NativeSuccess "docker compose ps for $service"

            if ([string]::IsNullOrWhiteSpace($containerId)) {
                continue
            }

            $status = docker inspect `
                --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}" `
                $containerId
            Assert-NativeSuccess "docker inspect for $service"

            if ($status.Trim() -eq "healthy") {
                $healthy++
            }
        }

        if ($healthy -eq $services.Count) {
            Write-Host "Cluster healthy: $healthy/$($services.Count) nodes."
            return
        }

        Start-Sleep -Milliseconds 500
    }
}

Push-Location $repositoryRoot

try {
    Write-Host "Checking Docker availability..."
    docker version --format "{{.Server.Version}}" | Out-Null
    Assert-NativeSuccess "Docker availability check"

    Write-Host "Removing previous PoC resources..."
    docker compose down -v
    Assert-NativeSuccess "Initial Docker cleanup"

    Write-Host "Building the shared node image..."
    $phase = [System.Diagnostics.Stopwatch]::StartNew()
    docker compose build
    Assert-NativeSuccess "Docker Compose build"
    $phase.Stop()
    $buildDuration = $phase.Elapsed
    Get-RemainingSeconds | Out-Null

    Write-Host "Starting the three-node cluster..."
    $phase.Restart()
    docker compose up -d
    Assert-NativeSuccess "Docker Compose startup"
    Wait-ClusterHealthy
    $phase.Stop()
    $startupDuration = $phase.Elapsed

    Write-Host "Running abrupt-owner failover demo..."
    $phase.Restart()
    $demoTimeout = Get-RemainingSeconds
    & powershell.exe -NoProfile -ExecutionPolicy Bypass `
        -File .\scripts\demo-failover.ps1 `
        -TimeoutSeconds $demoTimeout
    Assert-NativeSuccess "PowerShell failover demo"
    $phase.Stop()
    $demoDuration = $phase.Elapsed

    $overall.Stop()
    Write-Host ""
    Write-Host "SolidarityGrid PoC completed successfully."
    Write-Host ("Build:   {0:N1} s" -f $buildDuration.TotalSeconds)
    Write-Host ("Startup: {0:N1} s" -f $startupDuration.TotalSeconds)
    Write-Host ("Failover:{0,6:N1} s" -f $demoDuration.TotalSeconds)
    Write-Host ("Total:   {0:N1} s" -f $overall.Elapsed.TotalSeconds)
}
catch {
    $exitCode = 1
    Write-Host "SolidarityGrid PoC failed: $($_.Exception.Message)" `
        -ForegroundColor Red
}
finally {
    if ($KeepRunning) {
        Write-Host "KeepRunning enabled: containers and volumes were left for inspection."
        Write-Host "Cleanup command: docker compose down -v"
    }
    else {
        Write-Host "Cleaning up containers, network and volumes..."
        docker compose down -v
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Docker cleanup failed with exit code $LASTEXITCODE." `
                -ForegroundColor Red
            $exitCode = 1
        }
    }

    Pop-Location
}

exit $exitCode
