[CmdletBinding()]
param(
    [string]$ComposeFile = "compose.week3.yml",
    [string]$ManagementBaseUrl = "http://localhost:15672",
    [string]$RabbitUser,
    [string]$RabbitPassword,
    [ValidateSet("MalformedJson", "UnsupportedSchema", "WrongMessageType")]
    [string]$MessageCase = "MalformedJson"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Week3Environment.ps1"
$RabbitUser = if ([string]::IsNullOrWhiteSpace($RabbitUser)) {
    Get-Week3EnvironmentValue -Name 'WEEK3_RABBITMQ_USERNAME'
} else { $RabbitUser }
$RabbitPassword = if ([string]::IsNullOrWhiteSpace($RabbitPassword)) {
    Get-Week3EnvironmentValue -Name 'WEEK3_RABBITMQ_PASSWORD'
} else { $RabbitPassword }

function Get-DlqState {
    $json = docker compose -f $ComposeFile exec -T rabbitmq `
        rabbitmqctl list_queues name messages_ready messages_unacknowledged consumers --formatter json

    if ($LASTEXITCODE -ne 0) {
        throw "Could not read the RabbitMQ queue state."
    }

    $queues = $json | ConvertFrom-Json
    return ($queues | Where-Object {
        $_.name -eq "nsa.notifications.bulk.dlq"
    })
}

$dlqBefore = Get-DlqState
$messageId = [Guid]::NewGuid().ToString()
$correlationId = "week3-$($MessageCase.ToLowerInvariant())-$([Guid]::NewGuid().ToString('N'))"
$credentials = [Convert]::ToBase64String(
    [Text.Encoding]::ASCII.GetBytes("${RabbitUser}:${RabbitPassword}")
)

$payload = if ($MessageCase -eq "MalformedJson") {
    "{ this-is-intentionally-not-json"
}
else {
    @{
        schemaVersion = if ($MessageCase -eq "UnsupportedSchema") { 99 } else { 1 }
        messageId = $messageId
        jobId = [Guid]::NewGuid().ToString()
        correlationId = $correlationId
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    } | ConvertTo-Json -Compress
}

$publishRequest = @{
    properties = @{
        content_type = "application/json"
        delivery_mode = 2
        type = if ($MessageCase -eq "WrongMessageType") {
            "nsa.notifications.unsupported.v1"
        } else {
            "nsa.notifications.bulk-requested.v1"
        }
        message_id = $messageId
        correlation_id = $correlationId
    }
    routing_key = "bulk.requested.v1"
    payload = $payload
    payload_encoding = "string"
} | ConvertTo-Json -Depth 5

$publishResult = Invoke-RestMethod `
    -Uri "$ManagementBaseUrl/api/exchanges/%2F/nsa.notifications.commands.v1/publish" `
    -Method Post `
    -Headers @{ Authorization = "Basic $credentials" } `
    -ContentType "application/json" `
    -Body $publishRequest

if (-not $publishResult.routed) {
    throw "RabbitMQ did not route the malformed verification message."
}

$deadline = (Get-Date).AddSeconds(30)
do {
    Start-Sleep -Milliseconds 750
    $dlqAfter = Get-DlqState
} while ($dlqAfter.messages_ready -le $dlqBefore.messages_ready -and (Get-Date) -lt $deadline)

if ($dlqAfter.messages_ready -le $dlqBefore.messages_ready) {
    throw "The malformed message did not arrive in the DLQ before the deadline."
}

[pscustomobject]@{
    Case = $MessageCase
    Routed = $publishResult.routed
    MessageId = $messageId
    CorrelationId = $correlationId
    DlqMessagesBefore = $dlqBefore.messages_ready
    DlqMessagesAfter = $dlqAfter.messages_ready
    DlqUnacknowledged = $dlqAfter.messages_unacknowledged
} | Format-List
