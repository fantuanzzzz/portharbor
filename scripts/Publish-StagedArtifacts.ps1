param(
    [Parameter(Mandatory)][string]$StagingDirectory,
    [Parameter(Mandatory)][string]$BackupDirectory,
    [Parameter(Mandatory)][object[]]$Targets,
    [int]$FailAfterInstallCount = -1,
    [int]$LockInstalledTargetIndex = -1,
    [switch]$FailBeforePublish)

$ErrorActionPreference = 'Stop'
# Validate every mutation target before creating, moving or recursively removing anything.
$StagingDirectory = [System.IO.Path]::GetFullPath($StagingDirectory).TrimEnd('\','/')
$BackupDirectory = [System.IO.Path]::GetFullPath($BackupDirectory).TrimEnd('\','/')
$publishRoot = [System.IO.Path]::GetDirectoryName($StagingDirectory)
if (-not $publishRoot -or -not $BackupDirectory.StartsWith($StagingDirectory + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Backup directory must be contained in the staging directory.'
}
$names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($item in $Targets) {
    $item.Target = [System.IO.Path]::GetFullPath($item.Target)
    $item.Staged = [System.IO.Path]::GetFullPath($item.Staged)
    if ([System.IO.Path]::GetDirectoryName($item.Target) -ne $publishRoot -or
        [System.IO.Path]::GetDirectoryName($item.Staged) -ne $StagingDirectory -or
        [System.IO.Path]::GetFileName($item.Staged) -ne [System.IO.Path]::GetFileName($item.Target) -or
        $item.Target -eq $StagingDirectory -or $item.Staged -eq $BackupDirectory -or
        -not $names.Add([System.IO.Path]::GetFileName($item.Target))) {
        throw 'Publication targets must be distinct sibling outputs with matching staged names.'
    }
}
$backedUp = [System.Collections.Generic.List[string]]::new()
$installed = [System.Collections.Generic.List[string]]::new()
$committed = $false
$rollbackComplete = $true
$injectedLock = $null

try {
    if ($FailBeforePublish) { throw 'Simulated failure before publish replacement.' }
    New-Item -ItemType Directory -Force -Path $BackupDirectory | Out-Null
    foreach ($item in $Targets) {
        if (Test-Path -LiteralPath $item.Target) {
            Move-Item -LiteralPath $item.Target -Destination $BackupDirectory
            $backedUp.Add([System.IO.Path]::GetFullPath($item.Target))
        }
    }
    foreach ($item in $Targets) {
        Move-Item -LiteralPath $item.Staged -Destination $item.Target
        $installed.Add([System.IO.Path]::GetFullPath($item.Target))
        if ($installed.Count -eq ($LockInstalledTargetIndex + 1)) {
            $lockFile = if ((Get-Item -LiteralPath $item.Target).PSIsContainer) { Join-Path $item.Target 'version.txt' } else { $item.Target }
            $injectedLock = [System.IO.File]::Open($lockFile, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        }
        if ($FailAfterInstallCount -ge 0 -and $installed.Count -ge $FailAfterInstallCount) {
            throw "Simulated failure after installing $($installed.Count) targets."
        }
    }
    $committed = $true
}
catch {
    $publishError = $_
    $installedTargets = $installed.ToArray(); [array]::Reverse($installedTargets)
    foreach ($target in $installedTargets) {
        try { Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop }
        catch { $rollbackComplete = $false; Write-Warning "Could not remove partially published target '$target': $($_.Exception.Message)" }
    }
    $backedUpTargets = $backedUp.ToArray(); [array]::Reverse($backedUpTargets)
    foreach ($target in $backedUpTargets) {
        $backupPath = Join-Path $BackupDirectory (Split-Path $target -Leaf)
        try {
            if (Test-Path -LiteralPath $target) { throw "Target path is still occupied: $target" }
            if (Test-Path -LiteralPath $backupPath) { Move-Item -LiteralPath $backupPath -Destination $target -ErrorAction Stop }
            else { throw "Previous target is missing from rollback storage: $backupPath" }
        }
        catch { $rollbackComplete = $false; Write-Warning "Could not restore previous target '$target'; backup remains at '$backupPath': $($_.Exception.Message)" }
    }
    if (-not $rollbackComplete) {
        throw "Publish failed and rollback was incomplete. Preserve recovery data at '$StagingDirectory'. Original error: $($publishError.Exception.Message)"
    }
    throw $publishError
}
finally {
    if ($injectedLock) { $injectedLock.Dispose() }
    if ($committed -or $rollbackComplete) {
        if (Test-Path -LiteralPath $StagingDirectory) { Remove-Item -LiteralPath $StagingDirectory -Recurse -Force -ErrorAction SilentlyContinue }
    }
}
