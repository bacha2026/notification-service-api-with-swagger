[CmdletBinding()]
param(
    [string]$ManagementBaseUrl = "http://localhost:15672/",
    [string]$RabbitUser,
    [string]$RabbitPassword,
    [string]$OutputPath = "evidence/week-03/dlq.png"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\Week3Environment.ps1"
$RabbitUser = if ([string]::IsNullOrWhiteSpace($RabbitUser)) {
    Get-Week3EnvironmentValue -Name 'WEEK3_RABBITMQ_USERNAME'
} else { $RabbitUser }
$RabbitPassword = if ([string]::IsNullOrWhiteSpace($RabbitPassword)) {
    Get-Week3EnvironmentValue -Name 'WEEK3_RABBITMQ_PASSWORD'
} else { $RabbitPassword }
$edgeCandidates = @(
    "$env:ProgramFiles (x86)\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
)
$edgePath = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $edgePath) {
    throw "Microsoft Edge is required to capture the RabbitMQ Management evidence."
}

$playwrightRoot = Join-Path ([IO.Path]::GetTempPath()) "nsa-week3-playwright-core-1.61.1"
$playwrightModule = Join-Path $playwrightRoot "node_modules\playwright-core"
if (-not (Test-Path -LiteralPath $playwrightModule)) {
    New-Item -ItemType Directory -Path $playwrightRoot -Force | Out-Null
    npm install --prefix $playwrightRoot --no-save --no-package-lock playwright-core@1.61.1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Could not install the temporary Playwright Core dependency."
    }
}

$resolvedOutput = [IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
$previousNodePath = $env:NODE_PATH
$previousEdgePath = $env:WEEK3_EDGE_PATH
try {
    $env:NODE_PATH = Join-Path $playwrightRoot "node_modules"
    $env:WEEK3_EDGE_PATH = $edgePath
    node ".\scripts\Capture-Week3RabbitMqDlq.cjs" `
        $ManagementBaseUrl `
        $RabbitUser `
        $RabbitPassword `
        $resolvedOutput

    if ($LASTEXITCODE -ne 0) {
        throw "Could not capture the RabbitMQ Management evidence."
    }
}
finally {
    $env:NODE_PATH = $previousNodePath
    $env:WEEK3_EDGE_PATH = $previousEdgePath
}

Get-Item -LiteralPath $resolvedOutput | Select-Object FullName, Length, LastWriteTime
