[CmdletBinding()]
param(
    [int]$ReceiverPort = 5101,
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
$ports = @(5101, 5102, 5103)
Add-Type -AssemblyName System.Net.Http

function Wait-Nodes {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $allReady = $true
        foreach ($port in $ports) {
            try {
                $ready = Invoke-RestMethod "http://localhost:$port/health/ready"
                $mesh = Invoke-RestMethod "http://localhost:$port/mesh/status"
                if ($ready.status -ne "ready" -or
                    @($mesh.peers | Where-Object { $_.status -ne "Alive" }).Count -ne 0) {
                    $allReady = $false
                }
            }
            catch {
                $allReady = $false
            }
        }

        if ($allReady) {
            return
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    throw "The three nodes did not become ready with Alive peers."
}

function Submit-Payment {
    $client = [System.Net.Http.HttpClient]::new()
    try {
        $key = "PROCESS-DEMO-$([guid]::NewGuid().ToString('N'))"
        $client.DefaultRequestHeaders.Add("Idempotency-Key", $key)
        $content = [System.Net.Http.StringContent]::new(
            '{"amount":777.77,"currency":"COP"}',
            [System.Text.Encoding]::UTF8,
            "application/json")
        $response = $client.PostAsync(
            "http://localhost:$ReceiverPort/pay",
            $content).GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ([int]$response.StatusCode -ne 202) {
            throw "POST /pay returned $([int]$response.StatusCode): $body"
        }

        return $body | ConvertFrom-Json
    }
    finally {
        $client.Dispose()
    }
}

function Get-Payment([int]$Port, [string]$PaymentId) {
    Invoke-RestMethod "http://localhost:$Port/payments/$PaymentId"
}

try {
    Wait-Nodes
    $createdAt = Get-Date
    $created = Submit-Payment
    $paymentId = $created.paymentId
    Write-Host "PaymentId: $paymentId"
    Write-Host "Receiver: node-$([char](96 + ($ReceiverPort - 5100)))"

    $seenStatuses = [System.Collections.Generic.HashSet[string]]::new()
    [void]$seenStatuses.Add([string]$created.status)
    $owners = [System.Collections.Generic.HashSet[string]]::new()
    $terms = [System.Collections.Generic.HashSet[long]]::new()
    $processingVersions = [System.Collections.Generic.HashSet[long]]::new()
    $final = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    do {
        $snapshots = @()
        foreach ($port in $ports) {
            try {
                $snapshots += Get-Payment $port $paymentId
            }
            catch {
                $snapshots = @()
                break
            }
        }

        foreach ($payment in $snapshots) {
            [void]$seenStatuses.Add([string]$payment.status)
            if ($payment.ownerNodeId) {
                [void]$owners.Add([string]$payment.ownerNodeId)
                [void]$terms.Add([long]$payment.term)
            }

            if ($payment.status -eq "Processing") {
                [void]$processingVersions.Add([long]$payment.version)
            }
        }

        if ($snapshots.Count -eq 3 -and
            @($snapshots | Where-Object { $_.status -ne "Completed" }).Count -eq 0) {
            $final = $snapshots
            break
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    if ($null -eq $final) {
        throw "The payment did not converge to Completed within $TimeoutSeconds seconds."
    }

    if (-not $seenStatuses.Contains("Replicated") -or
        -not $seenStatuses.Contains("Processing")) {
        throw "The observable lifecycle did not include Replicated and Processing."
    }

    if ($owners.Count -ne 1 -or $terms.Count -ne 1) {
        throw "More than one owner or term was observed."
    }

    if (@($final | Where-Object {
        [int]$_.attempt -ne 1 -or
        $null -eq $_.completedAtUtc -or
        $null -ne $_.leaseExpiresAtUtc
    }).Count -ne 0) {
        throw "Final state has an invalid attempt, completion time, or lease."
    }

    $logText = docker compose logs --no-color
    $escapedId = [regex]::Escape($paymentId)
    $renewalCount = @(
        $logText | Select-String "Lease renewed for payment $escapedId"
    ).Count
    $completionCount = @(
        $logText | Select-String "Payment $escapedId completed successfully"
    ).Count
    if ($renewalCount -lt 1) {
        throw "No lease renewal was observed."
    }

    if ($completionCount -ne 1) {
        throw "Expected exactly one completion log; observed $completionCount."
    }

    $elapsed = [math]::Round(((Get-Date) - $createdAt).TotalSeconds, 2)
    Write-Host "Owner: $($owners | Select-Object -First 1)"
    Write-Host "Term: $($terms | Select-Object -First 1)"
    Write-Host "Attempt: 1"
    Write-Host "Processing elapsed: $elapsed seconds"
    Write-Host "Lease renewals: $renewalCount"
    Write-Host "Completed on node-a, node-b and node-c."
    Write-Host "Completion logs: $completionCount"
    Write-Host "SUCCESS"
}
catch {
    Write-Error "FAILURE: $($_.Exception.Message)"
    exit 1
}
