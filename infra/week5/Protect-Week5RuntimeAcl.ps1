[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $RuntimePath
)

$ErrorActionPreference = 'Stop'

$expectedPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.runtime')).TrimEnd('\')
$resolvedPath = (Resolve-Path -LiteralPath $RuntimePath).Path.TrimEnd('\')
if (-not $resolvedPath.Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to change ACLs at unexpected path: $resolvedPath"
}

$items = @(
    Get-Item -Force -LiteralPath $resolvedPath
) + @(
    Get-ChildItem -Force -LiteralPath $resolvedPath -Recurse
)

if (@($items | Where-Object {
            $_.Attributes -band [IO.FileAttributes]::ReparsePoint
        }).Count -ne 0) {
    throw 'Refusing to change ACLs through a reparse point.'
}

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principals = @(
    $currentIdentity.User
    [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::LocalSystemSid,
        $null)
    [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid,
        $null)
)
$principalValues = @($principals | ForEach-Object { $_.Value })

foreach ($item in $items) {
    if ($item.PSIsContainer) {
        $security = [IO.Directory]::GetAccessControl(
            $item.FullName,
            [Security.AccessControl.AccessControlSections]::Access)
    }
    else {
        $security = [IO.File]::GetAccessControl(
            $item.FullName,
            [Security.AccessControl.AccessControlSections]::Access)
    }
    $security.SetAccessRuleProtection($true, $false)
    foreach ($existingRule in @($security.Access)) {
        [void] $security.RemoveAccessRuleSpecific($existingRule)
    }

    if ($item.PSIsContainer) {
        $inheritance = [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    }
    else {
        $inheritance = [Security.AccessControl.InheritanceFlags]::None
    }

    foreach ($principal in $principals) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $principal,
            [Security.AccessControl.FileSystemRights]::FullControl,
            $inheritance,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow)
        [void] $security.AddAccessRule($rule)
    }
    if ($item.PSIsContainer) {
        [IO.Directory]::SetAccessControl($item.FullName, $security)
    }
    else {
        [IO.File]::SetAccessControl($item.FullName, $security)
    }
}

$violations = @()
foreach ($item in $items) {
    $acl = Get-Acl -LiteralPath $item.FullName
    $expectedInheritance = if ($item.PSIsContainer) {
        [Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    }
    else {
        [Security.AccessControl.InheritanceFlags]::None
    }
    $rules = @($acl.GetAccessRules(
            $true,
            $true,
            [Security.Principal.SecurityIdentifier]))

    if (-not $acl.AreAccessRulesProtected) {
        $violations += "unprotected:$($item.FullName)"
    }
    if ($rules.Count -ne $principalValues.Count) {
        $violations += "rule-count:$($item.FullName):$($rules.Count)"
    }
    foreach ($principalValue in $principalValues) {
        if (@($rules | Where-Object {
                    $_.IdentityReference.Value -eq $principalValue
                }).Count -ne 1) {
            $violations += "missing-or-duplicate:$($item.FullName):$principalValue"
        }
    }
    foreach ($entry in $rules) {
        if ($entry.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            $principalValues -notcontains $entry.IdentityReference.Value -or
            $entry.FileSystemRights -ne [Security.AccessControl.FileSystemRights]::FullControl -or
            $entry.InheritanceFlags -ne $expectedInheritance -or
            $entry.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None -or
            $entry.IsInherited) {
            $violations += "unexpected-rule-shape:$($item.FullName):$($entry.IdentityReference.Value)"
        }
    }
    $ownerSid = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($principalValues -notcontains $ownerSid) {
        $violations += "unexpected-owner:$($item.FullName):$ownerSid"
    }
}

if ($violations.Count -ne 0) {
    throw "Runtime ACL verification failed for $($violations.Count) entries."
}

Write-Output "Protected ACLs verified for $($items.Count) Week 5 runtime objects."
