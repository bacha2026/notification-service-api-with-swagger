function Get-Week3EnvironmentValue {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Name,

        [string]$EnvironmentFile = (Join-Path (Split-Path $PSScriptRoot -Parent) '.env')
    )

    $processValue = [Environment]::GetEnvironmentVariable($Name, 'Process')
    if (-not [string]::IsNullOrWhiteSpace($processValue)) {
        return $processValue
    }

    if (Test-Path -LiteralPath $EnvironmentFile) {
        foreach ($line in [IO.File]::ReadAllLines($EnvironmentFile)) {
            $trimmed = $line.Trim()
            if ($trimmed.Length -eq 0 -or $trimmed.StartsWith('#')) {
                continue
            }

            $parts = $trimmed.Split('=', 2)
            if ($parts.Count -eq 2 -and $parts[0].Trim() -eq $Name) {
                $value = $parts[1].Trim().Trim('"').Trim("'")
                if (-not [string]::IsNullOrWhiteSpace($value)) {
                    return $value
                }
            }
        }
    }

    throw "$Name is not configured. Run scripts/Initialize-Week3Environment.ps1 or set it in the current process."
}
