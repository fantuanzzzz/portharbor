param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'artifacts\rollback-validation' }
$targets = @(
    (Join-Path $output 'installed-win-x64'),
    (Join-Path $output 'portable-win-x64'),
    (Join-Path $output 'PortHarbor-0.1.0-installed-win-x64.zip'),
    (Join-Path $output 'PortHarbor-0.1.0-portable-win-x64.zip'))
$sentinel = 'previous-release-sentinel-' + [guid]::NewGuid().ToString('N')
foreach ($directory in @($targets[0], $targets[1])) {
    New-Item -ItemType Directory -Force $directory | Out-Null
}
Set-Content -LiteralPath (Join-Path $targets[0] 'previous.txt') -Value $sentinel -Encoding UTF8
Set-Content -LiteralPath (Join-Path $targets[1] 'previous.txt') -Value $sentinel -Encoding UTF8
Set-Content -LiteralPath $targets[2] -Value $sentinel -Encoding UTF8
Set-Content -LiteralPath $targets[3] -Value $sentinel -Encoding UTF8

$build = Join-Path $root 'build.ps1'
& powershell -NoProfile -ExecutionPolicy Bypass -File $build -OutputDirectory $output -SimulateFailureAfterInstall
if ($LASTEXITCODE -eq 0) { throw 'Expected the simulated publish failure to fail the build.' }
foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target)) { throw "Rollback lost previous target: $target" }
    $file = if ((Get-Item -LiteralPath $target).PSIsContainer) { Join-Path $target 'previous.txt' } else { $target }
    if ((Get-Content -LiteralPath $file -Raw).Trim() -ne $sentinel) { throw "Rollback changed previous target: $target" }
}
$staging = Get-ChildItem -LiteralPath $output -Directory -Filter '.staging-*' -ErrorAction SilentlyContinue
if ($staging) { throw 'Rollback left a staging directory behind.' }
Write-Host "Rollback preserved all previous targets in $output"
