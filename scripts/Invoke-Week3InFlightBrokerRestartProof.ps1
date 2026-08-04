[CmdletBinding()]
param(
    [string]$ComposeFile = "compose.week3.yml",
    [string]$ApiBaseUrl = "http://localhost:8080"
)

$ErrorActionPreference = "Stop"
$workerPaused = $false

function Get-MainQueueState {
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "SilentlyContinue"
    try {
        $json = docker compose -f $ComposeFile exec -T rabbitmq `
            rabbitmqctl list_queues name messages_ready messages_unacknowledged consumers --formatter json 2>$null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($exitCode -ne 0 -or -not $json) {
        return $null
    }

    return (($json | ConvertFrom-Json) | Where-Object { $_.name -eq "nsa.notifications.bulk.v1" })
}

try {
    docker compose -f $ComposeFile up -d | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Could not start the Week 3 stack." }

    $consumerDeadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Milliseconds 500
        $before = Get-MainQueueState
    } while (($null -eq $before -or $before.consumers -lt 1) -and (Get-Date) -lt $consumerDeadline)
    if ($null -eq $before -or $before.consumers -lt 1) {
        throw "The worker consumer was not ready before the test."
    }

    docker compose -f $ComposeFile pause worker | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Could not pause the worker." }
    $workerPaused = $true

    $request = @{
        notifications = @(
            @{
                recipientEmail = "in-flight-broker-restart@example.test"
                channel = 1
                subject = "Week 3 in-flight broker restart proof"
                body = "This durable command is unacknowledged while RabbitMQ restarts."
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

    $dispatchDeadline = (Get-Date).AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 500
        $dispatched = Get-MainQueueState
    } while (($null -eq $dispatched -or $dispatched.messages_unacknowledged -lt 1) -and (Get-Date) -lt $dispatchDeadline)
    if ($null -eq $dispatched -or $dispatched.messages_unacknowledged -lt 1) {
        throw "RabbitMQ did not expose an unacknowledged command before restart."
    }

    docker compose -f $ComposeFile restart rabbitmq | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Could not restart RabbitMQ." }

    docker compose -f $ComposeFile unpause worker | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Could not unpause the worker." }
    $workerPaused = $false

    $recoveryDeadline = (Get-Date).AddSeconds(75)
    do {
        Start-Sleep -Seconds 1
        $after = Get-MainQueueState
        try {
            $status = Invoke-RestMethod -Uri "$ApiBaseUrl/api/v2/notifications/bulk/$($accepted.jobId)"
        }
        catch {
            $status = $null
        }
    } while (
        ($null -eq $after -or $after.consumers -lt 1 -or $null -eq $status -or $status.status -notin @("Completed", "CompletedWithErrors", "DeadLettered")) -and
        (Get-Date) -lt $recoveryDeadline
    )

    if ($null -eq $status -or $status.status -ne "Completed") {
        throw "The in-flight job did not complete after broker recovery."
    }
    if ($null -eq $after -or $after.consumers -lt 1) {
        throw "The worker consumer did not recover after broker restart."
    }

    [pscustomobject]@{
        HttpStatus = [int]$acceptedResponse.StatusCode
        JobId = $status.jobId
        StatusBeforeRestart = "Queued"
        UnacknowledgedBeforeRestart = $dispatched.messages_unacknowledged
        StatusAfterRestart = $status.status
        Processed = $status.processedCount
        Succeeded = $status.succeededCount
        ConsumersAfterRestart = $after.consumers
        ReadyAfterRestart = $after.messages_ready
        UnacknowledgedAfterRestart = $after.messages_unacknowledged
        CorrelationId = $status.correlationId
    } | Format-List
}
finally {
    if ($workerPaused) {
        docker compose -f $ComposeFile unpause worker | Out-Host
    }
}
