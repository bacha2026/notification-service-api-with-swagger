param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$BaseUrl = 'http://127.0.0.1:5099',

    [Parameter()]
    [ValidateRange(20, 1000)]
    [int]$Samples = 25,

    [Parameter()]
    [ValidateRange(1, 100)]
    [int]$WarmupSamples = 5,

    [Parameter()]
    [ValidateRange(1, 100)]
    [int]$BatchSize = 1
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(10)
$endpoint = "$($BaseUrl.TrimEnd('/'))/api/v2/notifications/bulk"

function Invoke-BulkRequest {
    param([int]$Sequence)

    $response = $null

    $notifications = 1..$BatchSize | ForEach-Object {
        @{
            recipientEmail = "latency-$Sequence-$_@example.com"
            channel = 1
            subject = "Latency sample $Sequence item $_"
            body = 'Deterministic Week 2 latency verification payload.'
            orderId = $null
        }
    }

    $json = @{ notifications = @($notifications) } | ConvertTo-Json -Depth 5 -Compress
    $content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, 'application/json')
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = $client.PostAsync($endpoint, $content).GetAwaiter().GetResult()
        $stopwatch.Stop()
        if ([int]$response.StatusCode -ne 202) {
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            throw "Expected HTTP 202 but received $([int]$response.StatusCode): $body"
        }

        return [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
    }
    finally {
        if ($null -ne $response) { $response.Dispose() }
        $content.Dispose()
    }
}

try {
    1..$WarmupSamples | ForEach-Object { [void](Invoke-BulkRequest -Sequence (-$_)) }

    $measurements = 1..$Samples | ForEach-Object { Invoke-BulkRequest -Sequence $_ }
    $sorted = @($measurements | Sort-Object)
    $p50Index = [Math]::Max(0, [Math]::Ceiling(0.50 * $Samples) - 1)
    $p95Index = [Math]::Max(0, [Math]::Ceiling(0.95 * $Samples) - 1)

    [pscustomobject]@{
        measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        endpoint = $endpoint
        warmupSamples = $WarmupSamples
        samples = $Samples
        batchSize = $BatchSize
        concurrency = 1
        method = 'nearest-rank percentile over sequential requests after warm-up'
        p50Milliseconds = $sorted[$p50Index]
        p95Milliseconds = $sorted[$p95Index]
        maximumMilliseconds = $sorted[-1]
        acceptanceThresholdMilliseconds = 100
        passed = $sorted[$p95Index] -lt 100
        rawMilliseconds = @($measurements)
    } | ConvertTo-Json -Depth 4
}
finally {
    $client.Dispose()
}
