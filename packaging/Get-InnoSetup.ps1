[CmdletBinding()]
param([switch]$Download)

$ErrorActionPreference = 'Stop'
$version = '6.7.4'
$tools = Join-Path (Split-Path -Parent $PSScriptRoot) '.build-home\tools'
$directory = Join-Path $tools "innosetup-$version"
$compiler = Join-Path $directory 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) { return $compiler }
if (-not $Download) { throw 'Inno Setup is missing. Run: pwsh -File packaging/Get-InnoSetup.ps1 -Download' }
New-Item -ItemType Directory -Force -Path $tools | Out-Null
$downloadPath = Join-Path $tools "innosetup-$version.exe"
if (-not (Test-Path -LiteralPath $downloadPath)) {
    Invoke-WebRequest -Uri "https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-$version.exe" -OutFile $downloadPath
}
$signature = Get-AuthenticodeSignature -FilePath $downloadPath
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )CN=Pyrsys B\.V\.(,|$)') {
    throw 'The Inno Setup download must have a valid Pyrsys B.V. Authenticode signature.'
}
# Official /PORTABLE=1 mode skips registration, file associations and an uninstaller.
$process = Start-Process -FilePath $downloadPath -ArgumentList @('/PORTABLE=1', '/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', '/TASKS=""', ('/DIR="{0}"' -f $directory)) -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Inno Setup extraction timed out.' }
    if ($process.ExitCode -ne 0) { throw "Inno Setup extraction failed: $($process.ExitCode)" }
} finally { $process.Dispose() }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'ISCC.exe was not extracted.' }
return $compiler
