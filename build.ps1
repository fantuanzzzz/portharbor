param([string]$Dotnet = 'dotnet', [string]$PackageSource = '', [switch]$IncludeRuntime, [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.packages'
$env:APPDATA = Join-Path $PSScriptRoot '.build-home\AppData'
New-Item -ItemType Directory -Force $env:APPDATA | Out-Null

$version = '0.1.0'
$rid = 'win-x64'
$project = Join-Path $PSScriptRoot 'src\PortHarbor\PortHarbor.csproj'
$checks = Join-Path $PSScriptRoot 'tests\PortHarbor.Checks\PortHarbor.Checks.csproj'
$artifacts = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot 'artifacts' }
$portable = Join-Path $artifacts 'portable-win-x64'
$framework = Join-Path $artifacts 'installed-win-x64'
$portableZip = Join-Path $artifacts "PortHarbor-$version-portable-$rid.zip"
$frameworkZip = Join-Path $artifacts "PortHarbor-$version-installed-$rid.zip"
$config = Join-Path $PSScriptRoot 'NuGet.Config'
$staging = Join-Path $artifacts ('.staging-' + [guid]::NewGuid().ToString('N'))
$backup = Join-Path $staging 'previous'
$stagePortable = Join-Path $staging 'portable-win-x64'
$stageFramework = Join-Path $staging 'installed-win-x64'
$stagePortableZip = Join-Path $staging "PortHarbor-$version-portable-$rid.zip"
$stageFrameworkZip = Join-Path $staging "PortHarbor-$version-installed-$rid.zip"
$targets = @(
    @{ Staged = $stageFramework; Target = $framework; Directory = $true }
    @{ Staged = $stageFrameworkZip; Target = $frameworkZip; Directory = $false }
)
$targets += @{ Staged = $stagePortable; Target = $portable; Directory = $true }
$targets += @{ Staged = $stagePortableZip; Target = $portableZip; Directory = $false }
$installed = [System.Collections.Generic.List[string]]::new()

try {
    New-Item -ItemType Directory -Force $artifacts, $stagePortable, $stageFramework | Out-Null
    $restoreArgs = @('restore', $checks, '--runtime', $rid, '--configfile', $config)
    if ($PackageSource) { $restoreArgs += @('--source', $PackageSource) }
    & $Dotnet @restoreArgs
    if ($LASTEXITCODE) { throw 'Restore failed' }
    & $Dotnet build $checks -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed' }
    foreach ($suite in @('--unit', '--windows')) {
        & $Dotnet run --project $checks -c Release --no-build -- $suite --results (Join-Path $artifacts (($suite.TrimStart('-')) + '-results.json'))
        if ($LASTEXITCODE) { throw "Checks failed: $suite" }
    }

    $runtimeMode = if ($IncludeRuntime) { 'true' } else { 'false' }
    & $Dotnet publish $project -c Release --no-restore -r $rid --self-contained $runtimeMode -o $stageFramework
    if ($LASTEXITCODE) { throw 'Publish failed' }
    Copy-Item -Path (Join-Path $stageFramework '*') -Destination $stagePortable -Recurse -Force
    Set-Content -LiteralPath (Join-Path $stagePortable 'PortHarbor.portable') -Value 'Portable configuration: PortHarbor.ini beside the executable.' -Encoding UTF8

    $docs = @('README.md', 'THIRD-PARTY-NOTICES.md') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    $destinations = @($stageFramework, $stagePortable)
    foreach ($destination in $destinations) {
        Copy-Item -LiteralPath $docs -Destination $destination -Force
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $destination -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging\Start-PortHarbor.cmd') -Destination $stageFramework -Force
    Compress-Archive -Path (Join-Path $stagePortable '*') -DestinationPath $stagePortableZip -CompressionLevel Optimal
    Compress-Archive -Path (Join-Path $stageFramework '*') -DestinationPath $stageFrameworkZip -CompressionLevel Optimal

    New-Item -ItemType Directory -Force $backup | Out-Null
    foreach ($output in $targets) {
        if (Test-Path -LiteralPath $output.Target) {
            Move-Item -LiteralPath $output.Target -Destination $backup
        }
    }
    foreach ($output in $targets) {
        Move-Item -LiteralPath $output.Staged -Destination $output.Target
        $installed.Add($output.Target)
    }
    Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Portable: $portableZip"
    Write-Host "Installed layout (installer pending): $frameworkZip"
}
catch {
    foreach ($path in $installed) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    if (Test-Path -LiteralPath $backup) {
        foreach ($output in $targets) {
            $previous = Join-Path $backup (Split-Path $output.Target -Leaf)
            if (Test-Path -LiteralPath $previous) { Move-Item -LiteralPath $previous -Destination $output.Target -Force }
        }
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
}
