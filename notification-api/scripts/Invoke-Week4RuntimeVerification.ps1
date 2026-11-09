[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:8081",
    [string[]]$ComposeFiles = @(),
    [string]$EnvironmentFile = "",
    [string]$ComposeProjectName = "nsa-week3",
    [string]$EvidencePath = "",
    [ValidateRange(1, 5000)]
    [int]$MaxAdmissionMilliseconds = 100
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$apiRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent $apiRoot
if ($ComposeFiles.Count -eq 0) {
    $ComposeFiles = @(Join-Path $apiRoot "compose.week3.yml")
}
if ([string]::IsNullOrWhiteSpace($EnvironmentFile)) {
    $EnvironmentFile = Join-Path $apiRoot ".env"
}
$compose = @("--env-file", $EnvironmentFile, "--project-name", $ComposeProjectName)
foreach ($file in $ComposeFiles) {
    $compose += @("-f", $file)
}
$client = [System.Net.Http.HttpClient]::new()
$rabbitStarted = $true
$rabbitStoppedAtUtc = $null
$stage = "initialization"

if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
    $EvidencePath = Join-Path $repositoryRoot "artifacts/week4/runtime-verification.json"
}
elseif (-not [System.IO.Path]::IsPathRooted($EvidencePath)) {
    $EvidencePath = Join-Path (Get-Location) $EvidencePath
}
$EvidencePath = [System.IO.Path]::GetFullPath($EvidencePath)

$commitSha = $null
$workingTreeDirty = $null
try {
    $commitSha = git -C $repositoryRoot rev-parse HEAD 2>$null | Select-Object -First 1
    if ($LASTEXITCODE -ne 0) {
        $commitSha = $null
    }
    else {
        $workingTreeState = @(git -C $repositoryRoot status --porcelain 2>$null)
        $workingTreeDirty = $workingTreeState.Count -gt 0
    }
}
catch {
    $commitSha = $null
    $workingTreeDirty = $null
}

$evidence = [ordered]@{
    schemaVersion = 1
    verification = "week4-idempotency-outbox"
    result = "IN_PROGRESS"
    stage = $stage
    startedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    completedAtUtc = $null
    source = [ordered]@{
        commitSha = $commitSha
        workingTreeDirty = $workingTreeDirty
    }
    apiDuringBrokerOutage = [ordered]@{
        liveStatus = $null
        readyStatus = $null
    }
    idempotency = [ordered]@{
        initialStatus = $null
        admissionLatencyMilliseconds = $null
        admissionLatencyLimitMilliseconds = $MaxAdmissionMilliseconds
        replayStatus = $null
        conflictStatus = $null
        concurrentStatuses = @()
        persistedJobs = $null
        persistedOutboxRows = $null
        persistedReceipts = $null
    }
    relayRecovery = [ordered]@{
        rabbitStoppedAtUtc = $null
        rabbitRestartedAtUtc = $null
        outageDurationSeconds = $null
        completedJobs = $null
        publishedOutboxRows = $null
        inboxReceipts = $null
        producedEffects = $null
    }
    database = [ordered]@{
        latestMigration = $null
        prematureNotifications = $null
    }
    authenticatedRegression = [ordered]@{
        checkoutStatus = $null
        checkoutReplayStatus = $null
        notificationReadPersisted = $null
        concurrentDeleteStatuses = @()
        remainingGraphCounts = @()
        retainedInboxIdempotencyOrder = @()
    }
    failure = $null
}

function Set-VerificationStage {
    param([string]$Name)

    $script:stage = $Name
    $script:evidence.stage = $Name
}

function Write-SanitizedEvidence {
    param([System.Collections.IDictionary]$Value)

    $directory = Split-Path -Parent $EvidencePath
    [void][System.IO.Directory]::CreateDirectory($directory)
    $json = $Value | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText(
        $EvidencePath,
        $json,
        [System.Text.UTF8Encoding]::new($false))
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Read-ResponseBody {
    param([System.Net.Http.HttpResponseMessage]$Response)

    return $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
}

function New-Request {
    param(
        [System.Net.Http.HttpMethod]$Method,
        [string]$Path,
        [object]$Body,
        [string]$AccessToken,
        [string]$IdempotencyKey
    )

    $request = [System.Net.Http.HttpRequestMessage]::new($Method, "$BaseUrl$Path")
    if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
        [void]$request.Headers.TryAddWithoutValidation("Authorization", "Bearer $AccessToken")
    }

    if (-not [string]::IsNullOrWhiteSpace($IdempotencyKey)) {
        [void]$request.Headers.TryAddWithoutValidation("X-Idempotency-Key", $IdempotencyKey)
    }

    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 8 -Compress
        $request.Content = [System.Net.Http.StringContent]::new(
            $json,
            [System.Text.Encoding]::UTF8,
            "application/json")
    }

    return $request
}

function New-BulkRequest {
    param(
        [string]$Key,
        [string]$Recipient,
        [string]$Subject
    )

    return New-Request `
        -Method ([System.Net.Http.HttpMethod]::Post) `
        -Path "/api/v2/notifications/bulk" `
        -Body @{
            notifications = @(
                @{
                    recipientEmail = $Recipient
                    channel = 1
                    subject = $Subject
                    body = "Week 4 runtime durability verification."
                    orderId = $null
                }
            )
        } `
        -AccessToken "" `
        -IdempotencyKey $Key
}

function Send-Request {
    param([System.Net.Http.HttpRequestMessage]$Request)

    return $client.SendAsync($Request).GetAwaiter().GetResult()
}

function Invoke-DatabaseQuery {
    param([string]$Sql)

    $sql64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Sql))
    $containerScript = 'echo ' + $sql64
    $containerScript += ' | base64 -d | /opt/mssql-tools18/bin/sqlcmd'
    $containerScript += ' -S localhost -U sa -P "$MSSQL_SA_PASSWORD"'
    $containerScript += ' -C -d NotificationServiceDb -h -1 -W -s "|"'
    $script64 = [Convert]::ToBase64String(
        [System.Text.Encoding]::UTF8.GetBytes($containerScript))
    $launcher = "echo $script64|base64 -d|bash"
    $output = docker compose @compose exec -T sqlserver bash -lc $launcher
    if ($LASTEXITCODE -ne 0) {
        throw "SQL verification failed."
    }

    return @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-DelimitedValues {
    param([string]$Line)

    return @($Line.Split("|") | ForEach-Object { $_.Trim() })
}

function Wait-ContainerHealth {
    param(
        [string]$Container,
        [string]$Expected,
        [int]$Attempts = 40
    )

    for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {
        $state = docker inspect --format "{{.State.Health.Status}}" $Container
        if ($state -eq $Expected) {
            return $true
        }

        Start-Sleep -Seconds 2
    }

    return $false
}

function Wait-JobCompleted {
    param([Guid]$JobId)

    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        $response = Send-Request (New-Request `
            -Method ([System.Net.Http.HttpMethod]::Get) `
            -Path "/api/v2/notifications/bulk/$JobId" `
            -Body $null `
            -AccessToken "" `
            -IdempotencyKey "")
        if ([int]$response.StatusCode -eq 200) {
            $body = Read-ResponseBody $response | ConvertFrom-Json
            if ($body.status -eq "Completed") {
                return
            }
        }

        Start-Sleep -Seconds 2
    }

    throw "Job $JobId did not complete within the verification window."
}

function Get-RequiredHeader {
    param(
        [System.Net.Http.HttpResponseMessage]$Response,
        [string]$Name
    )

    return [string]($Response.Headers.GetValues($Name) | Select-Object -First 1)
}

try {
    Set-VerificationStage "prerequisites"
    Assert-True (Test-Path -LiteralPath $EnvironmentFile) "Create the configured environment file before running this verification."

    $liveBefore = Send-Request (New-Request `
        -Method ([System.Net.Http.HttpMethod]::Get) `
        -Path "/health/live" `
        -Body $null `
        -AccessToken "" `
        -IdempotencyKey "")
    Assert-True ([int]$liveBefore.StatusCode -eq 200) "The API is not running at $BaseUrl."

    # Warm the HTTP, EF Core, and SQL admission path before enforcing the
    # deployment SLO. The measured request below still runs while RabbitMQ is
    # unavailable, so it also proves broker I/O is not on the response path.
    Set-VerificationStage "warm-admission-path"
    $warmupKey = [Guid]::NewGuid().ToString("D")
    $warmup = Send-Request (New-BulkRequest `
        -Key $warmupKey `
        -Recipient "week4-warmup-$($warmupKey.Substring(0, 8))@example.test" `
        -Subject "Warm durable admission path")
    Assert-True ([int]$warmup.StatusCode -eq 202) "The admission warm-up request failed."
    $warmupBody = Read-ResponseBody $warmup | ConvertFrom-Json
    Wait-JobCompleted ([Guid]$warmupBody.jobId)

    Set-VerificationStage "stop-rabbitmq"
    docker compose @compose stop rabbitmq | Out-Host
    Assert-True ($LASTEXITCODE -eq 0) "Could not stop RabbitMQ."
    $rabbitStarted = $false
    $rabbitStoppedAtUtc = [DateTimeOffset]::UtcNow
    $evidence.relayRecovery.rabbitStoppedAtUtc = $rabbitStoppedAtUtc.ToString("O")

    Set-VerificationStage "verify-api-during-broker-outage"
    $live = Send-Request (New-Request `
        -Method ([System.Net.Http.HttpMethod]::Get) `
        -Path "/health/live" `
        -Body $null `
        -AccessToken "" `
        -IdempotencyKey "")
    $ready = Send-Request (New-Request `
        -Method ([System.Net.Http.HttpMethod]::Get) `
        -Path "/health/ready" `
        -Body $null `
        -AccessToken "" `
        -IdempotencyKey "")
    Assert-True ([int]$live.StatusCode -eq 200) "API liveness failed during the broker outage."
    Assert-True ([int]$ready.StatusCode -eq 200) "API readiness incorrectly depends on RabbitMQ."
    $evidence.apiDuringBrokerOutage.liveStatus = [int]$live.StatusCode
    $evidence.apiDuringBrokerOutage.readyStatus = [int]$ready.StatusCode

    Set-VerificationStage "verify-idempotent-admission"
    $suffix = [Guid]::NewGuid().ToString("N").Substring(0, 12)
    $firstKey = [Guid]::NewGuid().ToString("D")
    $firstRecipient = "week4-outage-$suffix@example.test"
    $admissionTimer = [System.Diagnostics.Stopwatch]::StartNew()
    $first = Send-Request (New-BulkRequest `
        -Key $firstKey `
        -Recipient $firstRecipient `
        -Subject "Broker outage durable admission")
    $admissionTimer.Stop()
    $admissionLatencyMilliseconds = [Math]::Round(
        $admissionTimer.Elapsed.TotalMilliseconds,
        3)
    $firstBody = Read-ResponseBody $first | ConvertFrom-Json
    Assert-True ([int]$first.StatusCode -eq 202) "Broker-outage admission was not accepted."
    Assert-True ($admissionLatencyMilliseconds -lt $MaxAdmissionMilliseconds) `
        "Admission exceeded the configured $MaxAdmissionMilliseconds ms latency limit."

    $replay = Send-Request (New-BulkRequest `
        -Key $firstKey `
        -Recipient $firstRecipient `
        -Subject "Broker outage durable admission")
    $replayBody = Read-ResponseBody $replay | ConvertFrom-Json
    Assert-True ([int]$replay.StatusCode -eq 202) "The idempotent replay was not accepted."
    Assert-True ((Get-RequiredHeader $replay "Idempotency-Replayed") -eq "true") `
        "The replay marker is missing."
    Assert-True ($firstBody.jobId -eq $replayBody.jobId) "The replay returned a different job."

    $conflict = Send-Request (New-BulkRequest `
        -Key $firstKey `
        -Recipient $firstRecipient `
        -Subject "Changed payload must conflict")
    $conflictBody = Read-ResponseBody $conflict | ConvertFrom-Json
    Assert-True ([int]$conflict.StatusCode -eq 409) "Changed payload did not return 409."
    Assert-True ($conflict.Content.Headers.ContentType.MediaType -eq "application/problem+json") `
        "The conflict did not use Problem Details."
    Assert-True ($conflictBody.status -eq 409) "The Problem Details status is incorrect."
    $evidence.idempotency.initialStatus = [int]$first.StatusCode
    $evidence.idempotency.admissionLatencyMilliseconds = $admissionLatencyMilliseconds
    $evidence.idempotency.replayStatus = [int]$replay.StatusCode
    $evidence.idempotency.conflictStatus = [int]$conflict.StatusCode

    Set-VerificationStage "verify-concurrent-idempotency"
    $concurrentKey = [Guid]::NewGuid().ToString("D")
    $concurrentRecipient = "week4-concurrent-$suffix@example.test"
    $firstTask = $client.SendAsync((New-BulkRequest `
        -Key $concurrentKey `
        -Recipient $concurrentRecipient `
        -Subject "Concurrent durable admission"))
    $secondTask = $client.SendAsync((New-BulkRequest `
        -Key $concurrentKey `
        -Recipient $concurrentRecipient `
        -Subject "Concurrent durable admission"))
    $firstConcurrent = $firstTask.GetAwaiter().GetResult()
    $secondConcurrent = $secondTask.GetAwaiter().GetResult()
    $firstConcurrentBody = Read-ResponseBody $firstConcurrent | ConvertFrom-Json
    $secondConcurrentBody = Read-ResponseBody $secondConcurrent | ConvertFrom-Json
    Assert-True (
        [int]$firstConcurrent.StatusCode -eq 202 -and
        [int]$secondConcurrent.StatusCode -eq 202) `
        "Concurrent retries were not both accepted deterministically."
    Assert-True ($firstConcurrentBody.jobId -eq $secondConcurrentBody.jobId) `
        "Concurrent retries created different jobs."
    $evidence.idempotency.concurrentStatuses = @(
        [int]$firstConcurrent.StatusCode,
        [int]$secondConcurrent.StatusCode)

    Set-VerificationStage "inspect-outage-persistence"
    $firstJobId = [Guid]$firstBody.jobId
    $concurrentJobId = [Guid]$firstConcurrentBody.jobId
    $beforeRows = @(Invoke-DatabaseQuery @"
SET NOCOUNT ON;
SELECT
  (SELECT COUNT(*) FROM BulkNotificationJobs WHERE Id IN ('$firstJobId', '$concurrentJobId')),
  (SELECT COUNT(*) FROM OutboxMessages WHERE AggregateId IN ('$firstJobId', '$concurrentJobId')),
  (SELECT COUNT(*) FROM IdempotencyRecords WHERE ResourceId IN ('$firstJobId', '$concurrentJobId')),
  (SELECT COUNT(*) FROM Notifications WHERE BulkNotificationJobId IN ('$firstJobId', '$concurrentJobId'));
"@)
    $before = Get-DelimitedValues $beforeRows[-1]
    Assert-True ($before.Count -eq 4) "Could not parse pre-recovery SQL evidence."
    Assert-True ($before[0] -eq "2") "Expected exactly two persisted jobs."
    Assert-True ($before[1] -eq "2") "Expected exactly two persisted Outbox rows."
    Assert-True ($before[2] -eq "2") "Expected exactly two idempotency receipts."
    Assert-True ($before[3] -eq "0") "A notification was exposed before processing completed."
    $evidence.idempotency.persistedJobs = [int]$before[0]
    $evidence.idempotency.persistedOutboxRows = [int]$before[1]
    $evidence.idempotency.persistedReceipts = [int]$before[2]
    $evidence.database.prematureNotifications = [int]$before[3]

    Set-VerificationStage "verify-relay-outage-readiness"
    Assert-True (Wait-ContainerHealth `
        -Container "$ComposeProjectName-outbox-relay-1" `
        -Expected "unhealthy" `
        -Attempts 20) `
        "Relay readiness did not detect the broker outage."

    Set-VerificationStage "restart-rabbitmq"
    docker compose @compose start rabbitmq | Out-Host
    Assert-True ($LASTEXITCODE -eq 0) "Could not restart RabbitMQ."
    $rabbitStarted = $true
    $rabbitRestartedAtUtc = [DateTimeOffset]::UtcNow
    $evidence.relayRecovery.rabbitRestartedAtUtc = $rabbitRestartedAtUtc.ToString("O")
    if ($null -ne $rabbitStoppedAtUtc) {
        $evidence.relayRecovery.outageDurationSeconds = [Math]::Round(
            ($rabbitRestartedAtUtc - $rabbitStoppedAtUtc).TotalSeconds,
            3)
    }
    Assert-True (Wait-ContainerHealth `
        -Container "$ComposeProjectName-rabbitmq-1" `
        -Expected "healthy") `
        "RabbitMQ did not recover."

    Set-VerificationStage "verify-relay-recovery"
    Wait-JobCompleted $firstJobId
    Wait-JobCompleted $concurrentJobId
    $completedRows = @(Invoke-DatabaseQuery @"
SET NOCOUNT ON;
SELECT j.Id, j.Status, i.Status,
       CASE WHEN o.PublishedAtUtc IS NULL THEN 0 ELSE 1 END,
       CASE WHEN ib.MessageId IS NULL THEN 0 ELSE 1 END,
       COUNT(n.Id)
FROM BulkNotificationJobs j
JOIN BulkNotificationJobItems i ON i.JobId = j.Id
JOIN OutboxMessages o ON o.AggregateId = CONVERT(nvarchar(36), j.Id)
LEFT JOIN InboxMessages ib ON ib.MessageId = o.Id AND ib.Handler = o.MessageType
LEFT JOIN Notifications n ON n.BulkNotificationJobItemId = i.Id
WHERE j.Id IN ('$firstJobId', '$concurrentJobId')
GROUP BY j.Id, j.Status, i.Status, o.PublishedAtUtc, ib.MessageId;
"@)
    Assert-True ($completedRows.Count -eq 2) "Expected two completed SQL evidence rows."
    foreach ($line in $completedRows) {
        $values = Get-DelimitedValues $line
        Assert-True (
            $values.Count -eq 6 -and
            $values[1] -eq "Completed" -and
            $values[2] -eq "Succeeded" -and
            $values[3] -eq "1" -and
            $values[4] -eq "1" -and
            $values[5] -eq "1") `
            "A recovered command did not produce one published, deduplicated effect."
    }
    $evidence.relayRecovery.completedJobs = $completedRows.Count
    $evidence.relayRecovery.publishedOutboxRows = $completedRows.Count
    $evidence.relayRecovery.inboxReceipts = $completedRows.Count
    $evidence.relayRecovery.producedEffects = $completedRows.Count

    $migrationRows = @(Invoke-DatabaseQuery @"
SET NOCOUNT ON;
SELECT TOP (1) MigrationId
FROM __EFMigrationsHistory
ORDER BY MigrationId DESC;
"@)
    Assert-True ($migrationRows.Count -gt 0) "Could not read the applied EF Core migration."
    $evidence.database.latestMigration = $migrationRows[-1].Trim()

    Set-VerificationStage "verify-authenticated-regression"
    $accountEmail = "week4-auth-$suffix@example.test"
    $accountPassword = "W4!$([Guid]::NewGuid().ToString('N'))aA9"
    $signup = Send-Request (New-Request `
        -Method ([System.Net.Http.HttpMethod]::Post) `
        -Path "/api/v2/auth/signup" `
        -Body @{
            email = $accountEmail
            password = $accountPassword
            firstName = "Week"
            lastName = "Four"
            gender = 1
        } `
        -AccessToken "" `
        -IdempotencyKey "")
    Assert-True ([int]$signup.StatusCode -eq 201) "The runtime account could not be created."
    $accessToken = (Read-ResponseBody $signup | ConvertFrom-Json).accessToken

    $cart = Send-Request (New-Request `
        -Method ([System.Net.Http.HttpMethod]::Post) `
        -Path "/api/v2/auth/cart/items" `
        -Body @{ productId = 1; quantity = 1 } `
        -AccessToken $accessToken `
        -IdempotencyKey "")
    Assert-True ([int]$cart.StatusCode -eq 200) "The authenticated cart item could not be added."

    $orderKey = [Guid]::NewGuid().ToString("D")
    $order = Send-Request (New-Request `
        -Method ([System.Net.Http.HttpMethod]::Post) `
        -Path "/api/v2/auth/orders" `
        -Body $null `
        -AccessToken $accessToken `
        -IdempotencyKey $orderKey)
    $orderReplay = Send-Request (New-Request `
        -Method ([System.Net.Http.HttpMethod]::Post) `
        -Path "/api/v2/auth/orders" `
        -Body $null `
        -AccessToken $accessToken `
        -IdempotencyKey $orderKey)
    Assert-True ([int]$order.StatusCode -eq 201) "Authenticated checkout failed."
    Assert-True ([int]$orderReplay.StatusCode -eq 201) "Authenticated checkout replay failed."
    Assert-True ((Get-RequiredHeader $orderReplay "Idempotency-Replayed") -eq "true") `
        "Authenticated checkout replay was not identified."
    $evidence.authenticatedRegression.checkoutStatus = [int]$order.StatusCode
    $evidence.authenticatedRegression.checkoutReplayStatus = [int]$orderReplay.StatusCode
    $orderBody = Read-ResponseBody $order | ConvertFrom-Json
    $orderReplayBody = Read-ResponseBody $orderReplay | ConvertFrom-Json
    Assert-True ($orderBody.id -eq $orderReplayBody.id) "Checkout replay returned another order."
    $orderJobId = [Guid](Get-RequiredHeader $order "X-Notification-Job-ID")
    Wait-JobCompleted $orderJobId

    $feed = $null
    $notification = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $feedResponse = Send-Request (New-Request `
            -Method ([System.Net.Http.HttpMethod]::Get) `
            -Path "/api/v2/auth/notifications" `
            -Body $null `
            -AccessToken $accessToken `
            -IdempotencyKey "")
        Assert-True ([int]$feedResponse.StatusCode -eq 200) "The authenticated feed failed."
        $feed = @(Read-ResponseBody $feedResponse | ConvertFrom-Json)
        $notification = $feed | Where-Object { $_.orderId -eq $orderBody.id } | Select-Object -First 1
        if ($null -ne $notification) {
            break
        }

        Start-Sleep -Seconds 1
    }
    Assert-True ($null -ne $notification) "The completed order notification did not reach its recipient."

    $read = Send-Request (New-Request `
        -Method ([System.Net.Http.HttpMethod]::new("PATCH")) `
        -Path "/api/v2/auth/notifications/$($notification.id)/read" `
        -Body $null `
        -AccessToken $accessToken `
        -IdempotencyKey "")
    $readBody = Read-ResponseBody $read | ConvertFrom-Json
    Assert-True ([int]$read.StatusCode -eq 200 -and $readBody.isRead) `
        "Opening the notification did not persist read state."
    $evidence.authenticatedRegression.notificationReadPersisted = [bool]$readBody.isRead

    $beforeDeleteRows = @(Invoke-DatabaseQuery @"
SET NOCOUNT ON;
SELECT
  (SELECT COUNT(*) FROM Notifications WHERE BulkNotificationJobId = '$orderJobId'),
  (SELECT COUNT(*) FROM BulkNotificationJobItems WHERE JobId = '$orderJobId'),
  (SELECT COUNT(*) FROM BulkNotificationJobs WHERE Id = '$orderJobId'),
  (SELECT COUNT(*) FROM OutboxMessages WHERE AggregateId = '$orderJobId');
"@)
    $beforeDelete = Get-DelimitedValues $beforeDeleteRows[-1]
    Assert-True ($beforeDelete.Count -eq 4 -and [int]$beforeDelete[0] -ge 1) `
        "Could not inspect the shared notification job before deletion."

    $deleteTaskA = $client.SendAsync((New-Request `
        -Method ([System.Net.Http.HttpMethod]::Delete) `
        -Path "/api/v2/auth/notifications/$($notification.id)" `
        -Body $null `
        -AccessToken $accessToken `
        -IdempotencyKey ""))
    $deleteTaskB = $client.SendAsync((New-Request `
        -Method ([System.Net.Http.HttpMethod]::Delete) `
        -Path "/api/v2/auth/notifications/$($notification.id)" `
        -Body $null `
        -AccessToken $accessToken `
        -IdempotencyKey ""))
    $deleteA = $deleteTaskA.GetAwaiter().GetResult()
    $deleteB = $deleteTaskB.GetAwaiter().GetResult()
    $deleteStatuses = @([int]$deleteA.StatusCode, [int]$deleteB.StatusCode) | Sort-Object
    Assert-True (
        $deleteStatuses[0] -eq 204 -and
        $deleteStatuses[1] -eq 404) `
        "Concurrent delete did not resolve to one 204 and one controlled 404."
    $evidence.authenticatedRegression.concurrentDeleteStatuses = @(
        [int]$deleteStatuses[0],
        [int]$deleteStatuses[1])

    $deleteEvidence = @(Invoke-DatabaseQuery @"
SET NOCOUNT ON;
SELECT
  (SELECT COUNT(*) FROM Notifications WHERE BulkNotificationJobId = '$orderJobId'),
  (SELECT COUNT(*) FROM BulkNotificationJobItems WHERE JobId = '$orderJobId'),
  (SELECT COUNT(*) FROM BulkNotificationJobs WHERE Id = '$orderJobId'),
  (SELECT COUNT(*) FROM OutboxMessages WHERE AggregateId = '$orderJobId'),
  (SELECT COUNT(*) FROM InboxMessages WHERE AggregateId = '$orderJobId'),
  (SELECT COUNT(*) FROM IdempotencyRecords WHERE ResourceType = 'Order' AND ResourceId = '$($orderBody.id)'),
  (SELECT COUNT(*) FROM Orders WHERE Id = $($orderBody.id));
"@)
    $deleted = Get-DelimitedValues $deleteEvidence[-1]
    Assert-True (
        $deleted.Count -eq 7 -and
        [int]$deleted[0] -eq ([int]$beforeDelete[0] - 1) -and
        $deleted[1] -eq $beforeDelete[1] -and
        $deleted[2] -eq "1" -and
        $deleted[3] -eq "1" -and
        $deleted[4] -eq "1" -and
        $deleted[5] -eq "1" -and
        $deleted[6] -eq "1") `
        "Delete did not remove only the selected notification while retaining the shared job, other recipients, replay receipts, and order."
    $evidence.authenticatedRegression.remainingGraphCounts = @(
        [int]$deleted[0],
        [int]$deleted[1],
        [int]$deleted[2],
        [int]$deleted[3])
    $evidence.authenticatedRegression.retainedInboxIdempotencyOrder = @(
        [int]$deleted[4],
        [int]$deleted[5],
        [int]$deleted[6])

    Set-VerificationStage "complete"
    $evidence.result = "PASS"
    [pscustomobject]@{
        ApiLiveDuringOutage = [int]$live.StatusCode
        ApiReadyDuringOutage = [int]$ready.StatusCode
        InitialAdmission = [int]$first.StatusCode
        AdmissionLatencyMilliseconds = $admissionLatencyMilliseconds
        AdmissionLatencyLimitMilliseconds = $MaxAdmissionMilliseconds
        SameKeyReplay = [int]$replay.StatusCode
        ChangedPayload = [int]$conflict.StatusCode
        ConcurrentAdmissions = "$([int]$firstConcurrent.StatusCode),$([int]$secondConcurrent.StatusCode)"
        JobsPersistedDuringOutage = $before[0]
        OutboxRowsDuringOutage = $before[1]
        PrematureNotifications = $before[3]
        RecoveredSingleEffects = $completedRows.Count
        AuthenticatedCheckoutReplay = [int]$orderReplay.StatusCode
        NotificationReadPersisted = [bool]$readBody.isRead
        ConcurrentDeleteStatuses = "$($deleteStatuses[0]),$($deleteStatuses[1])"
        RemainingGraphCounts = ($deleted[0..3] -join ",")
        RetainedInboxIdempotencyOrder = ($deleted[4..6] -join ",")
        EvidencePath = $EvidencePath
        Result = "PASS"
    } | Format-List
}
catch {
    $evidence.result = "FAIL"
    $evidence.stage = $stage
    $evidence.failure = [ordered]@{
        exceptionType = $_.Exception.GetType().FullName
    }
    throw
}
finally {
    if (-not $rabbitStarted) {
        docker compose @compose start rabbitmq | Out-Host
        if ($LASTEXITCODE -eq 0) {
            $rabbitStarted = $true
            $rabbitRestartedAtUtc = [DateTimeOffset]::UtcNow
            $evidence.relayRecovery.rabbitRestartedAtUtc = $rabbitRestartedAtUtc.ToString("O")
            if ($null -ne $rabbitStoppedAtUtc) {
                $evidence.relayRecovery.outageDurationSeconds = [Math]::Round(
                    ($rabbitRestartedAtUtc - $rabbitStoppedAtUtc).TotalSeconds,
                    3)
            }
        }
        elseif ($evidence.result -eq "PASS") {
            $evidence.result = "FAIL"
            $evidence.stage = "restore-rabbitmq"
            $evidence.failure = [ordered]@{
                exceptionType = "DockerComposeRabbitMqRestartFailure"
            }
        }
    }

    if ($evidence.result -eq "IN_PROGRESS") {
        $evidence.result = "FAIL"
        $evidence.stage = $stage
        $evidence.failure = [ordered]@{
            exceptionType = "VerificationTerminatedBeforeCompletion"
        }
    }
    $evidence.completedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    Write-SanitizedEvidence $evidence
    $client.Dispose()
}
