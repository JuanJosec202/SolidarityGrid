$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
$nodeBStopped = $false
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)

function Assert-BeforeDeadline {
    if ([DateTimeOffset]::UtcNow -ge $deadline) {
        throw "Global heartbeat demo timeout exceeded."
    }
}

function Wait-Ready([string]$Url) {
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $health = Invoke-RestMethod -Uri $Url -TimeoutSec 3
            if ($health.status -eq "ready") { return }
        }
        catch {
            # The container may still be starting.
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for readiness at $Url."
}

function Get-NodeAStatus {
    Assert-BeforeDeadline
    Invoke-RestMethod -Uri "http://localhost:5101/mesh/status" -TimeoutSec 5
}

function Wait-PeerStatus([string]$NodeId, [string]$ExpectedStatus) {
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $status = Get-NodeAStatus
        $peer = $status.peers | Where-Object nodeId -eq $NodeId
        if ($peer.status -eq $ExpectedStatus) {
            return $peer
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for $NodeId to become $ExpectedStatus."
}

try {
    Wait-Ready "http://localhost:5101/health/ready"
    Wait-Ready "http://localhost:5102/health/ready"
    Wait-Ready "http://localhost:5103/health/ready"

    $initialNodeB = Wait-PeerStatus "node-b" "Alive"
    $null = Wait-PeerStatus "node-c" "Alive"
    $oldInstanceId = $initialNodeB.instanceId
    $oldRestartCount = [long]$initialNodeB.restartCount

    docker compose stop node-b
    if ($LASTEXITCODE -ne 0) { throw "Could not stop node-b." }
    $nodeBStopped = $true
    $failureStarted = [DateTimeOffset]::UtcNow

    $suspected = Wait-PeerStatus "node-b" "Suspected"
    $suspectedAfter = ([DateTimeOffset]::UtcNow - $failureStarted).TotalSeconds
    $unreachable = Wait-PeerStatus "node-b" "Unreachable"
    $unreachableAfter = ([DateTimeOffset]::UtcNow - $failureStarted).TotalSeconds

    $duringFailure = Get-NodeAStatus
    $nodeC = $duringFailure.peers | Where-Object nodeId -eq "node-c"
    if ($nodeC.status -ne "Alive") { throw "node-c did not remain Alive." }
    Wait-Ready "http://localhost:5101/health/ready"
    Write-Host "[node-a] Peer node-c remains Alive; local readiness remains ready."

    docker compose start node-b
    if ($LASTEXITCODE -ne 0) { throw "Could not start node-b." }
    Wait-Ready "http://localhost:5102/health/ready"
    $recovered = Wait-PeerStatus "node-b" "Alive"

    if ($recovered.instanceId -eq $oldInstanceId) {
        throw "node-b InstanceId did not change."
    }
    if ([long]$recovered.restartCount -le $oldRestartCount) {
        throw "node-b RestartCount did not increment."
    }

    $nodeBStopped = $false
    Write-Host ("Timeline: Alive -> Suspected ({0:N1}s) -> Unreachable ({1:N1}s) -> Alive" -f $suspectedAfter, $unreachableAfter)
    Write-Host "Instances: $oldInstanceId -> $($recovered.instanceId)"
    Write-Host "RestartCount: $oldRestartCount -> $($recovered.restartCount)"
    Write-Host "SUCCESS: heartbeat failure detection and recovery verified."
}
finally {
    if ($nodeBStopped) {
        docker compose start node-b | Out-Null
    }
    Pop-Location
}
