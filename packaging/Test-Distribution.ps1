[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PortableExecutable,
    [Parameter(Mandatory)][string]$Installer,
    [Parameter(Mandatory)][string]$UpgradeInstaller,
    [Parameter(Mandatory)][string]$TestId,
    [Parameter(Mandatory)][string]$ReportDirectory,
    [Parameter(Mandatory)][string]$InstallVersion,
    [Parameter(Mandatory)][string]$UpgradeVersion
)
$ErrorActionPreference = 'Stop'
if ($TestId -notmatch '^[a-f0-9]{32}$') { throw 'Installer test identity must be a 32-character random hexadecimal ID.' }
$PortableExecutable = [IO.Path]::GetFullPath($PortableExecutable)
$Installer = [IO.Path]::GetFullPath($Installer)
$UpgradeInstaller = [IO.Path]::GetFullPath($UpgradeInstaller)
$reportRoot = [IO.Path]::GetFullPath($ReportDirectory)
$fixture = Join-Path $reportRoot '中文 路径 fixture'
$portable = Join-Path $fixture 'portable'
$installed = Join-Path $fixture 'installed app'
$data = Join-Path $fixture 'installed data'
$key = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\PH.Check.${TestId}_is1"
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "PortHarbor Packaging Check $TestId\PortHarbor.lnk"
$checks = [Collections.Generic.List[string]]::new()
$failure = $null

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
    $checks.Add($Message); Write-Host "PASS: $Message"
}
function Run([string]$Exe, [string[]]$Arguments, [int]$Timeout = 60000) {
    $process = Start-Process -FilePath $Exe -ArgumentList $Arguments -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit($Timeout)) { $process.Kill(); $process.WaitForExit(); throw "Process timeout: $Exe" }
        if ($process.ExitCode -ne 0) { throw "Process failed ($($process.ExitCode)): $Exe" }
    } finally { $process.Dispose() }
}
function Get-DataHashes([string]$Directory) {
    if (Test-Path -LiteralPath $Directory) {
        @(Get-ChildItem -LiteralPath $Directory -File -Recurse | Sort-Object FullName | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash }) -join "`n"
    } else { '' }
}

try {
    if ((Test-Path -LiteralPath $fixture) -or (Test-Path -LiteralPath $key) -or (Test-Path -LiteralPath $shortcut)) { throw 'Packaging check paths/identity must be new.' }
    New-Item -ItemType Directory -Path $portable, $data -Force | Out-Null
    $exe = Join-Path $portable (Split-Path -Leaf $PortableExecutable)
    Copy-Item -LiteralPath $PortableExecutable -Destination $exe
    Assert (@(Get-ChildItem -LiteralPath $portable -Force).Count -eq 1) 'Portable delivery contains exactly one executable'
    # No --portable, no --data-dir, no marker: test the actual double-click storage default.
    Run $exe @('--smoke')
    $ini = Join-Path $portable 'PortHarbor.ini'
    Assert (Test-Path -LiteralPath $ini) 'Portable first start creates executable-adjacent INI without arguments'
    $hash = (Get-FileHash -LiteralPath $ini).Hash
    Run $exe @('--smoke')
    Assert ((Get-FileHash -LiteralPath $ini).Hash -eq $hash) 'Portable restart preserves configuration'
    $originalExeHash = (Get-FileHash -LiteralPath $exe).Hash
    $rejected = $false
    try { Run $Installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="{0}"' -f $portable), ('/LOG="{0}"' -f (Join-Path $reportRoot 'rejected-install.log'))) }
    catch { $rejected = $true }
    Assert ($rejected -and -not (Test-Path -LiteralPath $key) -and (Get-FileHash -LiteralPath $ini).Hash -eq $hash -and
        (Get-FileHash -LiteralPath $exe).Hash -eq $originalExeHash) 'Silent install rejects an existing portable data directory without modifying it'
    $installArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/DIR="{0}"' -f $installed), ('/LOG="{0}"' -f (Join-Path $reportRoot 'install.log')))
    Run $Installer $installArguments
    Assert ((Test-Path -LiteralPath (Join-Path $installed 'PortHarbor.exe')) -and (Test-Path -LiteralPath (Join-Path $installed 'PortHarbor.installed'))) 'Installer deploys program and installed-mode marker'
    $registration = Get-ItemProperty -LiteralPath $key
    Assert ($registration.InstallLocation.TrimEnd('\') -eq $installed -and $registration.DisplayVersion -eq $InstallVersion) 'Installer registers only the isolated per-user uninstall identity'
    Assert (Test-Path -LiteralPath $shortcut) 'Installer creates a Start Menu shortcut'
    $shell = New-Object -ComObject WScript.Shell
    try {
        $link = $shell.CreateShortcut($shortcut)
        Assert ($link.TargetPath -eq (Join-Path $installed 'PortHarbor.exe')) 'Start Menu shortcut targets the installed executable'
    } finally { [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null }
    Run (Join-Path $installed 'PortHarbor.exe') @('--smoke', '--data-dir', ('"{0}"' -f $data))
    Assert (-not (Test-Path -LiteralPath (Join-Path $installed 'PortHarbor.ini')) -and -not (Test-Path -LiteralPath (Join-Path $data 'PortHarbor.ini'))) 'Installed single-file startup keeps program and portable data separate'
    $sentinel = Join-Path $data 'settings.json'
    Set-Content -LiteralPath $sentinel -Value '{"SchemaVersion":1,"Groups":[],"Sessions":[]}' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $data 'credential-preservation.sentinel') -Value 'test credential preservation' -Encoding UTF8
    $dataHashes = Get-DataHashes $data
    Set-Content -LiteralPath (Join-Path $installed 'user-owned.txt') -Value 'Unrecognized file must survive uninstall' -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $installed 'INSTALL-NOTES.txt') -Value 'Old program file to be replaced during upgrade' -Encoding UTF8
    Run $UpgradeInstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="{0}"' -f (Join-Path $reportRoot 'upgrade.log')))
    $upgraded = Get-ItemProperty -LiteralPath $key
    Assert ($upgraded.DisplayVersion -eq $UpgradeVersion -and $upgraded.InstallLocation.TrimEnd('\') -eq $installed) 'Upgrade reuses the recorded install directory and updates its version'
    Assert ((Get-Content -LiteralPath (Join-Path $installed 'INSTALL-NOTES.txt') -Raw) -notmatch '^Old program file') 'Upgrade replaces the previous managed program files'
    Assert ((Get-DataHashes $data) -eq $dataHashes) 'Upgrade preserves user configuration and credentials'
    Run (Join-Path $installed 'PortHarbor.exe') @('--smoke', '--data-dir', ('"{0}"' -f $data))
    Assert ((Get-DataHashes $data) -eq $dataHashes) 'Upgraded application opens existing installed data without changes'
    Run (Join-Path $installed 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="{0}"' -f (Join-Path $reportRoot 'uninstall.log')))
    Assert (-not (Test-Path -LiteralPath (Join-Path $installed 'PortHarbor.exe')) -and -not (Test-Path -LiteralPath $key) -and -not (Test-Path -LiteralPath $shortcut)) 'Uninstall removes owned program files, registration and shortcut'
    Assert ((Get-DataHashes $data) -eq $dataHashes -and (Test-Path -LiteralPath (Join-Path $installed 'user-owned.txt'))) 'Uninstall preserves configuration, credentials and unrecognized files'
}
catch { $failure = $_.Exception.Message; throw }
finally {
    if ($failure -and (Test-Path -LiteralPath (Join-Path $installed 'unins000.exe'))) {
        try { Run (Join-Path $installed 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') }
        catch { Write-Warning "Test installation cleanup failed: $($_.Exception.Message)" }
    }
    if (-not $failure -and (Test-Path -LiteralPath $exe)) { Remove-Item -LiteralPath $exe -Force }
    [pscustomobject]@{ Passed = $checks.Count; Failure = $failure; Checks = @($checks); TestId = $TestId;
        InstallVersion = $InstallVersion; SimulatedUpgradeVersion = $UpgradeVersion; CleanWindowsVerified = $false } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reportRoot 'distribution-results.json') -Encoding UTF8
}
