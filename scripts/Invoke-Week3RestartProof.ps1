[CmdletBinding()]
param(
    [string]$ComposeFile = "compose.week3.yml",
    [string]$ApiBaseUrl = "http://localhost:8080"
)

$ErrorActionPreference = "Stop"

function Get-MainQueueState {
    $json = docker compose -f $ComposeFile exec -T rabbitmq `
        rabbitmqctl list_queues name messages_ready messages_unacknowledged consumers --formatter json

    if ($LASTEXITCODE -ne 0) {
        throw "Could not read the RabbitMQ queue state."
    }

    $queues = $json | ConvertFrom-Json
    return ($queues | Where-Object {
        $_.name -eq "nsa.notifications.bulk.v1"
    })
}

docker compose -f $ComposeFile up -d --no-deps worker | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Could not start the worker container."
}

$consumerDeadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Milliseconds 500
    $consumerState = Get-MainQueueState
} while ($consumerState.consumers -lt 1 -and (Get-Date) -lt $consumerDeadline)

if ($consumerState.consumers -lt 1) {
    throw "The worker did not register its RabbitMQ consumer before the deadline."
}

docker compose -f $ComposeFile pause worker | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Could not pause the worker container."
}

$workerPaused = $true

try {
    $request = @{
        notifications = @(
            @{
                recipientEmail = "restart-proof@example.test"
                channel = 1
                subject = "Week 3 worker restart proof"
                body = "This delivery was interrupted after RabbitMQ dispatched it."
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
    $jobId = $accepted.jobId

    $dispatchDeadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $queueBefore = Get-MainQueueState
    } while ($queueBefore.messages_unacknowledged -lt 1 -and (Get-Date) -lt $dispatchDeadline)

    if ($queueBefore.messages_unacknowledged -lt 1) {
        throw "RabbitMQ did not expose an unacknowledged delivery before the deadline."
    }

    $queued = Invoke-RestMethod -Uri "$ApiBaseUrl/api/v2/notifications/bulk/$jobId"

    [pscustomobject]@{
        Phase = "delivery-unacknowledged"
        HttpStatus = [int]$acceptedResponse.StatusCode
        JobId = $jobId
        ApiStatus = $queued.status
        QueueReady = $queueBefore.messages_ready
        QueueUnacknowledged = $queueBefore.messages_unacknowledged
        Consumers = $queueBefore.consumers
        CorrelationId = $queued.correlationId
    } | Format-List | Out-Host

    docker compose -f $ComposeFile kill worker | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Could not terminate the paused worker container."
    }
    $workerPaused = $false

    docker compose -f $ComposeFile up -d --no-deps worker | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Could not restart the worker container."
    }

    $deadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 750
        $current = Invoke-RestMethod -Uri "$ApiBaseUrl/api/v2/notifications/bulk/$jobId"
    } while (
        $current.status -notin @("Completed", "CompletedWithErrors", "DeadLettered") -and
        (Get-Date) -lt $deadline
    )

    if ($current.status -ne "Completed") {
        throw "Restart proof job ended in '$($current.status)'."
    }

    $settleDeadline = (Get-Date).AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 500
        $queueAfter = Get-MainQueueState
    } while ($queueAfter.messages_unacknowledged -gt 0 -and (Get-Date) -lt $settleDeadline)

    [pscustomobject]@{
        Phase = "redelivered-after-restart"
        JobId = $jobId
        ApiStatus = $current.status
        Processed = $current.processedCount
        Succeeded = $current.succeededCount
        QueueReady = $queueAfter.messages_ready
        QueueUnacknowledged = $queueAfter.messages_unacknowledged
        Consumers = $queueAfter.consumers
        CorrelationId = $current.correlationId
    } | Format-List
}
finally {
    if ($workerPaused) {
        docker compose -f $ComposeFile unpause worker | Out-Host
    }
}
