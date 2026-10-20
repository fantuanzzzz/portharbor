param([string]$Dotnet = 'dotnet', [string]$PackageSource = '')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.packages'
$env:APPDATA = Join-Path $PSScriptRoot '.build-home\AppData'
New-Item -ItemType Directory -Force $env:APPDATA | Out-Null

$version = '0.1.0'
$rid = 'win-x64'
$project = Join-Path $PSScriptRoot 'src\PortHarbor\PortHarbor.csproj'
$checks = Join-Path $PSScriptRoot 'tests\PortHarbor.Checks\PortHarbor.Checks.csproj'
$artifacts = Join-Path $PSScriptRoot 'artifacts'
$portable = Join-Path $artifacts 'portable-win-x64'
$framework = Join-Path $artifacts 'framework-win-x64'
$portableZip = Join-Path $artifacts "PortHarbor-$version-portable-$rid.zip"
$frameworkZip = Join-Path $artifacts "PortHarbor-$version-framework-$rid.zip"
$config = Join-Path $PSScriptRoot 'NuGet.Config'

foreach ($path in @($portable, $framework)) {
    if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
foreach ($path in @($portableZip, $frameworkZip)) {
    if (Test-Path $path) { Remove-Item -LiteralPath $path -Force }
}

$restoreArgs = @('restore', $checks, '--runtime', $rid, '--configfile', $config)
if ($PackageSource) { $restoreArgs += @('--source', $PackageSource) }
& $Dotnet @restoreArgs
if ($LASTEXITCODE) { throw 'Restore failed' }
& $Dotnet build $checks -c Release --no-restore
if ($LASTEXITCODE) { throw 'Build failed' }
& $Dotnet run --project $checks -c Release --no-build -- (Join-Path $PSScriptRoot 'test-results.json')
if ($LASTEXITCODE) { throw 'Checks failed' }

& $Dotnet publish $project -c Release --no-restore -r $rid --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $portable
if ($LASTEXITCODE) { throw 'Portable publish failed' }

& $Dotnet publish $project -c Release --no-restore -r $rid --self-contained false -o $framework
if ($LASTEXITCODE) { throw 'Framework-dependent publish failed' }

$docs = @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md') | ForEach-Object { Join-Path $PSScriptRoot $_ }
foreach ($destination in @($portable, $framework)) {
    Copy-Item -LiteralPath $docs -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $destination -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging\Start-PortHarbor.cmd') -Destination $framework -Force

Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $portableZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $framework '*') -DestinationPath $frameworkZip -CompressionLevel Optimal
Write-Host "Portable: $portableZip"
Write-Host "Framework-dependent: $frameworkZip"
