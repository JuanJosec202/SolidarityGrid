param(
    [string]$BaseUrl = "http://localhost:5101"
)

$ErrorActionPreference = "Stop"

$readiness = Invoke-RestMethod -Uri "$BaseUrl/health/ready" -Method Get
if ($readiness.status -ne "ready") {
    throw "node-a is not ready."
}

$idempotencyKey = "DEMO-$([Guid]::NewGuid().ToString('N'))"
$headers = @{ "Idempotency-Key" = $idempotencyKey }
$payload = @{ amount = 150000; currency = "COP" } | ConvertTo-Json -Compress

$createdResponse = Invoke-WebRequest `
    -Uri "$BaseUrl/pay" `
    -Method Post `
    -Headers $headers `
    -ContentType "application/json" `
    -Body $payload `
    -UseBasicParsing
if ([int]$createdResponse.StatusCode -ne 202) {
    throw "Expected 202, received $($createdResponse.StatusCode)."
}
$created = $createdResponse.Content | ConvertFrom-Json

$replayResponse = Invoke-WebRequest `
    -Uri "$BaseUrl/pay" `
    -Method Post `
    -Headers $headers `
    -ContentType "application/json" `
    -Body $payload `
    -UseBasicParsing
if ([int]$replayResponse.StatusCode -ne 200) {
    throw "Expected replay 200, received $($replayResponse.StatusCode)."
}
$replay = $replayResponse.Content | ConvertFrom-Json
if ($created.paymentId -ne $replay.paymentId) {
    throw "Replay returned a different payment ID."
}

$payment = Invoke-RestMethod `
    -Uri "$BaseUrl/payments/$($created.paymentId)" `
    -Method Get
if ($payment.paymentId -ne $created.paymentId) {
    throw "GET returned a different payment ID."
}

$conflictingPayload =
    @{ amount = 150001; currency = "COP" } | ConvertTo-Json -Compress
$conflictStatus = 0
try {
    Invoke-WebRequest `
        -Uri "$BaseUrl/pay" `
        -Method Post `
        -Headers $headers `
        -ContentType "application/json" `
        -Body $conflictingPayload `
        -UseBasicParsing | Out-Null
}
catch {
    $conflictStatus = [int]$_.Exception.Response.StatusCode
}
if ($conflictStatus -ne 409) {
    throw "Expected conflict 409, received $conflictStatus."
}

Write-Host "SolidarityGrid API demo succeeded."
Write-Host "Node: $BaseUrl"
Write-Host "Idempotency-Key: $idempotencyKey"
Write-Host "PaymentId: $($created.paymentId)"
Write-Host "Created: 202; Replay: 200; GET: 200; Conflict: 409"
