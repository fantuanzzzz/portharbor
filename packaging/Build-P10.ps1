[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [string]$OutputDirectory = '', [string]$Compiler = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $root '.build-home'
$env:NUGET_PACKAGES = Join-Path $root '.packages'
$project = Join-Path $root 'src\PortHarbor\PortHarbor.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$parsedVersion = [version]$version
$upgradeVersion = '{0}.{1}.{2}' -f $parsedVersion.Major, $parsedVersion.Minor, ($parsedVersion.Build + 1)
$rid = 'win-x64'
$artifacts = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'artifacts\releases' }
$runId = [guid]::NewGuid().ToString('N')
$staging = Join-Path $artifacts ('.p10-staging-' + $runId)
$reports = Join-Path $root ('artifacts\distribution-validation\' + $runId)
$payload = Join-Path $staging 'payload'
$portableName = "PortHarbor-$version-portable-$rid.exe"
$setupName = "PortHarbor-$version-setup-$rid.exe"
$success = $false

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host "P10: $Name"
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "P10 step failed: $Name (exit $LASTEXITCODE)" }
}

try {
    if (-not $Compiler) { $Compiler = & (Join-Path $PSScriptRoot 'Get-InnoSetup.ps1') }
    if (-not (Test-Path -LiteralPath $Compiler -PathType Leaf)) { throw "Inno Setup compiler not found: $Compiler" }
    New-Item -ItemType Directory -Force -Path $artifacts, $payload, $reports | Out-Null
    Invoke-Step 'locked restore' { & $Dotnet restore $project --runtime $rid --configfile (Join-Path $root 'NuGet.Config') --locked-mode }
    $identity = & (Join-Path $root 'scripts\Get-BuildIdentity.ps1') -Dotnet $Dotnet
    $identityArguments = @("-p:SourceRevisionId=$($identity.Commit)", "-p:PortHarborSourceDirty=$($identity.Dirty)",
        "-p:PortHarborSourceSnapshot=$($identity.SourceSnapshotSha256)", "-p:PortHarborBuildSdk=$($identity.SdkVersion)")
    Invoke-Step 'self-contained single-file publish' {
        & $Dotnet publish $project -c Release --no-restore -r $rid --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false @identityArguments -o $payload
    }
    $files = @(Get-ChildItem -LiteralPath $payload -Force)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'PortHarbor.exe') { throw "Single-file publish produced unexpected files: $($files.Name -join ', ')" }
    Copy-Item -LiteralPath (Join-Path $payload 'PortHarbor.exe') -Destination (Join-Path $staging $portableName)
    Set-Content -LiteralPath (Join-Path $payload 'PortHarbor.installed') -Value 'Installed mode: user data is kept in %LOCALAPPDATA%\PortHarbor.' -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'INSTALL-NOTES.txt') -Destination $payload
    Invoke-Step 'compile shipping installation wizard' {
        & $Compiler '/Qp' "/DPayload=$payload" "/DBuildOutput=$staging" "/DOutputName=$([IO.Path]::GetFileNameWithoutExtension($setupName))" "/DAppVersion=$version" (Join-Path $PSScriptRoot 'PortHarbor.iss')
    }
    # Validate a separate installer identity: never register/unregister the user's real PortHarbor.
    Invoke-Step 'compile isolated installer' {
        & $Compiler '/Qp' "/DPayload=$payload" "/DBuildOutput=$reports" '/DOutputName=setup-check' "/DTestId=$runId" "/DAppVersion=$version" (Join-Path $PSScriptRoot 'PortHarbor.iss')
    }
    Invoke-Step 'compile isolated upgrade' {
        & $Compiler '/Qp' "/DPayload=$payload" "/DBuildOutput=$reports" '/DOutputName=upgrade-check' "/DTestId=$runId" "/DAppVersion=$upgradeVersion" (Join-Path $PSScriptRoot 'PortHarbor.iss')
    }
    & (Join-Path $PSScriptRoot 'Test-Distribution.ps1') -PortableExecutable (Join-Path $staging $portableName) -Installer (Join-Path $reports 'setup-check.exe') -UpgradeInstaller (Join-Path $reports 'upgrade-check.exe') -TestId $runId -ReportDirectory $reports -InstallVersion $version -UpgradeVersion $upgradeVersion
    Remove-Item -LiteralPath (Join-Path $reports 'setup-check.exe'), (Join-Path $reports 'upgrade-check.exe') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $staging 'README.md')
    $manifestFiles = @($portableName, $setupName, 'README.md') | ForEach-Object {
        $file = Get-Item -LiteralPath (Join-Path $staging $_)
        [pscustomobject]@{ Name = $file.Name; Bytes = $file.Length; Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    }
    $validation = Get-Content -LiteralPath (Join-Path $reports 'distribution-results.json') -Raw | ConvertFrom-Json
    $runtimes = @(& (Join-Path $root 'scripts\Get-PublishedRuntime.ps1') -AssetsFile (Join-Path $root 'src\PortHarbor\obj\project.assets.json'))
    if ($runtimes.Count -eq 0) { throw 'Published runtime versions could not be identified.' }
    [pscustomobject]@{ SchemaVersion = 2; Version = $version; Runtime = $rid; RuntimePacks = $runtimes; SelfContained = $true;
        Build = $identity; GeneratedUtc = [DateTime]::UtcNow.ToString('o');
        Validation = [pscustomobject]@{ Passed = $validation.Passed; Failure = $validation.Failure; CleanWindowsVerified = $validation.CleanWindowsVerified };
        ValidationReport = "artifacts/distribution-validation/$runId/distribution-results.json"; Files = @($manifestFiles) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $staging 'release-manifest.json') -Encoding UTF8
    $targets = @($portableName, $setupName, 'README.md', 'release-manifest.json') | ForEach-Object {
        @{ Staged = Join-Path $staging $_; Target = Join-Path $artifacts $_; Directory = $false }
    }
    & (Join-Path $root 'scripts\Publish-StagedArtifacts.ps1') -StagingDirectory $staging -BackupDirectory (Join-Path $staging 'previous') -Targets $targets
    $success = $true
    Write-Host "Portable: $(Join-Path $artifacts $portableName)"
    Write-Host "Installer: $(Join-Path $artifacts $setupName)"
    Write-Host "Validation: $reports"
}
finally {
    # Keep a failed build out of the user-facing release folder. Incomplete rollback stays in place.
    if (-not $success) {
        $failedStaging = Join-Path $reports 'failed-staging'
        if ((Test-Path -LiteralPath $staging) -and (Test-Path -LiteralPath $reports) -and
            -not (Test-Path -LiteralPath (Join-Path $staging 'previous')) -and
            [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($staging)) -eq $artifacts -and
            [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($failedStaging)) -eq $reports) {
            try { Move-Item -LiteralPath $staging -Destination $failedStaging -ErrorAction Stop; $staging = $failedStaging }
            catch { Write-Warning "Could not move diagnostic staging: $($_.Exception.Message)" }
        }
        Write-Warning "Build did not publish. Existing releases are unchanged. Diagnostic staging: $staging; reports: $reports"
    }
}
