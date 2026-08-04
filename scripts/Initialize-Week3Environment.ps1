[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) '.env')
)

$ErrorActionPreference = 'Stop'

function New-LocalSecret {
    param([string]$Prefix)

    $bytes = [byte[]]::new(24)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    $suffix = [BitConverter]::ToString($bytes).Replace('-', '')
    return "$Prefix$suffix"
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $resolvedOutput) {
    Write-Host "Week 3 environment already exists at '$resolvedOutput'; no values were changed."
    return
}

$lines = @(
    '# Generated local-only environment. This file is ignored by Git.',
    "WEEK3_SQL_PASSWORD=$(New-LocalSecret -Prefix 'W3Sql!aA1')",
    "WEEK3_RABBITMQ_USERNAME=week3_app",
    "WEEK3_RABBITMQ_PASSWORD=$(New-LocalSecret -Prefix 'W3Rabbit!aA1')"
)

[IO.File]::WriteAllLines($resolvedOutput, $lines, [Text.UTF8Encoding]::new($false))
Write-Host "Created ignored Week 3 environment at '$resolvedOutput'. Secret values were not printed."
