[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall')]
    [string]$Action = 'Install',

    [Parameter(Mandatory = $false)]
    [string]$ReleaseRoot,

    [Parameter(Mandatory = $true)]
    [string]$InstallRoot,

    [string]$PathFile,

    [switch]$ApplyUserPath,

    [switch]$KeepBackup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string]$Path) {
    return [IO.Path]::GetFullPath($Path)
}

function Normalize-PathEntry([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ''
    }

    $trimmed = $Path.Trim().Trim('"')
    try {
        return (Resolve-FullPath $trimmed).TrimEnd('\', '/')
    }
    catch {
        return $trimmed.TrimEnd('\', '/')
    }
}

function Read-PathValue {
    if ($PathFile) {
        if (-not (Test-Path -LiteralPath $PathFile -PathType Leaf)) {
            return ''
        }

        return [IO.File]::ReadAllText((Resolve-FullPath $PathFile))
    }

    return [Environment]::GetEnvironmentVariable('Path', 'User')
}

function Write-PathValue([string]$Value) {
    if ($PathFile) {
        $resolved = Resolve-FullPath $PathFile
        $parent = Split-Path -Parent $resolved
        if ($parent) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }

        [IO.File]::WriteAllText($resolved, $Value, [Text.UTF8Encoding]::new($false))
        return
    }

    if (-not $ApplyUserPath) {
        return
    }

    [Environment]::SetEnvironmentVariable('Path', $Value, 'User')
}

function Get-PathEntries([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }

    return @($Value -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Add-PathEntry([string]$Value, [string]$Entry) {
    $entries = @(Get-PathEntries $Value)
    $normalizedEntry = Normalize-PathEntry $Entry
    if (-not ($entries | Where-Object { (Normalize-PathEntry $_) -ieq $normalizedEntry })) {
        $entries += $Entry
    }

    return ($entries -join ';')
}

function Remove-PathEntry([string]$Value, [string]$Entry) {
    $normalizedEntry = Normalize-PathEntry $Entry
    $entries = @(Get-PathEntries $Value | Where-Object { (Normalize-PathEntry $_) -ine $normalizedEntry })
    return ($entries -join ';')
}

function Test-CommandCollision([string]$Root, [string[]]$Entries) {
    $normalizedRoot = Normalize-PathEntry $Root
    foreach ($entry in $Entries) {
        $directory = Normalize-PathEntry $entry
        if (-not $directory -or $directory -ieq $normalizedRoot -or -not (Test-Path -LiteralPath $directory -PathType Container)) {
            continue
        }

        foreach ($command in @('flashrouter.cmd', 'flashrouter.bat', 'flashrouter.exe')) {
            $candidate = Join-Path $directory $command
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                throw "COMMAND_COLLISION: '$candidate' already exists outside the requested installation root."
            }
        }
    }
}

function Assert-ReleasePackage([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "RELEASE_NOT_FOUND: '$Root' does not exist."
    }

    foreach ($required in @('flashrouter.exe', 'flashrouter.cmd', 'FLASH_ROUTER.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $required) -PathType Leaf)) {
            throw "RELEASE_INVALID: '$required' is missing from the release package."
        }
    }

    $sensitive = Get-ChildItem -LiteralPath $Root -Recurse -Force -File |
        Where-Object {
            $relative = $_.FullName.Substring($Root.Length + 1)
            $relative -match '(^|[\\/])\.local([\\/]|$)' -or
            $_.Name -match '^(control\.secret|instance\.json|flashrouter\.db)$' -or
            $_.Extension -in @('.vault', '.secret')
        }
    if ($sensitive) {
        throw "RELEASE_SENSITIVE_DATA: release packages must not contain local data, vault files, or runtime secrets."
    }
}

function Get-InstallationMarker([string]$Root) {
    $markerPath = Join-Path $Root 'installation.json'
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        return $null
    }

    try {
        return Get-Content -Raw -LiteralPath $markerPath | ConvertFrom-Json
    }
    catch {
        throw "INSTALLATION_INVALID: '$markerPath' is not valid installation metadata."
    }
}

if ($PathFile -and $ApplyUserPath) {
    throw 'PATH_TARGET_INVALID: use either -PathFile for sandbox testing or -ApplyUserPath for the real user PATH.'
}

$resolvedInstallRoot = Resolve-FullPath $InstallRoot
$oldPathValue = Read-PathValue
$pathEntries = @(Get-PathEntries $oldPathValue)

if ($Action -eq 'Install') {
    if (-not $ReleaseRoot) {
        throw 'RELEASE_REQUIRED: -ReleaseRoot is required for installation.'
    }

    $resolvedReleaseRoot = Resolve-FullPath $ReleaseRoot
    Assert-ReleasePackage $resolvedReleaseRoot
    Test-CommandCollision $resolvedInstallRoot $pathEntries

    $existingMarker = if (Test-Path -LiteralPath $resolvedInstallRoot -PathType Container) {
        Get-InstallationMarker $resolvedInstallRoot
    }
    else {
        $null
    }
    if ((Test-Path -LiteralPath $resolvedInstallRoot) -and $null -eq $existingMarker) {
        throw "INSTALL_COLLISION: '$resolvedInstallRoot' exists but is not a FLASH ROUTER installation."
    }

    $installParent = Split-Path -Parent $resolvedInstallRoot
    New-Item -ItemType Directory -Path $installParent -Force | Out-Null
    $staging = Join-Path $installParent ('.' + (Split-Path -Leaf $resolvedInstallRoot) + '.staging-' + [Guid]::NewGuid().ToString('N'))
    $backup = $null
    try {
        New-Item -ItemType Directory -Path $staging -Force | Out-Null
        Copy-Item -Path (Join-Path $resolvedReleaseRoot '*') -Destination $staging -Recurse -Force
        $metadata = [ordered]@{
            product = 'FLASH ROUTER'
            launcher = 'flashrouter.exe'
            host = 'FLASH_ROUTER.exe'
            installedAtUtc = [DateTime]::UtcNow.ToString('O')
            releaseRoot = $resolvedReleaseRoot
        }
        $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staging 'installation.json') -Encoding UTF8

        if (Test-Path -LiteralPath $resolvedInstallRoot) {
            $backup = $resolvedInstallRoot + '.backup-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
            Move-Item -LiteralPath $resolvedInstallRoot -Destination $backup
        }
        Move-Item -LiteralPath $staging -Destination $resolvedInstallRoot

        $newPathValue = if ($PathFile -or $ApplyUserPath) {
            Add-PathEntry $oldPathValue $resolvedInstallRoot
        }
        else {
            $oldPathValue
        }
        if ($PathFile -or $ApplyUserPath) {
            Write-PathValue $newPathValue
        }

        [ordered]@{
            action = 'install'
            installRoot = $resolvedInstallRoot
            pathUpdated = [bool]($PathFile -or $ApplyUserPath)
            pathTarget = if ($PathFile) { $PathFile } elseif ($ApplyUserPath) { 'User' } else { 'NotChanged' }
            backupRoot = $backup
            command = (Join-Path $resolvedInstallRoot 'flashrouter.cmd')
        } | ConvertTo-Json
    }
    catch {
        if (Test-Path -LiteralPath $staging) {
            Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ($backup -and (Test-Path -LiteralPath $backup) -and -not (Test-Path -LiteralPath $resolvedInstallRoot)) {
            Move-Item -LiteralPath $backup -Destination $resolvedInstallRoot -ErrorAction SilentlyContinue
        }
        throw
    }
}
else {
    $marker = Get-InstallationMarker $resolvedInstallRoot
    if ($null -eq $marker) {
        throw "UNINSTALL_REFUSED: '$resolvedInstallRoot' is not a marked FLASH ROUTER installation."
    }

    $newPathValue = Remove-PathEntry $oldPathValue $resolvedInstallRoot
    try {
        if ($PathFile -or $ApplyUserPath) {
            Write-PathValue $newPathValue
        }
        if (-not $KeepBackup) {
            Remove-Item -LiteralPath $resolvedInstallRoot -Recurse -Force
        }

        [ordered]@{
            action = 'uninstall'
            installRoot = $resolvedInstallRoot
            pathUpdated = [bool]($PathFile -or $ApplyUserPath)
            pathTarget = if ($PathFile) { $PathFile } elseif ($ApplyUserPath) { 'User' } else { 'NotChanged' }
            preserved = [bool]$KeepBackup
        } | ConvertTo-Json
    }
    catch {
        if ($PathFile -or $ApplyUserPath) {
            Write-PathValue $oldPathValue
        }
        throw
    }
}
