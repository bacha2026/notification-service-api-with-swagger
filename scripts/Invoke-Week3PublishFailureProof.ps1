[CmdletBinding()]
param(
    [string]$ComposeFile = "compose.week3.yml",
    [string]$ApiBaseUrl = "http://localhost:8080",
    [string]$SqlPassword
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Week3Environment.ps1"
$SqlPassword = if ([string]::IsNullOrWhiteSpace($SqlPassword)) {
    Get-Week3EnvironmentValue -Name 'WEEK3_SQL_PASSWORD'
} else { $SqlPassword }
$rabbitStopped = $false

try {
    docker compose -f $ComposeFile stop rabbitmq | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Could not stop the RabbitMQ container."
    }
    $rabbitStopped = $true

    $request = @{
        notifications = @(
            @{
                recipientEmail = "publish-failure-proof@example.test"
                channel = 1
                subject = "Week 3 unavailable broker proof"
                body = "This command must not be reported as accepted."
                orderId = $null
            }
        )
    } | ConvertTo-Json -Depth 5

    try {
        $unexpected = Invoke-WebRequest `
            -UseBasicParsing `
            -Uri "$ApiBaseUrl/api/v2/notifications/bulk" `
            -Method Post `
            -ContentType "application/json" `
            -Body $request
        throw "Expected a 503 response, but the API returned $($unexpected.StatusCode)."
    }
    catch {
        if ($null -eq $_.Exception.Response) {
            throw
        }

        $statusCode = [int]$_.Exception.Response.StatusCode
        if ($_.ErrorDetails.Message) {
            $problemBody = $_.ErrorDetails.Message
        }
        else {
            $reader = [IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
            try {
                $problemBody = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
        }
    }

    if ($statusCode -ne 503) {
        throw "Expected HTTP 503 while RabbitMQ was stopped; received $statusCode."
    }

    $sql = @"
SET NOCOUNT ON;
SELECT TOP (1)
    CONVERT(varchar(36), Id),
    Status,
    Error
FROM BulkNotificationJobs
WHERE Status = 'PublishFailed'
ORDER BY QueuedAtUtc DESC;
"@
    $publishFailedRow = docker compose -f $ComposeFile exec -T sqlserver `
        /opt/mssql-tools18/bin/sqlcmd `
        -S localhost `
        -U sa `
        -P $SqlPassword `
        -d NotificationServiceDb `
        -C `
        -h -1 `
        -W `
        -s "|" `
        -Q $sql

    if ($LASTEXITCODE -ne 0 -or -not ($publishFailedRow -match "PublishFailed")) {
        throw "The SQL-backed PublishFailed state was not found."
    }
}
finally {
    if ($rabbitStopped) {
        docker compose -f $ComposeFile start rabbitmq | Out-Host
    }
}

$recoveryDeadline = (Get-Date).AddSeconds(60)
do {
    Start-Sleep -Seconds 1
    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = "SilentlyContinue"
    $queueJson = docker compose -f $ComposeFile exec -T rabbitmq `
        rabbitmqctl list_queues name consumers --formatter json 2>$null
    $rabbitExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorPreference
    if ($rabbitExitCode -eq 0 -and $queueJson) {
        $mainQueue = ($queueJson | ConvertFrom-Json) | Where-Object {
            $_.name -eq "nsa.notifications.bulk.v1"
        }
    }
} while (($null -eq $mainQueue -or $mainQueue.consumers -lt 1) -and (Get-Date) -lt $recoveryDeadline)

if ($null -eq $mainQueue -or $mainQueue.consumers -lt 1) {
    throw "RabbitMQ restarted, but the worker consumer did not recover before the deadline."
}

[pscustomobject]@{
    HttpStatus = $statusCode
    Problem = $problemBody
    PersistedRow = ($publishFailedRow -join " ").Trim()
    RecoveredConsumers = $mainQueue.consumers
} | Format-List
