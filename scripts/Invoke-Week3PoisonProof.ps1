[CmdletBinding()]
param(
    [string]$ComposeFile = "compose.week3.yml",
    [string]$ApiBaseUrl = "http://localhost:8080",
    [string]$ManagementBaseUrl = "http://localhost:15672",
    [string]$RabbitUser,
    [string]$RabbitPassword,
    [string]$FailureSubject = "[week3-poison]"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Week3Environment.ps1"
$RabbitUser = if ([string]::IsNullOrWhiteSpace($RabbitUser)) {
    Get-Week3EnvironmentValue -Name 'WEEK3_RABBITMQ_USERNAME'
} else { $RabbitUser }
$RabbitPassword = if ([string]::IsNullOrWhiteSpace($RabbitPassword)) {
    Get-Week3EnvironmentValue -Name 'WEEK3_RABBITMQ_PASSWORD'
} else { $RabbitPassword }

function Wait-WorkerHealthy {
    $deadline = (Get-Date).AddSeconds(45)
    do {
        Start-Sleep -Seconds 1
        $health = docker inspect --format "{{.State.Health.Status}}" nsa-week3-worker-1
    } while ($health -ne "healthy" -and (Get-Date) -lt $deadline)

    if ($health -ne "healthy") {
        throw "The worker did not become healthy before the deadline."
    }
}

function Get-DlqState {
    $json = docker compose -f $ComposeFile exec -T rabbitmq `
        rabbitmqctl list_queues name messages_ready messages_unacknowledged --formatter json
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect the DLQ."
    }

    $queues = $json | ConvertFrom-Json
    return ($queues | Where-Object { $_.name -eq "nsa.notifications.bulk.dlq" })
}

$previousFailureSubject = [Environment]::GetEnvironmentVariable(
    "WEEK3_FAILURE_INJECTION_SUBJECT",
    "Process"
)
$poisonWorkerCreated = $false

try {
    $env:WEEK3_FAILURE_INJECTION_SUBJECT = $FailureSubject
    docker compose -f $ComposeFile up -d --no-deps --force-recreate worker | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create the opt-in poison-demo worker."
    }
    $poisonWorkerCreated = $true
    Wait-WorkerHealthy

    $dlqBefore = Get-DlqState
    $request = @{
        notifications = @(
            @{
                recipientEmail = "poison-proof@example.test"
                channel = 1
                subject = $FailureSubject
                body = "Exercises three bounded attempts and final dead-letter routing."
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
    } while ($current.status -ne "DeadLettered" -and (Get-Date) -lt $deadline)

    if ($current.status -ne "DeadLettered") {
        throw "Poison job ended in '$($current.status)' instead of DeadLettered."
    }

    do {
        Start-Sleep -Milliseconds 750
        $dlqAfter = Get-DlqState
    } while ($dlqAfter.messages_ready -le $dlqBefore.messages_ready -and (Get-Date) -lt $deadline)

    if ($dlqAfter.messages_ready -le $dlqBefore.messages_ready) {
        throw "The poison command did not reach the DLQ before the deadline."
    }

    $credentials = [Convert]::ToBase64String(
        [Text.Encoding]::ASCII.GetBytes("${RabbitUser}:${RabbitPassword}")
    )
    $getRequest = @{
        count = 100
        ackmode = "ack_requeue_true"
        encoding = "auto"
        truncate = 50000
    } | ConvertTo-Json
    $dlqMessages = Invoke-RestMethod `
        -Uri "$ManagementBaseUrl/api/queues/%2F/nsa.notifications.bulk.dlq/get" `
        -Method Post `
        -Headers @{ Authorization = "Basic $credentials" } `
        -ContentType "application/json" `
        -Body $getRequest
    $matchingMessage = $dlqMessages | Where-Object {
        $_.payload -like "*$($current.jobId)*"
    } | Select-Object -First 1

    if ($null -eq $matchingMessage) {
        throw "The poison job's command could not be found in the DLQ."
    }

    $death = $matchingMessage.properties.headers."x-death" | Select-Object -First 1
    [pscustomobject]@{
        HttpStatus = [int]$acceptedResponse.StatusCode
        JobId = $current.jobId
        ApiStatus = $current.status
        Error = $current.error
        MessageId = $matchingMessage.properties.message_id
        CorrelationId = $current.correlationId
        RetryHeader = $matchingMessage.properties.headers."x-retry-count"
        DeathReason = $death.reason
        DlqMessagesBefore = $dlqBefore.messages_ready
        DlqMessagesAfter = $dlqAfter.messages_ready
    } | Format-List | Out-Host

    docker compose -f $ComposeFile logs --no-color worker |
        Select-String -Pattern "retry|dead-letter" -CaseSensitive:$false |
        Select-Object -Last 8 | Out-Host
}
finally {
    if ($null -eq $previousFailureSubject) {
        Remove-Item Env:WEEK3_FAILURE_INJECTION_SUBJECT -ErrorAction SilentlyContinue
    }
    else {
        $env:WEEK3_FAILURE_INJECTION_SUBJECT = $previousFailureSubject
    }

    if ($poisonWorkerCreated) {
        docker compose -f $ComposeFile up -d --no-deps --force-recreate worker | Out-Host
        Wait-WorkerHealthy
    }
}
