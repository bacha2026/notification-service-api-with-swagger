[CmdletBinding()]
param(
    [ValidateSet('rabbitmq', 'sqlserver')]
    [string]$Dependency = 'rabbitmq',

    [string]$ComposeFile = 'compose.week3.yml',

    [string]$ApiBaseUrl = 'http://127.0.0.1:8080',

    [ValidateRange(30, 300)]
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$dependencyWasStopped = $false

function Invoke-Compose {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [switch]$IgnoreExitCode
    )

    & docker compose -f $ComposeFile @Arguments
    if (-not $IgnoreExitCode -and $LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-HttpStatus {
    param([Parameter(Mandatory)][string]$Uri)

    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Uri -TimeoutSec 10
        return [int]$response.StatusCode
    }
    catch {
        if ($null -ne $_.Exception.Response) {
            return [int]$_.Exception.Response.StatusCode
        }

        throw
    }
}

function Get-ServiceHealth {
    param([Parameter(Mandatory)][string]$Service)

    $containerId = (& docker compose -f $ComposeFile ps -q $Service).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
        return 'missing'
    }

    return (& docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $containerId).Trim()
}

function Test-WorkerMarker {
    & docker compose -f $ComposeFile exec -T worker sh -c 'test -f /tmp/nsa-worker-ready' *> $null
    return $LASTEXITCODE -eq 0
}

function Wait-ForCondition {
    param(
        [Parameter(Mandatory)][scriptblock]$Condition,
        [Parameter(Mandatory)][string]$Description
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if (& $Condition) {
            return
        }

        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    throw "Timed out waiting for $Description."
}

try {
    $baselineLive = Get-HttpStatus "$ApiBaseUrl/health/live"
    $baselineReady = Get-HttpStatus "$ApiBaseUrl/health/ready"
    if ($baselineLive -ne 200 -or $baselineReady -ne 200 -or -not (Test-WorkerMarker)) {
        throw 'The stack was not live, ready, and worker-ready before the outage proof.'
    }

    $stoppedAt = [DateTimeOffset]::UtcNow
    Invoke-Compose -Arguments @('stop', $Dependency)
    $dependencyWasStopped = $true

    Wait-ForCondition -Description 'API readiness 503 while process liveness remains 200' -Condition {
        (Get-HttpStatus "$ApiBaseUrl/health/live") -eq 200 -and
        (Get-HttpStatus "$ApiBaseUrl/health/ready") -eq 503
    }
    $readinessFailedAt = [DateTimeOffset]::UtcNow
    $outageLiveStatus = Get-HttpStatus "$ApiBaseUrl/health/live"
    $outageReadyStatus = Get-HttpStatus "$ApiBaseUrl/health/ready"

    Wait-ForCondition -Description 'worker readiness marker removal' -Condition {
        -not (Test-WorkerMarker)
    }
    $workerMarkerRemovedAt = [DateTimeOffset]::UtcNow

    Wait-ForCondition -Description 'API and worker Docker health becoming unhealthy' -Condition {
        (Get-ServiceHealth 'api') -eq 'unhealthy' -and
        (Get-ServiceHealth 'worker') -eq 'unhealthy'
    }
    $containersUnhealthyAt = [DateTimeOffset]::UtcNow

    Invoke-Compose -Arguments @('start', $Dependency)
    $dependencyWasStopped = $false

    Wait-ForCondition -Description "$Dependency container health recovery" -Condition {
        (Get-ServiceHealth $Dependency) -eq 'healthy'
    }
    Wait-ForCondition -Description 'API and worker readiness recovery' -Condition {
        (Get-HttpStatus "$ApiBaseUrl/health/live") -eq 200 -and
        (Get-HttpStatus "$ApiBaseUrl/health/ready") -eq 200 -and
        (Test-WorkerMarker) -and
        (Get-ServiceHealth 'api') -eq 'healthy' -and
        (Get-ServiceHealth 'worker') -eq 'healthy'
    }
    $recoveredAt = [DateTimeOffset]::UtcNow

    [ordered]@{
        dependency = $Dependency
        stoppedAtUtc = $stoppedAt.ToString('O')
        baselineLiveStatus = $baselineLive
        baselineReadyStatus = $baselineReady
        outageLiveStatus = $outageLiveStatus
        outageReadyStatus = $outageReadyStatus
        readinessFailureDetectedMilliseconds = [math]::Round(($readinessFailedAt - $stoppedAt).TotalMilliseconds, 3)
        workerMarkerRemovedMilliseconds = [math]::Round(($workerMarkerRemovedAt - $stoppedAt).TotalMilliseconds, 3)
        containersUnhealthyMilliseconds = [math]::Round(($containersUnhealthyAt - $stoppedAt).TotalMilliseconds, 3)
        recoveredAtUtc = $recoveredAt.ToString('O')
        recoveryMilliseconds = [math]::Round(($recoveredAt - $stoppedAt).TotalMilliseconds, 3)
        finalLiveStatus = Get-HttpStatus "$ApiBaseUrl/health/live"
        finalReadyStatus = Get-HttpStatus "$ApiBaseUrl/health/ready"
        finalApiHealth = Get-ServiceHealth 'api'
        finalWorkerHealth = Get-ServiceHealth 'worker'
        workerMarkerRestored = Test-WorkerMarker
        passed = $true
    } | ConvertTo-Json
}
finally {
    if ($dependencyWasStopped) {
        Invoke-Compose -Arguments @('start', $Dependency) -IgnoreExitCode
    }
}
