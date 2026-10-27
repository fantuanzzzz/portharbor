param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'artifacts\rollback-validation' }
$helper = Join-Path $root 'scripts\Publish-StagedArtifacts.ps1'
New-Item -ItemType Directory -Force -Path $output | Out-Null

function New-Scenario([string]$Name) {
    $directory = Join-Path $output ($Name + '-' + [guid]::NewGuid().ToString('N'))
    $stage = Join-Path $directory 'stage'; $backup = Join-Path $stage 'previous'
    New-Item -ItemType Directory -Force -Path $stage,$directory | Out-Null
    $targets = @()
    foreach ($leaf in @('installed-win-x64','portable-win-x64','installed.zip','portable.zip')) {
        $target = Join-Path $directory $leaf; $staged = Join-Path $stage $leaf
        if ($leaf.EndsWith('.zip')) { Set-Content -LiteralPath $target -Value "old-$Name-$leaf" -NoNewline; Set-Content -LiteralPath $staged -Value "new-$Name-$leaf" -NoNewline }
        else {
            New-Item -ItemType Directory -Force -Path $target,$staged | Out-Null
            Set-Content -LiteralPath (Join-Path $target 'version.txt') -Value "old-$Name-$leaf" -NoNewline
            Set-Content -LiteralPath (Join-Path $staged 'version.txt') -Value "new-$Name-$leaf" -NoNewline
        }
        $targets += @{ Staged = $staged; Target = $target }
    }
    return @{ Directory=$directory; Stage=$stage; Backup=$backup; Targets=$targets; Name=$Name }
}

function Assert-Previous([object]$Scenario) {
    foreach ($item in $Scenario.Targets) {
        if (-not (Test-Path -LiteralPath $item.Target)) { throw "Rollback lost previous target: $($item.Target)" }
        $file = if ((Get-Item -LiteralPath $item.Target).PSIsContainer) { Join-Path $item.Target 'version.txt' } else { $item.Target }
        if ((Get-Content -LiteralPath $file -Raw) -ne "old-$($Scenario.Name)-$(Split-Path $item.Target -Leaf)") { throw "Rollback changed previous target: $($item.Target)" }
    }
    if (Test-Path -LiteralPath $Scenario.Stage) { throw "Rollback left staging behind: $($Scenario.Stage)" }
}

$before = New-Scenario 'before-publish'
try { & $helper -StagingDirectory $before.Stage -BackupDirectory $before.Backup -Targets $before.Targets -FailBeforePublish; throw 'Expected failure before publish replacement.' }
catch { if ($_.Exception.Message -ne 'Simulated failure before publish replacement.') { throw } }
Assert-Previous $before
Write-Host 'PASS: failure before replacement preserves all previous targets'

$partial = New-Scenario 'partial-publish'
try { & $helper -StagingDirectory $partial.Stage -BackupDirectory $partial.Backup -Targets $partial.Targets -FailAfterInstallCount 2; throw 'Expected failure after partial replacement.' }
catch { if ($_.Exception.Message -ne 'Simulated failure after installing 2 targets.') { throw } }
Assert-Previous $partial
Write-Host 'PASS: failure after partial replacement restores all previous targets'

$locked = New-Scenario 'locked-old-archive'
$stream = [System.IO.File]::Open($locked.Targets[2].Target, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
try {
    try { & $helper -StagingDirectory $locked.Stage -BackupDirectory $locked.Backup -Targets $locked.Targets; throw 'Expected an occupied old archive to block replacement.' }
    catch { if ($_.Exception.Message -eq 'Expected an occupied old archive to block replacement.') { throw } }
}
finally { $stream.Dispose() }
Assert-Previous $locked
Write-Host 'PASS: an occupied old archive blocks replacement without losing prior outputs'

$blockedRollback = New-Scenario 'locked-new-target-during-rollback'
try { & $helper -StagingDirectory $blockedRollback.Stage -BackupDirectory $blockedRollback.Backup -Targets $blockedRollback.Targets -FailAfterInstallCount 1 -LockInstalledTargetIndex 0; throw 'Expected injected partial publish failure.' }
catch { if ($_.Exception.Message -notlike 'Publish failed and rollback was incomplete.*') { throw } }
$saved = Join-Path $blockedRollback.Backup 'installed-win-x64'
if (-not (Test-Path -LiteralPath $saved) -or (Get-Content -LiteralPath (Join-Path $saved 'version.txt') -Raw) -ne 'old-locked-new-target-during-rollback-installed-win-x64') { throw 'Incomplete rollback deleted or changed the recoverable old target.' }
if (-not (Test-Path -LiteralPath $blockedRollback.Stage)) { throw 'Incomplete rollback discarded its recovery directory.' }
Write-Host "PASS: incomplete rollback preserves the previous target and recovery directory at $($blockedRollback.Stage)"

$success = New-Scenario 'successful-replacement'
& $helper -StagingDirectory $success.Stage -BackupDirectory $success.Backup -Targets $success.Targets
foreach ($item in $success.Targets) {
    $file = if ((Get-Item -LiteralPath $item.Target).PSIsContainer) { Join-Path $item.Target 'version.txt' } else { $item.Target }
    if ((Get-Content -LiteralPath $file -Raw) -ne "new-$($success.Name)-$(Split-Path $item.Target -Leaf)") { throw 'Successful publication did not replace a target.' }
}
if (Test-Path -LiteralPath $success.Stage) { throw 'Successful publication left staging or backups behind.' }
Write-Host 'PASS: successful replacement publishes all four targets and removes staging and backups'
