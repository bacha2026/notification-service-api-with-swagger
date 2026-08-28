[CmdletBinding()]
param(
    [string]$BaseUrl = "http://127.0.0.1:8080",
    [string]$ComposeProjectName = "nsa-week5",
    [string]$EvidencePath = "",
    [ValidateRange(1, 5000)]
    [int]$MaxAdmissionMilliseconds = 100
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

$apiRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent $apiRoot
if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = Join-Path `
        $repositoryRoot `
        "infra/week5/.runtime/evidence/week4-broker-outage-durability.json"
}
elseif (-not [System.IO.Path]::IsPathRooted($EvidencePath)) {
    $EvidencePath = Join-Path (Get-Location) $EvidencePath
}
$EvidencePath = [System.IO.Path]::GetFullPath($EvidencePath)

$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(20)
$stage = "initialization"
$rabbitContainer = $null
$rabbitWasStoppedByProof = $false
$rabbitStoppedAtUtc = $null
$failureToRethrow = $null

$commitSha = $null
$workingTreeDirty = $null
try {
    $commitOutput = @(git -C $repositoryRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and $commitOutput.Count -eq 1) {
        $commitSha = [string]$commitOutput[0]
        $workingTreeState = @(git -C $repositoryRoot status --porcelain 2>$null)
        $workingTreeDirty = $workingTreeState.Count -gt 0
    }
    else {
        $commitSha = $null
    }
}
catch {
    $commitSha = $null
    $workingTreeDirty = $null
}

$evidence = [ordered]@{
    schemaVersion = 1
    verification = "week4-week5-broker-outage-durability"
    result = "IN_PROGRESS"
    stage = $stage
    startedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    completedAtUtc = $null
    source = [ordered]@{
        commitSha = $commitSha
        workingTreeDirty = $workingTreeDirty
    }
    target = [ordered]@{
        composeProject = $ComposeProjectName
        baseUrl = $BaseUrl
    }
    brokerOutage = [ordered]@{
        stoppedAtUtc = $null
        restartedAtUtc = $null
        durationSeconds = $null
        apiLiveStatus = $null
        apiReadyStatus = $null
    }
    idempotency = [ordered]@{
        initialStatus = $null
        admissionLatencyMilliseconds = $null
        admissionLatencyLimitMilliseconds = $MaxAdmissionMilliseconds
        replayStatus = $null
        replayHeader = $null
        sameJobIdentity = $null
    }
    durablePersistence = [ordered]@{
        jobs = $null
        jobItems = $null
        outboxRows = $null
        unpublishedOutboxRows = $null
        idempotencyReceipts = $null
        effectsBeforeRecovery = $null
    }
    recovery = [ordered]@{
        rabbitHealth = $null
        relayHealth = $null
        workerHealth = $null
        terminalStatus = $null
        totalCount = $null
        processedCount = $null
        succeededCount = $null
        failedCount = $null
        publishedOutboxRows = $null
        inboxReceipts = $null
        producedEffects = $null
    }
    failure = $null
}

function Set-VerificationStage {
    param([Parameter(Mandatory)][string]$Name)

    $script:stage = $Name
    $script:evidence.stage = $Name
}

function Write-SanitizedEvidence {
    $directory = Split-Path -Parent $EvidencePath
    [void][System.IO.Directory]::CreateDirectory($directory)
    [System.IO.File]::WriteAllText(
        $EvidencePath,
        ($evidence | ConvertTo-Json -Depth 8),
        [System.Text.UTF8Encoding]::new($false))
}

function Assert-True {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-Docker {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = @(& docker @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Docker command failed during stage '$stage'."
    }

    return $output
}

function Resolve-ComposeContainer {
    param([Parameter(Mandatory)][string]$Service)

    $containers = @(Invoke-Docker @(
        "ps",
        "--all",
        "--filter", "label=com.docker.compose.project=$ComposeProjectName",
        "--filter", "label=com.docker.compose.service=$Service",
        "--format", "{{.Names}}") |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-True ($containers.Count -eq 1) `
        "Expected one '$Service' container in Compose project '$ComposeProjectName'."
    return [string]$containers[0]
}

function Get-ContainerRunning {
    param([Parameter(Mandatory)][string]$Container)

    $value = (Invoke-Docker @("inspect", "--format", "{{.State.Running}}", $Container) |
        Select-Object -Last 1).Trim()
    return $value -eq "true"
}

function Get-ContainerHealth {
    param([Parameter(Mandatory)][string]$Container)

    return [string]((Invoke-Docker @(
        "inspect",
        "--format",
        "{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}",
        $Container) | Select-Object -Last 1).Trim())
}

function Wait-ContainerHealth {
    param(
        [Parameter(Mandatory)][string]$Container,
        [int]$Attempts = 60
    )

    for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {
        if ((Get-ContainerRunning $Container) -and
            (Get-ContainerHealth $Container) -eq "healthy") {
            return "healthy"
        }

        Start-Sleep -Seconds 2
    }

    throw "Container health did not recover during stage '$stage'."
}

function New-Request {
    param(
        [Parameter(Mandatory)][System.Net.Http.HttpMethod]$Method,
        [Parameter(Mandatory)][string]$Path,
        [object]$Body,
        [string]$IdempotencyKey = ""
    )

    $request = [System.Net.Http.HttpRequestMessage]::new($Method, "$BaseUrl$Path")
    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) {
        [void]$request.Headers.TryAddWithoutValidation(
            "X-Idempotency-Key",
            $IdempotencyKey)
    }
    if ($null -ne $Body) {
        $request.Content = [System.Net.Http.StringContent]::new(
            ($Body | ConvertTo-Json -Depth 8 -Compress),
            [System.Text.Encoding]::UTF8,
            "application/json")
    }

    return $request
}

function Send-Request {
    param([Parameter(Mandatory)][System.Net.Http.HttpRequestMessage]$Request)

    return $client.SendAsync($Request).GetAwaiter().GetResult()
}

function Read-JsonBody {
    param([Parameter(Mandatory)][System.Net.Http.HttpResponseMessage]$Response)

    return $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult() |
        ConvertFrom-Json
}

function Get-HeaderValue {
    param(
        [Parameter(Mandatory)][System.Net.Http.HttpResponseMessage]$Response,
        [Parameter(Mandatory)][string]$Name
    )

    try {
        return [string]($Response.Headers.GetValues($Name) | Select-Object -First 1)
    }
    catch {
        return $null
    }
}

function New-BulkRequest {
    param(
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][string]$Recipient
    )

    return New-Request `
        -Method ([System.Net.Http.HttpMethod]::Post) `
        -Path "/api/v2/notifications/bulk" `
        -IdempotencyKey $Key `
        -Body @{
            notifications = @(
                @{
                    recipientEmail = $Recipient
                    channel = 1
                    subject = "Broker outage durable admission"
                    body = "Week 4 broker outage durability proof."
                    orderId = $null
                }
            )
        }
}

function Invoke-DatabaseQuery {
    param(
        [Parameter(Mandatory)][string]$SqlContainer,
        [Parameter(Mandatory)][string]$Sql
    )

    $sql64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Sql))
    $containerScript = 'export SQLCMDPASSWORD="$(tr -d ''\r\n'' </run/secrets/sql_sa_password)"; '
    $containerScript += 'echo ' + $sql64
    $containerScript += ' | base64 -d | /opt/mssql-tools18/bin/sqlcmd'
    $containerScript += ' -S localhost -U sa'
    $containerScript += ' -C -d NotificationServiceDb -h -1 -W -s "|"'
    $script64 = [Convert]::ToBase64String(
        [System.Text.Encoding]::UTF8.GetBytes($containerScript))
    $launcher = "echo $script64|base64 -d|bash"
    $output = @(Invoke-Docker @("exec", $SqlContainer, "bash", "-lc", $launcher))
    return @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-DelimitedValues {
    param([Parameter(Mandatory)][string]$Line)

    return @($Line.Split("|") | ForEach-Object { $_.Trim() })
}

function Wait-JobCompletion {
    param([Parameter(Mandatory)][Guid]$JobId)

    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        $response = Send-Request (New-Request `
            -Method ([System.Net.Http.HttpMethod]::Get) `
            -Path "/api/v2/notifications/bulk/$JobId")
        if ([int]$response.StatusCode -eq 200) {
            $body = Read-JsonBody $response
            if ($body.status -eq "Completed") {
                return $body
            }
            if ($body.status -eq "Failed") {
                throw "The verification job reached a failed terminal state."
            }
        }

        Start-Sleep -Seconds 2
    }

    throw "The verification job did not complete within the bounded wait."
}

try {
    Set-VerificationStage "validate-existing-stack"
    $rabbitContainer = Resolve-ComposeContainer "rabbitmq"
    $sqlContainer = Resolve-ComposeContainer "sqlserver"
    $apiContainer = Resolve-ComposeContainer "api"
    $relayContainer = Resolve-ComposeContainer "outbox-relay"
    $workerContainer = Resolve-ComposeContainer "worker"

    foreach ($container in @(
        $rabbitContainer,
        $sqlContainer,
        $apiContainer,
        $relayContainer,
        $workerContainer)) {
        Assert-True (Get-ContainerRunning $container) `
            "A required existing container is not running."
        Assert-True ((Get-ContainerHealth $container) -eq "healthy") `
            "A required existing container is not healthy."
    }

    $liveBefore = Send-Request (New-Request `
        -Method ([System.Net.Http.HttpMethod]::Get) `
        -Path "/health/live")
    Assert-True ([int]$liveBefore.StatusCode -eq 200) `
        "The existing API is not live."

    Set-VerificationStage "stop-only-rabbitmq"
    [void](Invoke-Docker @("stop", "--time", "30", $rabbitContainer))
    $rabbitWasStoppedByProof = $true
    $rabbitStoppedAtUtc = [DateTimeOffset]::UtcNow
    $evidence.brokerOutage.stoppedAtUtc = $rabbitStoppedAtUtc.ToString("O")
    Assert-True (-not (Get-ContainerRunning $rabbitContainer)) `
        "RabbitMQ remained running after the scoped stop."

    Set-VerificationStage "admit-and-replay-during-outage"
    $live = Send-Request (New-Request `
        -Method ([System.Net.Http.HttpMethod]::Get) `
        -Path "/health/live")
    $ready = Send-Request (New-Request `
        -Method ([System.Net.Http.HttpMethod]::Get) `
        -Path "/health/ready")
    Assert-True ([int]$live.StatusCode -eq 200) `
        "API liveness failed during the broker outage."
    Assert-True ([int]$ready.StatusCode -eq 200) `
        "API readiness unexpectedly depended on RabbitMQ."
    $evidence.brokerOutage.apiLiveStatus = [int]$live.StatusCode
    $evidence.brokerOutage.apiReadyStatus = [int]$ready.StatusCode

    $suffix = [Guid]::NewGuid().ToString("N").Substring(0, 12)
    $idempotencyKey = [Guid]::NewGuid().ToString("D")
    $recipient = "week4-outage-$suffix@example.test"

    $admissionTimer = [System.Diagnostics.Stopwatch]::StartNew()
    $first = Send-Request (New-BulkRequest `
        -Key $idempotencyKey `
        -Recipient $recipient)
    $admissionTimer.Stop()
    $admissionLatencyMilliseconds = [Math]::Round(
        $admissionTimer.Elapsed.TotalMilliseconds,
        3)
    $firstBody = Read-JsonBody $first
    $replay = Send-Request (New-BulkRequest `
        -Key $idempotencyKey `
        -Recipient $recipient)
    $replayBody = Read-JsonBody $replay
    $replayHeader = Get-HeaderValue $replay "Idempotency-Replayed"

    $evidence.idempotency.initialStatus = [int]$first.StatusCode
    $evidence.idempotency.admissionLatencyMilliseconds = `
        $admissionLatencyMilliseconds
    Assert-True ([int]$first.StatusCode -eq 202) `
        "The broker-outage admission was not accepted."
    Assert-True ($admissionLatencyMilliseconds -lt $MaxAdmissionMilliseconds) `
        "Admission exceeded the configured latency limit."
    Assert-True ([int]$replay.StatusCode -eq 202) `
        "The broker-outage replay was not accepted."
    Assert-True ($replayHeader -eq "true") `
        "The replay marker was not returned."
    Assert-True ($firstBody.jobId -eq $replayBody.jobId) `
        "The idempotent replay returned a different job identity."

    $evidence.idempotency.replayStatus = [int]$replay.StatusCode
    $evidence.idempotency.replayHeader = $replayHeader
    $evidence.idempotency.sameJobIdentity = $true
    $jobId = [Guid]$firstBody.jobId

    Set-VerificationStage "verify-durable-sql-boundary"
    $outageRows = @(Invoke-DatabaseQuery `
        -SqlContainer $sqlContainer `
        -Sql @"
SET NOCOUNT ON;
SELECT
  (SELECT COUNT(*) FROM BulkNotificationJobs WHERE Id = '$jobId'),
  (SELECT COUNT(*) FROM BulkNotificationJobItems WHERE JobId = '$jobId'),
  (SELECT COUNT(*) FROM OutboxMessages WHERE AggregateId = '$jobId'),
  (SELECT COUNT(*) FROM OutboxMessages WHERE AggregateId = '$jobId' AND PublishedAtUtc IS NULL),
  (SELECT COUNT(*) FROM IdempotencyRecords WHERE ResourceId = '$jobId'),
  (SELECT COUNT(*) FROM Notifications WHERE BulkNotificationJobId = '$jobId');
"@)
    Assert-True ($outageRows.Count -gt 0) `
        "No SQL durability evidence was returned."
    $outage = Get-DelimitedValues $outageRows[-1]
    Assert-True ($outage.Count -eq 6) `
        "The SQL durability evidence could not be parsed."
    Assert-True (
        $outage[0] -eq "1" -and
        $outage[1] -eq "1" -and
        $outage[2] -eq "1" -and
        $outage[3] -eq "1" -and
        $outage[4] -eq "1" -and
        $outage[5] -eq "0") `
        "The broker-outage transaction was not durably and uniquely persisted."

    $evidence.durablePersistence.jobs = [int]$outage[0]
    $evidence.durablePersistence.jobItems = [int]$outage[1]
    $evidence.durablePersistence.outboxRows = [int]$outage[2]
    $evidence.durablePersistence.unpublishedOutboxRows = [int]$outage[3]
    $evidence.durablePersistence.idempotencyReceipts = [int]$outage[4]
    $evidence.durablePersistence.effectsBeforeRecovery = [int]$outage[5]

    Set-VerificationStage "restart-rabbitmq"
    [void](Invoke-Docker @("start", $rabbitContainer))
    $rabbitWasStoppedByProof = $false
    $rabbitRestartedAtUtc = [DateTimeOffset]::UtcNow
    $evidence.brokerOutage.restartedAtUtc = $rabbitRestartedAtUtc.ToString("O")
    $evidence.brokerOutage.durationSeconds = [Math]::Round(
        ($rabbitRestartedAtUtc - $rabbitStoppedAtUtc).TotalSeconds,
        3)

    Set-VerificationStage "wait-for-service-recovery"
    $evidence.recovery.rabbitHealth = Wait-ContainerHealth $rabbitContainer
    $evidence.recovery.relayHealth = Wait-ContainerHealth $relayContainer
    $evidence.recovery.workerHealth = Wait-ContainerHealth $workerContainer

    Set-VerificationStage "verify-terminal-completion"
    $completed = Wait-JobCompletion $jobId
    Assert-True (
        [int]$completed.totalCount -eq 1 -and
        [int]$completed.processedCount -eq 1 -and
        [int]$completed.succeededCount -eq 1 -and
        [int]$completed.failedCount -eq 0) `
        "The recovered job did not produce exactly one successful effect."

    $evidence.recovery.terminalStatus = [string]$completed.status
    $evidence.recovery.totalCount = [int]$completed.totalCount
    $evidence.recovery.processedCount = [int]$completed.processedCount
    $evidence.recovery.succeededCount = [int]$completed.succeededCount
    $evidence.recovery.failedCount = [int]$completed.failedCount

    $recoveryRows = @(Invoke-DatabaseQuery `
        -SqlContainer $sqlContainer `
        -Sql @"
SET NOCOUNT ON;
SELECT
  (SELECT COUNT(*) FROM OutboxMessages WHERE AggregateId = '$jobId' AND PublishedAtUtc IS NOT NULL),
  (SELECT COUNT(*) FROM InboxMessages i INNER JOIN OutboxMessages o ON o.Id = i.MessageId WHERE o.AggregateId = '$jobId' AND i.Handler = o.MessageType),
  (SELECT COUNT(*) FROM Notifications WHERE BulkNotificationJobId = '$jobId');
"@)
    Assert-True ($recoveryRows.Count -gt 0) `
        "No SQL recovery evidence was returned."
    $recovery = Get-DelimitedValues $recoveryRows[-1]
    Assert-True (
        $recovery.Count -eq 3 -and
        $recovery[0] -eq "1" -and
        $recovery[1] -eq "1" -and
        $recovery[2] -eq "1") `
        "The recovered relay/consumer path was not published and deduplicated once."

    $evidence.recovery.publishedOutboxRows = [int]$recovery[0]
    $evidence.recovery.inboxReceipts = [int]$recovery[1]
    $evidence.recovery.producedEffects = [int]$recovery[2]
    Set-VerificationStage "complete"
    $evidence.result = "PASS"
}
catch {
    $failureToRethrow = $_
    $evidence.result = "FAIL"
    $evidence.failure = [ordered]@{
        stage = $stage
        exceptionType = $_.Exception.GetType().FullName
    }
}
finally {
    if ($rabbitWasStoppedByProof -and -not [string]::IsNullOrWhiteSpace($rabbitContainer)) {
        try {
            Set-VerificationStage "restore-rabbitmq-after-failure"
            [void](Invoke-Docker @("start", $rabbitContainer))
            $rabbitWasStoppedByProof = $false
            $rabbitRestartedAtUtc = [DateTimeOffset]::UtcNow
            $evidence.brokerOutage.restartedAtUtc = $rabbitRestartedAtUtc.ToString("O")
            if ($null -ne $rabbitStoppedAtUtc) {
                $evidence.brokerOutage.durationSeconds = [Math]::Round(
                    ($rabbitRestartedAtUtc - $rabbitStoppedAtUtc).TotalSeconds,
                    3)
            }
            $evidence.recovery.rabbitHealth = Wait-ContainerHealth $rabbitContainer
        }
        catch {
            $evidence.result = "FAIL"
            $evidence.failure = [ordered]@{
                stage = "restore-rabbitmq-after-failure"
                exceptionType = $_.Exception.GetType().FullName
            }
            if ($null -eq $failureToRethrow) {
                $failureToRethrow = $_
            }
        }
    }

    $evidence.completedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    Write-SanitizedEvidence
    $client.Dispose()
}

if ($null -ne $failureToRethrow) {
    throw $failureToRethrow
}

Write-Output "Week 4 Week 5 broker-outage durability proof: PASS"
Write-Output "Admission/replay status: 202/202; same identity: true"
Write-Output "Admission latency: $($evidence.idempotency.admissionLatencyMilliseconds) ms (< $MaxAdmissionMilliseconds ms)"
Write-Output "Durable outage rows: job=1, item=1, outbox=1 unpublished, receipt=1, effects=0"
Write-Output "Recovered rows: completed=1, published=1, inbox=1, effects=1"
Write-Output "Sanitized evidence: $EvidencePath"
