[CmdletBinding()]
param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'artifacts\shortcut-validation' }
if (-not ('PortHarbor.BuildTools.ShortcutInspection' -as [type])) {
    Add-Type -Path (Join-Path $root 'packaging\ShortcutInspection.cs')
}
$fixture = Join-Path $output ([guid]::NewGuid().ToString('N'))
$directory = Join-Path $fixture '中文 路径 fixture'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$target = Join-Path $directory 'PortHarbor.exe'
$copy = Join-Path $directory 'different.exe'
[IO.File]::WriteAllText($target, 'synthetic file, never executed')
[IO.File]::WriteAllText($copy, 'synthetic file, never executed')
$checks = [Collections.Generic.List[string]]::new()
function Assert([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $checks.Add($Name); Write-Host "PASS: $Name"
}
$shell = New-Object -ComObject WScript.Shell
function New-Shortcut([string]$Name, [string]$Target, [string]$Arguments = '') {
    $path = Join-Path $fixture $Name
    $link = $shell.CreateShortcut($path)
    try { $link.TargetPath = $Target; $link.Arguments = $Arguments; $link.WorkingDirectory = $directory; $link.Save() }
    finally { [Runtime.InteropServices.Marshal]::ReleaseComObject($link) | Out-Null }
    return $path
}
$failure = $null
try {
    $link = [PortHarbor.BuildTools.ShortcutInspection]::Read((New-Shortcut '中文.lnk' $target))
    Assert ($link.TargetPath -eq $target -and [PortHarbor.BuildTools.ShortcutInspection]::TargetsFile($link.TargetPath, $target)) 'Unicode Shell reader preserves a Chinese path with spaces and verifies its file'
    Assert ([PortHarbor.BuildTools.ShortcutInspection]::TargetsFile($target.ToUpperInvariant(), $target)) 'Windows case aliases identify the same file'
    $alias = Join-Path $fixture 'alias.exe'
    New-Item -ItemType HardLink -Path $alias -Target $target | Out-Null
    $link = [PortHarbor.BuildTools.ShortcutInspection]::Read((New-Shortcut 'alias.lnk' $alias))
    Assert ($link.TargetPath -ne $target -and [PortHarbor.BuildTools.ShortcutInspection]::TargetsFile($link.TargetPath, $target)) 'A differently spelled path to the same file passes without string equality'
    $link = [PortHarbor.BuildTools.ShortcutInspection]::Read((New-Shortcut 'wrong.lnk' $copy))
    Assert (-not [PortHarbor.BuildTools.ShortcutInspection]::TargetsFile($link.TargetPath, $target)) 'A different executable with identical contents is rejected'
    $link = [PortHarbor.BuildTools.ShortcutInspection]::Read((New-Shortcut 'arguments.lnk' $target '--unexpected'))
    Assert ($link.Arguments -eq '--unexpected') 'Unexpected shortcut arguments remain visible to the validator'
    Assert (-not [PortHarbor.BuildTools.ShortcutInspection]::TargetsFile('', $target)) 'An empty shortcut target is rejected'
    $rejected = $false
    try { [void][PortHarbor.BuildTools.ShortcutInspection]::TargetsFile((Join-Path $fixture 'missing.exe'), $target) }
    catch { $rejected = $true }
    Assert $rejected 'A nonexistent target cannot pass file identity inspection'
}
catch { $failure = $_.Exception.Message; throw }
finally {
    [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
    [pscustomobject]@{ Passed = $checks.Count; Failure = $failure; Checks = @($checks) } | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $fixture 'shortcut-results.json') -Encoding UTF8
}
