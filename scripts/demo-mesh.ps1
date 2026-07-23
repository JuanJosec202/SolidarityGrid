$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
$nodeBStopped = $false

function Wait-Ready([string]$Url) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            $health = Invoke-RestMethod -Uri $Url -TimeoutSec 3
            if ($health.status -eq "ready") {
                return
            }
        }
        catch {
            # The node may still be starting.
        }

        Start-Sleep -Seconds 1
    }

    throw "Timed out waiting for readiness at $Url."
}

function Get-Mesh {
    Invoke-RestMethod -Uri "http://localhost:5101/mesh/peers" -TimeoutSec 5
}

try {
    Wait-Ready "http://localhost:5101/health/ready"
    Wait-Ready "http://localhost:5102/health/ready"
    Wait-Ready "http://localhost:5103/health/ready"

    $initial = Get-Mesh
    if (($initial.peers | Where-Object isReachable).Count -ne 2) {
        throw "node-a did not report both peers as reachable."
    }

    $initialNodeB = $initial.peers | Where-Object nodeId -eq "node-b"
    $oldInstanceId = $initialNodeB.remoteInstanceId
    if ([string]::IsNullOrWhiteSpace($oldInstanceId)) {
        throw "node-b did not return an InstanceId."
    }

    docker compose stop node-b
    if ($LASTEXITCODE -ne 0) {
        throw "Could not stop node-b."
    }
    $nodeBStopped = $true

    $duringFailure = Get-Mesh
    $failedNodeB = $duringFailure.peers | Where-Object nodeId -eq "node-b"
    $availableNodeC = $duringFailure.peers | Where-Object nodeId -eq "node-c"
    if ($failedNodeB.isReachable -or -not $failedNodeB.errorCode) {
        throw "node-b was not reported with a neutral connectivity error."
    }
    if (-not $availableNodeC.isReachable) {
        throw "node-c became unreachable while node-b was stopped."
    }
    Wait-Ready "http://localhost:5101/health/ready"

    docker compose start node-b
    if ($LASTEXITCODE -ne 0) {
        throw "Could not restart node-b."
    }
    Wait-Ready "http://localhost:5102/health/ready"

    $recovered = $null
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        $recovered = Get-Mesh
        $recoveredNodeB = $recovered.peers | Where-Object nodeId -eq "node-b"
        if ($recoveredNodeB.isReachable) {
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not $recoveredNodeB.isReachable) {
        throw "node-b did not become reachable again."
    }
    if ($recoveredNodeB.remoteInstanceId -eq $oldInstanceId) {
        throw "node-b retained the same InstanceId after restart."
    }

    $nodeBStopped = $false
    Write-Host "Initial node-b InstanceId: $oldInstanceId"
    Write-Host "During stop: node-b=$($failedNodeB.errorCode), node-c reachable=$($availableNodeC.isReachable)"
    Write-Host "Restarted node-b InstanceId: $($recoveredNodeB.remoteInstanceId)"
    Write-Host "SUCCESS: direct mesh connectivity, failure isolation, and restart identity verified."
}
finally {
    if ($nodeBStopped) {
        docker compose start node-b | Out-Null
    }
    Pop-Location
}
