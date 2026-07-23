$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
$nodeBStopped = $false
$nodeCStopped = $false

function Wait-Ready([int]$Port) {
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $ready = Invoke-RestMethod "http://localhost:$Port/health/ready" -TimeoutSec 3
            if ($ready.status -eq "ready") { return }
        }
        catch {
            # The node may still be starting.
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for node on port $Port."
}

function Send-Payment([string]$Key, [string]$CorrelationId) {
    $headers = @{
        "Idempotency-Key" = $Key
        "X-Correlation-ID" = $CorrelationId
    }
    $body = @{ amount = 125.50; currency = "COP" } | ConvertTo-Json
    try {
        $response = Invoke-WebRequest `
            -Uri "http://localhost:5101/pay" `
            -Method Post `
            -Headers $headers `
            -ContentType "application/json" `
            -Body $body `
            -UseBasicParsing `
            -TimeoutSec 10
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Body = $response.Content | ConvertFrom-Json
            Location = $response.Headers["Location"]
        }
    }
    catch {
        if ($null -eq $_.Exception.Response) { throw }
        $errorResponse = $_.Exception.Response
        $reader = New-Object System.IO.StreamReader($errorResponse.GetResponseStream())
        try {
            $content = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
        return [pscustomobject]@{
            StatusCode = [int]$errorResponse.StatusCode
            Body = $content | ConvertFrom-Json
            Location = $errorResponse.Headers["Location"]
        }
    }
}

function Get-Payment([int]$Port, [string]$PaymentId) {
    Invoke-RestMethod `
        -Uri "http://localhost:$Port/payments/$PaymentId" `
        -TimeoutSec 5
}

function Assert-Replicated([object]$Payment, [string]$ExpectedId) {
    if ($Payment.paymentId -ne $ExpectedId) {
        throw "PaymentId was not preserved."
    }
    if ($Payment.status -ne "Replicated" -or [long]$Payment.version -ne 2) {
        throw "Payment is not Replicated at version 2."
    }
}

try {
    Wait-Ready 5101
    Wait-Ready 5102
    Wait-Ready 5103

    $first = Send-Payment "DEMO-REPLICATION-$([Guid]::NewGuid().ToString('N'))" "demo-replication-all"
    if ($first.StatusCode -ne 202) { throw "Three-node request did not return 202." }
    $firstId = $first.Body.paymentId
    Assert-Replicated (Get-Payment 5101 $firstId) $firstId
    Assert-Replicated (Get-Payment 5102 $firstId) $firstId
    Assert-Replicated (Get-Payment 5103 $firstId) $firstId
    Write-Host "Payment $firstId is durable on node-a, node-b and node-c."

    docker compose stop node-c
    if ($LASTEXITCODE -ne 0) { throw "Could not stop node-c." }
    $nodeCStopped = $true

    $second = Send-Payment "DEMO-ONE-PEER-$([Guid]::NewGuid().ToString('N'))" "demo-replication-one-peer"
    if ($second.StatusCode -ne 202) { throw "One-peer quorum did not return 202." }
    $secondId = $second.Body.paymentId
    Assert-Replicated (Get-Payment 5101 $secondId) $secondId
    Assert-Replicated (Get-Payment 5102 $secondId) $secondId
    Write-Host "Payment $secondId reached quorum with node-c stopped."

    docker compose stop node-b
    if ($LASTEXITCODE -ne 0) { throw "Could not stop node-b." }
    $nodeBStopped = $true

    $thirdKey = "DEMO-NO-QUORUM-$([Guid]::NewGuid().ToString('N'))"
    $third = Send-Payment $thirdKey "demo-replication-no-quorum"
    if ($third.StatusCode -ne 503) { throw "No-quorum request did not return 503." }
    if ($third.Body.code -ne "PAYMENT_REPLICATION_QUORUM_UNAVAILABLE") {
        throw "No-quorum response contained an unexpected code."
    }
    $thirdId = [System.IO.Path]::GetFileName($third.Location)
    $received = Get-Payment 5101 $thirdId
    if ($received.status -ne "Received" -or [long]$received.version -ne 1) {
        throw "No-quorum payment was not preserved as Received version 1."
    }

    docker compose start node-b
    if ($LASTEXITCODE -ne 0) { throw "Could not start node-b." }
    Wait-Ready 5102
    $retry = Send-Payment $thirdKey "demo-replication-retry"
    if ($retry.StatusCode -notin @(200, 202)) {
        throw "Replication retry did not succeed."
    }
    if ($retry.Body.paymentId -ne $thirdId) {
        throw "Replication retry changed PaymentId."
    }
    Assert-Replicated $retry.Body $thirdId
    Assert-Replicated (Get-Payment 5102 $thirdId) $thirdId
    $nodeBStopped = $false

    Write-Host "No quorum: 503 with local Received; retry preserved $thirdId and reached Replicated."
    Write-Host "SUCCESS: durable payment replication and quorum 2/3 verified."
}
finally {
    if ($nodeBStopped) { docker compose start node-b | Out-Null }
    if ($nodeCStopped) { docker compose start node-c | Out-Null }
    Pop-Location
}
