[CmdletBinding()]
param([string]$BaseRevision = '', [switch]$Fix, [string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $baseline = if ($BaseRevision) { $BaseRevision } else { 'HEAD' }
    $changed = @(& git diff --name-only --diff-filter=ACMR $baseline -- '*.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine changed source files.' }
    $changed += @(& git ls-files --others --exclude-standard -- '*.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine new source files.' }
    $changed = @($changed | Sort-Object -Unique | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($changed.Count -eq 0) { Write-Host 'No changed C# files.'; return }
    # These entrypoints cover all source projects and linked Shared checks, including the pure test project.
    foreach ($project in @('tests/PortHarbor.Checks/PortHarbor.Checks.csproj',
        'tests/PortHarbor.Application.Checks/PortHarbor.Application.Checks.csproj',
        'tests/PortHarbor.UnitTests/PortHarbor.UnitTests.csproj')) {
        $arguments = @('format', 'whitespace', $project, '--no-restore', '--verbosity', 'quiet', '--include') + $changed
        if (-not $Fix) { $arguments += '--verify-no-changes' }
        & $Dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "C# formatting failed: $project. Run scripts/Check-CodeStyle.ps1 -Fix with the same -BaseRevision." }
    }
    Write-Host "C# formatting passed ($($changed.Count) changed files)."
}
finally { Pop-Location }
