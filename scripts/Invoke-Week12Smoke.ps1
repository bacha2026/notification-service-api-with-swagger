param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$BaseUrl = 'http://127.0.0.1:5099'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(10)
$root = $BaseUrl.TrimEnd('/')

function Invoke-ContractProbe {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Path,
        [int]$ExpectedStatus,
        [string]$ExpectedMediaType,
        [bool]$ExpectProblemDetails = $false,
        [Nullable[bool]]$ExpectDeprecation = $null,
        [string]$Body = '',
        [string]$RequestMediaType = 'application/json'
    )

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::new($Method),
        "$root$Path")
    $response = $null
    if ($Method -in @('POST', 'PUT', 'PATCH')) {
        $request.Content = [System.Net.Http.StringContent]::new(
            $Body,
            [System.Text.Encoding]::UTF8,
            $RequestMediaType)
    }

    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $status = [int]$response.StatusCode
        $mediaType = $response.Content.Headers.ContentType.MediaType
        $deprecation = if ($response.Headers.Contains('Deprecation')) {
            $response.Headers.GetValues('Deprecation') -join ','
        } else {
            $null
        }
        $sunset = if ($response.Headers.Contains('Sunset')) {
            $response.Headers.GetValues('Sunset') -join ','
        } else {
            $null
        }

        if ($status -ne $ExpectedStatus) {
            throw "$Name expected HTTP $ExpectedStatus but received $status. Body: $responseBody"
        }

        if ($mediaType -ne $ExpectedMediaType) {
            throw "$Name expected '$ExpectedMediaType' but received '$mediaType'."
        }

        $hasTraceId = $false
        if ($ExpectProblemDetails) {
            $problem = $responseBody | ConvertFrom-Json
            foreach ($property in @('status', 'title', 'instance', 'traceId')) {
                if ($problem.PSObject.Properties.Name -notcontains $property) {
                    throw "$Name is missing Problem Details property '$property'."
                }
            }

            if ([int]$problem.status -ne $ExpectedStatus -or [string]::IsNullOrWhiteSpace($problem.traceId)) {
                throw "$Name returned inconsistent Problem Details."
            }

            $hasTraceId = $true
        }

        if ($null -ne $ExpectDeprecation) {
            if ($ExpectDeprecation -and ($deprecation -ne 'true' -or [string]::IsNullOrWhiteSpace($sunset))) {
                throw "$Name did not include the required v1 Deprecation and Sunset headers."
            }

            if (-not $ExpectDeprecation -and $null -ne $deprecation) {
                throw "$Name unexpectedly included a Deprecation header."
            }
        }

        return [pscustomobject]@{
            name = $Name
            method = $Method
            path = $Path
            status = $status
            contentType = $mediaType
            deprecation = $deprecation
            sunset = $sunset
            problemTraceIdPresent = $hasTraceId
        }
    }
    finally {
        if ($null -ne $response) { $response.Dispose() }
        $request.Dispose()
    }
}

try {
    $results = @(
        Invoke-ContractProbe -Name 'Swagger v1 JSON' -Method GET -Path '/swagger/v1/swagger.json' -ExpectedStatus 200 -ExpectedMediaType 'application/json'
        Invoke-ContractProbe -Name 'Swagger v2 JSON' -Method GET -Path '/swagger/v2/swagger.json' -ExpectedStatus 200 -ExpectedMediaType 'application/json'
        Invoke-ContractProbe -Name 'v1 route and retirement headers' -Method GET -Path '/api/v1/products' -ExpectedStatus 200 -ExpectedMediaType 'application/json' -ExpectDeprecation $true
        Invoke-ContractProbe -Name 'v1.0 missing resource' -Method GET -Path '/api/v1.0/notifications/2147483647' -ExpectedStatus 404 -ExpectedMediaType 'application/problem+json' -ExpectProblemDetails $true -ExpectDeprecation $true
        Invoke-ContractProbe -Name 'v2 route' -Method GET -Path '/api/v2/products' -ExpectedStatus 200 -ExpectedMediaType 'application/json' -ExpectDeprecation $false
        Invoke-ContractProbe -Name 'unversioned compatibility route' -Method GET -Path '/api/products' -ExpectedStatus 200 -ExpectedMediaType 'application/json' -ExpectDeprecation $false
        Invoke-ContractProbe -Name 'model validation' -Method POST -Path '/api/v2/notifications' -Body '{}' -ExpectedStatus 400 -ExpectedMediaType 'application/problem+json' -ExpectProblemDetails $true -ExpectDeprecation $false
        Invoke-ContractProbe -Name 'malformed JSON' -Method POST -Path '/api/v2/notifications' -Body '{' -ExpectedStatus 400 -ExpectedMediaType 'application/problem+json' -ExpectProblemDetails $true -ExpectDeprecation $false
        Invoke-ContractProbe -Name 'missing route' -Method GET -Path '/api/v2/not-a-route' -ExpectedStatus 404 -ExpectedMediaType 'application/problem+json' -ExpectProblemDetails $true -ExpectDeprecation $false
        Invoke-ContractProbe -Name 'method mismatch' -Method PATCH -Path '/api/v2/products/1' -Body '{}' -ExpectedStatus 405 -ExpectedMediaType 'application/problem+json' -ExpectProblemDetails $true -ExpectDeprecation $false
        Invoke-ContractProbe -Name 'unsupported media type' -Method POST -Path '/api/v2/notifications' -Body 'plain text' -RequestMediaType 'text/plain' -ExpectedStatus 415 -ExpectedMediaType 'application/problem+json' -ExpectProblemDetails $true -ExpectDeprecation $false
        Invoke-ContractProbe -Name 'unsupported API version' -Method GET -Path '/api/v9/products' -ExpectedStatus 404 -ExpectedMediaType 'application/problem+json' -ExpectProblemDetails $true -ExpectDeprecation $false
    )

    [pscustomobject]@{
        verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        baseUrl = $root
        passed = $true
        probeCount = $results.Count
        probes = $results
    } | ConvertTo-Json -Depth 5
}
finally {
    $client.Dispose()
}
