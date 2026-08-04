[CmdletBinding()]
param(
    [string]$ComposeFile = "compose.week3.yml",
    [string]$ApiBaseUrl = "http://localhost:8080"
)

$ErrorActionPreference = "Stop"

function Get-QueueStates {
    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = "SilentlyContinue"
    try {
        $json = docker compose -f $ComposeFile exec -T rabbitmq `
            rabbitmqctl list_queues name messages_ready messages_unacknowledged consumers --formatter json 2>$null
        $rabbitExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorPreference
    }

    if ($rabbitExitCode -ne 0 -or -not $json) {
        return $null
    }

    return ($json | ConvertFrom-Json)
}

$queuesBefore = Get-QueueStates
if (-not $queuesBefore) {
    throw "RabbitMQ was not ready before the restart test."
}

$dlqBefore = $queuesBefore | Where-Object {
    $_.name -eq "nsa.notifications.bulk.dlq"
}

$restartStartedUtc = [DateTimeOffset]::UtcNow
docker compose -f $ComposeFile restart rabbitmq | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Could not restart the RabbitMQ container."
}

$recoveryDeadline = (Get-Date).AddSeconds(60)
do {
    Start-Sleep -Seconds 1
    $queuesAfterRestart = Get-QueueStates
    $mainAfterRestart = $queuesAfterRestart | Where-Object {
        $_.name -eq "nsa.notifications.bulk.v1"
    }
} while (
    (-not $mainAfterRestart -or $mainAfterRestart.consumers -lt 1) -and
    (Get-Date) -lt $recoveryDeadline
)

if (-not $mainAfterRestart -or $mainAfterRestart.consumers -lt 1) {
    throw "RabbitMQ recovered, but the worker consumer did not reconnect before the deadline."
}

$request = @{
    notifications = @(
        @{
            recipientEmail = "broker-restart-proof@example.test"
            channel = 1
            subject = "Week 3 broker restart proof"
            body = "Published after the RabbitMQ container restarted."
            orderId = $null
        }
    )
} | ConvertTo-Json -Depth 5

$acceptedResponse = Invoke-WebRequest `
    -UseBasicParsing `
    -Uri "$ApiBaseUrl/api/v2/notifications/bulk" `
    -Method Post `
    -ContentType "application/json" `
    -Body $request

$accepted = $acceptedResponse.Content | ConvertFrom-Json
$deadline = (Get-Date).AddSeconds(45)
do {
    Start-Sleep -Milliseconds 750
    $current = Invoke-RestMethod -Uri "$ApiBaseUrl/api/v2/notifications/bulk/$($accepted.jobId)"
} while (
    $current.status -notin @("Completed", "CompletedWithErrors", "DeadLettered") -and
    (Get-Date) -lt $deadline
)

if ($current.status -ne "Completed") {
    throw "Broker restart proof job ended in '$($current.status)'."
}

$queuesAfter = Get-QueueStates
$dlqAfter = $queuesAfter | Where-Object {
    $_.name -eq "nsa.notifications.bulk.dlq"
}

if ($dlqAfter.messages_ready -lt $dlqBefore.messages_ready) {
    throw "The durable DLQ lost messages across the broker restart."
}

[pscustomobject]@{
    RestartStartedUtc = $restartStartedUtc.ToString("O")
    HttpStatus = [int]$acceptedResponse.StatusCode
    JobId = $current.jobId
    ApiStatus = $current.status
    Processed = $current.processedCount
    Succeeded = $current.succeededCount
    MainQueueConsumers = $mainAfterRestart.consumers
    DlqMessagesBefore = $dlqBefore.messages_ready
    DlqMessagesAfter = $dlqAfter.messages_ready
    CorrelationId = $current.correlationId
} | Format-List
