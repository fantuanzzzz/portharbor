param([string]$Dotnet = 'dotnet', [string]$PackageSource = '', [switch]$IncludeRuntime, [switch]$ExtendedBaseline, [string]$OutputDirectory = '', [switch]$SimulateFailureAfterInstall, [switch]$P10)
$ErrorActionPreference = 'Stop'
if ($P10) {
    & (Join-Path $PSScriptRoot 'packaging\Build-P10.ps1') -Dotnet $Dotnet -OutputDirectory $OutputDirectory
    return
}
$userAppData = $env:APPDATA
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.packages'
$env:APPDATA = Join-Path $PSScriptRoot '.build-home\AppData'
New-Item -ItemType Directory -Force $env:APPDATA | Out-Null

$version = ([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\PortHarbor\PortHarbor.csproj') -Raw)).Project.PropertyGroup.Version
$rid = 'win-x64'
$project = Join-Path $PSScriptRoot 'src\PortHarbor\PortHarbor.csproj'
$checks = Join-Path $PSScriptRoot 'tests\PortHarbor.Checks\PortHarbor.Checks.csproj'
$applicationChecks = Join-Path $PSScriptRoot 'tests\PortHarbor.Application.Checks\PortHarbor.Application.Checks.csproj'
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
$smokeResults = [System.Collections.Generic.List[object]]::new()
$timings = [System.Collections.Generic.List[object]]::new()
$extraReports = [System.Collections.Generic.List[string]]::new()

function Invoke-TimedStep([string]$Name, [scriptblock]$Command) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    & $Command
    $exitCode = $LASTEXITCODE
    $watch.Stop()
    $timings.Add([pscustomobject]@{ Name = $Name; DurationMs = $watch.ElapsedMilliseconds; ExitCode = $exitCode })
    if ($exitCode -ne 0) { throw "Build step failed: $Name (exit $exitCode)" }
}

function Invoke-PublishedSmoke([string]$Executable, [string[]]$StartupArguments, [string]$Mode) {
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $Executable -ArgumentList $StartupArguments -PassThru -WindowStyle Hidden
    try {
        if (-not $process.WaitForExit(30000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "Published application smoke timed out: $Mode"
        }
        $watch.Stop()
        $smokeResults.Add([pscustomobject]@{ Mode = $Mode; DurationMs = $watch.ElapsedMilliseconds; ExitCode = $process.ExitCode })
        if ($process.ExitCode -ne 0) { throw "Published application smoke failed: $Mode (exit $($process.ExitCode))" }
    }
    finally { $process.Dispose() }
}

function Invoke-ExtendedChecks {
    $buildAppData = $env:APPDATA
    $env:APPDATA = $userAppData
    try {
        $python = Get-Command python -ErrorAction SilentlyContinue
        if ($null -eq $python) { throw 'Extended baseline requires Python 3.12+ and tests/requirements.txt.' }
        & $python.Source '-c' 'import paramiko, pyftpdlib' 2>$null
        $dependencyExit = $LASTEXITCODE
        if ($dependencyExit -ne 0) { throw 'Extended baseline dependencies missing; install them with: python -m pip install -r tests/requirements.txt' }

        $fixtureRoot = Join-Path $staging 'protocol-fixture'
    $serversPath = Join-Path $fixtureRoot 'servers.json'
    $stdout = Join-Path $staging 'protocol-fixture-out.log'
    $stderr = Join-Path $staging 'protocol-fixture-error.log'
    New-Item -ItemType Directory -Force $fixtureRoot | Out-Null
    $fixture = Start-Process -FilePath $python.Source -ArgumentList @((Join-Path $PSScriptRoot 'tests\protocol_servers.py'), '--root', ('"{0}"' -f $fixtureRoot)) -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        try {
        $readyWatch = [System.Diagnostics.Stopwatch]::StartNew()
        for ($attempt = 0; $attempt -lt 60 -and -not (Test-Path -LiteralPath $serversPath); $attempt++) {
            if ($fixture.HasExited) { throw "Protocol fixture exited early: $(Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue)" }
            Start-Sleep -Seconds 1
        }
        $readyWatch.Stop()
        if (-not (Test-Path -LiteralPath $serversPath)) { throw "Protocol fixture did not become ready: $(Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue)" }
        $timings.Add([pscustomobject]@{ Name = 'protocol-fixture-startup'; DurationMs = $readyWatch.ElapsedMilliseconds; ExitCode = 0 })
        $integrationReport = Join-Path $artifacts 'integration-results.json'
        try {
            Invoke-TimedStep 'protocol-loopback' { & $Dotnet run --project $checks -c Release --no-build -- --integration $serversPath --results $integrationReport }
        }
        catch {
            foreach ($log in @($stdout, $stderr)) {
                if (Test-Path -LiteralPath $log) {
                    Copy-Item -LiteralPath $log -Destination (Join-Path $artifacts (Split-Path $log -Leaf)) -Force
                }
            }
            throw
        }
        $extraReports.Add($integrationReport)
    }
        finally {
            if (-not $fixture.HasExited) { Stop-Process -Id $fixture.Id -Force }
            $fixture.Dispose()
        }
    }
    finally {
        $env:APPDATA = $buildAppData
    }

    $uiImage = Join-Path $artifacts 'baseline-ui.png'
    $uiReport = Join-Path $artifacts 'ui-results.json'
    Invoke-TimedStep 'winforms-ui' { & $Dotnet run --project $checks -c Release --no-build -- --ui $uiImage --results $uiReport }
    if (-not (Test-Path -LiteralPath $uiImage -PathType Leaf)) { throw 'WinForms UI check did not produce a screenshot.' }
    $extraReports.Add($uiReport)

    $shellOutput = Join-Path $artifacts 'ui-shell-results.txt'
    Invoke-TimedStep 'conpty-ui-shell' { & $Dotnet run --project $checks -c Release --no-build -- --ui-shell $shellOutput }
    if (-not (Select-String -LiteralPath $shellOutput -Pattern 'CONPTY_OK' -Quiet)) { throw 'ConPTY UI shell check did not produce its expected marker.' }
    $smokeResults.Add([pscustomobject]@{ Mode = 'conpty-ui-shell-marker'; ExitCode = 0 })
}

try {
    New-Item -ItemType Directory -Force $artifacts, $stagePortable, $stageFramework | Out-Null
    Invoke-TimedStep 'diff-check' { & git diff --check }
    $standardTests = Join-Path $PSScriptRoot 'tests\PortHarbor.UnitTests\PortHarbor.UnitTests.csproj'
    $standardRestore = @('restore', $standardTests, '--configfile', $config, '--locked-mode')
    if ($PackageSource) { $standardRestore += @('--source', $PackageSource) }
    Invoke-TimedStep 'standard-restore' { & $Dotnet @standardRestore }
    Invoke-TimedStep 'standard-tests' { & $Dotnet test $standardTests -c Release --no-restore --logger 'trx;LogFileName=core.trx' --results-directory (Join-Path $artifacts 'standard') --collect 'XPlat Code Coverage' --settings (Join-Path $PSScriptRoot 'tests\coverage.runsettings') }
    $applicationRestoreArgs = @('restore', $applicationChecks, '--configfile', $config, '--locked-mode')
    if ($PackageSource) { $applicationRestoreArgs += @('--source', $PackageSource) }
    Invoke-TimedStep 'application-restore' { & $Dotnet @applicationRestoreArgs }
    Invoke-TimedStep 'application-build' { & $Dotnet build $applicationChecks -c Release --no-restore }
    Invoke-TimedStep 'application-checks' { & $Dotnet run --project $applicationChecks -c Release --no-build -- --results (Join-Path $artifacts 'application-results.json') }
    $restoreArgs = @('restore', $checks, '--runtime', $rid, '--configfile', $config, '--locked-mode')
    if ($PackageSource) { $restoreArgs += @('--source', $PackageSource) }
    Invoke-TimedStep 'windows-restore' { & $Dotnet @restoreArgs }
    & (Join-Path $PSScriptRoot 'scripts\Check-CodeStyle.ps1') -Dotnet $Dotnet
    Invoke-TimedStep 'windows-build' { & $Dotnet build $checks -c Release --no-restore }
    foreach ($suite in @('--unit', '--windows')) {
        $suiteName = $suite.TrimStart('-')
        Invoke-TimedStep "${suiteName}-checks" { & $Dotnet run --project $checks -c Release --no-build -- $suite --results (Join-Path $artifacts "$suiteName-results.json") }
    }
    if ($ExtendedBaseline) { Invoke-ExtendedChecks }

    $runtimeMode = if ($IncludeRuntime) { 'true' } else { 'false' }
    $identity = & (Join-Path $PSScriptRoot 'scripts\Get-BuildIdentity.ps1') -Dotnet $Dotnet
    $identityArguments = @("-p:SourceRevisionId=$($identity.Commit)", "-p:PortHarborSourceDirty=$($identity.Dirty)",
        "-p:PortHarborSourceSnapshot=$($identity.SourceSnapshotSha256)", "-p:PortHarborBuildSdk=$($identity.SdkVersion)")
    Invoke-TimedStep 'publish' { & $Dotnet publish $project -c Release --no-restore -r $rid --self-contained $runtimeMode @identityArguments -o $stageFramework }
    $smokeData = Join-Path $staging 'smoke-data'
    New-Item -ItemType Directory -Force $smokeData | Out-Null
    Invoke-PublishedSmoke -Executable (Join-Path $stageFramework 'PortHarbor.exe') -StartupArguments @('--smoke', '--installed', '--data-dir', ('"{0}"' -f $smokeData)) -Mode 'installed-isolated'
    Copy-Item -Path (Join-Path $stageFramework '*') -Destination $stagePortable -Recurse -Force
    Set-Content -LiteralPath (Join-Path $stagePortable 'PortHarbor.portable') -Value 'Portable configuration: PortHarbor.ini beside the executable.' -Encoding UTF8
    Invoke-PublishedSmoke -Executable (Join-Path $stagePortable 'PortHarbor.exe') -StartupArguments @('--smoke') -Mode 'portable-default-first-start'
    $portableIni = Join-Path $stagePortable 'PortHarbor.ini'
    if (-not (Test-Path -LiteralPath $portableIni -PathType Leaf)) { throw 'Default portable startup did not create PortHarbor.ini' }
    Invoke-PublishedSmoke -Executable (Join-Path $stagePortable 'PortHarbor.exe') -StartupArguments @('--smoke') -Mode 'portable-default-restart'
    $smokeResults | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'startup-smoke-results.json') -Encoding UTF8
    # Only remove the empty smoke configuration from this build's private staging directory.
    foreach ($smokeFile in @($portableIni, "$portableIni.bak", "$portableIni.tmp", (Join-Path $stagePortable '.portharbor.lock'))) {
        if (Test-Path -LiteralPath $smokeFile) { Remove-Item -LiteralPath $smokeFile -Force }
    }

    $docs = @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    $destinations = @($stageFramework, $stagePortable)
    foreach ($destination in $destinations) {
        Copy-Item -LiteralPath $docs -Destination $destination -Force
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $destination -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging\Start-PortHarbor.cmd') -Destination $stageFramework -Force
    Invoke-TimedStep 'portable-archive' { Compress-Archive -Path (Join-Path $stagePortable '*') -DestinationPath $stagePortableZip -CompressionLevel Optimal }
    Invoke-TimedStep 'installed-archive' { Compress-Archive -Path (Join-Path $stageFramework '*') -DestinationPath $stageFrameworkZip -CompressionLevel Optimal }

    $reportNames = @('application-results.json', 'unit-results.json', 'windows-results.json') + @($extraReports | ForEach-Object { Split-Path $_ -Leaf })
    $checkReports = foreach ($reportName in $reportNames) {
        $reportPath = Join-Path $artifacts $reportName
        if (-not (Test-Path -LiteralPath $reportPath)) { throw "Missing check report: $reportName" }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        [pscustomobject]@{ Name = $reportName; Passed = $report.passed; Failed = if ($report.failure) { 1 } else { 0 }; Failure = $report.failure }
    }
    $sdkVersion = (& $Dotnet --version | Select-Object -Last 1).ToString().Trim()
    [pscustomobject]@{
        SchemaVersion = 1
        GeneratedAt = [DateTimeOffset]::Now
        SdkVersion = $sdkVersion
        Build = $identity
        RuntimePacks = @(& (Join-Path $PSScriptRoot 'scripts\Get-PublishedRuntime.ps1') -AssetsFile (Join-Path $PSScriptRoot 'src\PortHarbor\obj\project.assets.json'))
        StandardTests = [pscustomobject]@{ Report = 'standard/core.trx'; Coverage = 'standard/*/coverage.cobertura.xml'; Scope = 'Domain/Application only' }
        Configuration = if ($IncludeRuntime) { 'Release self-contained win-x64' } else { 'Release framework-dependent win-x64' }
        ExtendedBaseline = [bool]$ExtendedBaseline
        Steps = @($timings)
        Checks = @($checkReports)
        Smoke = @($smokeResults)
        Coverage = [pscustomobject]@{
            Measurement = 'check-count, not source-code coverage'
            Included = @('Application checks', 'core Windows checks', 'published installed/portable startup smoke')
            SeparateEntrypoints = if ($ExtendedBaseline) { @('none; all available automated Windows entries included') } else { @('protocol loopback integration', 'WinForms UI rendering', 'ConPTY UI shell (enable with -ExtendedBaseline)') }
            NotMeasured = @('Windows/UI/adapter source coverage', 'real serial devices', 'real RDP login', 'FTPS and production-server interoperability')
        }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifacts 'engineering-baseline.json') -Encoding UTF8

    $publish = Join-Path $PSScriptRoot 'scripts\Publish-StagedArtifacts.ps1'
    $publishTargets = @($targets | ForEach-Object { @{ Staged = $_.Staged; Target = $_.Target } })
    $publishArgs = @{ StagingDirectory = $staging; BackupDirectory = $backup; Targets = $publishTargets }
    if ($SimulateFailureAfterInstall) { $publishArgs.FailAfterInstallCount = $publishTargets.Count }
    & $publish @publishArgs
    Write-Host "Portable: $portableZip"
    Write-Host "Installed-layout ZIP (installation wizard: build.ps1 -P10): $frameworkZip"
}
catch { throw }
finally {
    if ((Test-Path -LiteralPath $staging) -and -not (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}
