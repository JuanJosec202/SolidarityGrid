[CmdletBinding()]
param(
    [int]$ReceiverPort = 5101,
    [int]$TimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"
$nodes = [ordered]@{
    "node-a" = 5101
    "node-b" = 5102
    "node-c" = 5103
}
$startedAt = Get-Date
$deadline = $startedAt.AddSeconds($TimeoutSeconds)
Add-Type -AssemblyName System.Net.Http

function Assert-InTime {
    if ((Get-Date) -ge $deadline) {
        throw "Global timeout of $TimeoutSeconds seconds was exceeded."
    }
}

function Get-Json([string]$Url) {
    Invoke-RestMethod -Uri $Url -TimeoutSec 3
}

function Get-Payment([string]$NodeId, [string]$PaymentId) {
    Get-Json "http://localhost:$($nodes[$NodeId])/payments/$PaymentId"
}

function Submit-Payment([string]$NodeId, [string]$Prefix) {
    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromSeconds(3)
    try {
        $key = "$Prefix-$([guid]::NewGuid().ToString('N'))"
        $client.DefaultRequestHeaders.Add("Idempotency-Key", $key)
        $content = [System.Net.Http.StringContent]::new(
            '{"amount":777.77,"currency":"COP"}',
            [System.Text.Encoding]::UTF8,
            "application/json")
        $response = $client.PostAsync(
            "http://localhost:$($nodes[$NodeId])/pay",
            $content).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Body = $body
            Payment = if ($body) { $body | ConvertFrom-Json } else { $null }
        }
    }
    finally {
        $client.Dispose()
    }
}

function Get-PeerStatus([string]$Survivor, [string]$PeerNodeId) {
    $mesh = Get-Json "http://localhost:$($nodes[$Survivor])/mesh/status"
    @($mesh.peers | Where-Object { $_.nodeId -eq $PeerNodeId }) |
        Select-Object -First 1
}

try {
    do {
        Assert-InTime
        $allReady = $true
        foreach ($entry in $nodes.GetEnumerator()) {
            try {
                $ready = Get-Json "http://localhost:$($entry.Value)/health/ready"
                $mesh = Get-Json "http://localhost:$($entry.Value)/mesh/status"
                if ($ready.status -ne "ready" -or
                    @($mesh.peers | Where-Object {
                        $_.status -ne "Alive"
                    }).Count -ne 0) {
                    $allReady = $false
                }
            }
            catch {
                $allReady = $false
            }
        }

        if (-not $allReady) {
            Start-Sleep -Milliseconds 250
        }
    } while (-not $allReady)

    $receiverNodeId = @(
        $nodes.GetEnumerator() |
            Where-Object { $_.Value -eq $ReceiverPort } |
            Select-Object -ExpandProperty Key
    ) | Select-Object -First 1
    if (-not $receiverNodeId) {
        throw "ReceiverPort $ReceiverPort does not identify a configured node."
    }

    $created = Submit-Payment $receiverNodeId "FAILOVER-DEMO"
    if ($created.StatusCode -ne 202) {
        throw "POST /pay returned $($created.StatusCode): $($created.Body)"
    }

    $paymentId = [string]$created.Payment.paymentId
    $initial = $null
    do {
        Assert-InTime
        foreach ($nodeId in $nodes.Keys) {
            try {
                $candidate = Get-Payment $nodeId $paymentId
                if ($candidate.status -eq "Processing") {
                    $initial = $candidate
                    break
                }
            }
            catch {
            }
        }

        if ($null -eq $initial) {
            Start-Sleep -Milliseconds 150
        }
    } while ($null -eq $initial)

    $ownerInitial = [string]$initial.ownerNodeId
    $termInitial = [long]$initial.term
    $attemptInitial = [int]$initial.attempt
    if (-not $nodes.Contains($ownerInitial) -or
        $termInitial -lt 1 -or
        $attemptInitial -ne 1) {
        throw "Initial processing identity is invalid."
    }

    Start-Sleep -Seconds 3
    Assert-InTime
    $latestBeforeKill = $null
    foreach ($nodeId in $nodes.Keys) {
        try {
            $candidate = Get-Payment $nodeId $paymentId
            if ($candidate.ownerNodeId -eq $ownerInitial -and
                $candidate.status -eq "Processing") {
                $latestBeforeKill = $candidate
                break
            }
        }
        catch {
        }
    }

    if ($null -eq $latestBeforeKill) {
        throw "Payment was not Processing immediately before the kill."
    }

    $leaseBeforeKill = [DateTimeOffset]$latestBeforeKill.leaseExpiresAtUtc
    $ownerContainerId = (
        docker compose ps -q $ownerInitial
    ).Trim()
    if (-not $ownerContainerId) {
        throw "Could not resolve the owner container for $ownerInitial."
    }

    $killAt = Get-Date
    docker kill $ownerContainerId | Out-Null
    $running = (
        docker inspect --format "{{.State.Running}}" $ownerContainerId
    ).Trim()
    if ($running -ne "false") {
        throw "Owner container $ownerInitial is still running after docker kill."
    }

    $survivors = @($nodes.Keys | Where-Object { $_ -ne $ownerInitial })
    $unreachableAt = $null
    do {
        Assert-InTime
        $allUnreachable = $true
        foreach ($survivor in $survivors) {
            try {
                $ready = Get-Json (
                    "http://localhost:$($nodes[$survivor])/health/ready"
                )
                if ($ready.status -ne "ready") {
                    throw "$survivor is not ready."
                }

                $peer = Get-PeerStatus $survivor $ownerInitial
                if ($null -eq $peer -or $peer.status -ne "Unreachable") {
                    $allUnreachable = $false
                }

                $payment = Get-Payment $survivor $paymentId
                if ([long]$payment.term -gt $termInitial -and
                    [DateTimeOffset]::UtcNow -lt $leaseBeforeKill) {
                    throw "Takeover occurred before the durable lease expired."
                }
            }
            catch {
                if ($_.Exception.Message -like
                    "*Takeover occurred*" -or
                    $_.Exception.Message -like "*is not ready*") {
                    throw
                }

                $allUnreachable = $false
            }
        }

        if ($allUnreachable) {
            $unreachableAt = Get-Date
        }
        else {
            Start-Sleep -Milliseconds 200
        }
    } while ($null -eq $unreachableAt)

    $takeover = $null
    $takeoverAt = $null
    do {
        Assert-InTime
        foreach ($survivor in $survivors) {
            $candidate = Get-Payment $survivor $paymentId
            if ($candidate.ownerNodeId -ne $ownerInitial -and
                [long]$candidate.term -gt $termInitial -and
                [int]$candidate.attempt -eq 2 -and
                $candidate.status -in @("Processing", "Completed")) {
                $takeover = $candidate
                $takeoverAt = Get-Date
                break
            }
        }

        if ($null -eq $takeover) {
            Start-Sleep -Milliseconds 150
        }
    } while ($null -eq $takeover)

    $ownerNew = [string]$takeover.ownerNodeId
    $termNew = [long]$takeover.term
    $second = Submit-Payment $survivors[0] "FAILOVER-QUORUM"
    if ($second.StatusCode -ne 202) {
        throw "Second POST /pay returned $($second.StatusCode): $($second.Body)"
    }

    $finalSnapshots = $null
    do {
        Assert-InTime
        $snapshots = @()
        foreach ($survivor in $survivors) {
            try {
                $snapshots += Get-Payment $survivor $paymentId
            }
            catch {
                $snapshots = @()
                break
            }
        }

        if ($snapshots.Count -eq 2 -and
            @($snapshots | Where-Object {
                $_.status -ne "Completed" -or
                $_.ownerNodeId -ne $ownerNew -or
                [long]$_.term -ne $termNew -or
                [int]$_.attempt -ne 2 -or
                $null -ne $_.leaseExpiresAtUtc -or
                $null -eq $_.completedAtUtc
            }).Count -eq 0) {
            $finalSnapshots = $snapshots
        }
        else {
            Start-Sleep -Milliseconds 200
        }
    } while ($null -eq $finalSnapshots)

    $running = (
        docker inspect --format "{{.State.Running}}" $ownerContainerId
    ).Trim()
    if ($running -ne "false") {
        throw "Old owner was restarted before completion verification."
    }

    $logs = docker compose logs --no-color
    $completionPattern = "Payment $([regex]::Escape($paymentId)) completed successfully"
    $completionCount = @(
        $logs | Select-String -Pattern $completionPattern
    ).Count
    if ($completionCount -ne 1) {
        throw "Expected one PaymentCompleted event; observed $completionCount."
    }

    $unreachableSeconds = [math]::Round(
        ($unreachableAt - $killAt).TotalSeconds,
        2)
    $leaseSeconds = [math]::Round(
        [math]::Max(
            0,
            ($leaseBeforeKill.UtcDateTime - $killAt.ToUniversalTime()).TotalSeconds),
        2)
    $takeoverSeconds = [math]::Round(
        ($takeoverAt - $killAt).TotalSeconds,
        2)

    Write-Host "PaymentId: $paymentId"
    Write-Host "Receiver: $receiverNodeId"
    Write-Host "Initial owner: $ownerInitial"
    Write-Host "Initial term: $termInitial"
    Write-Host "Initial attempt: $attemptInitial"
    Write-Host "Kill moment: $($killAt.ToUniversalTime().ToString('O'))"
    Write-Host "Time until Unreachable: $unreachableSeconds seconds"
    Write-Host "Time until lease expiration: $leaseSeconds seconds"
    Write-Host "Time until takeover: $takeoverSeconds seconds"
    Write-Host "New owner: $ownerNew"
    Write-Host "New term: $termNew"
    Write-Host "New attempt: 2"
    Write-Host "Survivor $($survivors[0]): Completed"
    Write-Host "Survivor $($survivors[1]): Completed"
    Write-Host "PaymentCompleted events: $completionCount"
    Write-Host "Second payment: HTTP $($second.StatusCode) ($($second.Payment.paymentId))"
    Write-Host "Old owner running: false"
    Write-Host "SUCCESS"
}
catch {
    Write-Error "FAILURE: $($_.Exception.Message)"
    exit 1
}
