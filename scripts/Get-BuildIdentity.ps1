[CmdletBinding()]
param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40,64}$') { throw 'Cannot determine source commit.' }
    $status = @(& git status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source worktree status.' }
    $sdk = (& $Dotnet --version | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine .NET SDK version.' }
    $sourceFiles = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source snapshot.' }
    $snapshot = [Text.StringBuilder]::new()
    $locks = @()
    foreach ($relative in $sourceFiles) {
        if (-not (Test-Path -LiteralPath $relative -PathType Leaf)) { continue }
        $fileHasher = [Security.Cryptography.SHA256]::Create()
        $stream = [IO.File]::OpenRead((Join-Path $root $relative))
        try { $hash = [BitConverter]::ToString($fileHasher.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose(); $fileHasher.Dispose() }
        [void]$snapshot.Append($relative.Replace('\', '/')).Append(':').Append($hash).Append("`n")
        if ([IO.Path]::GetFileName($relative) -eq 'packages.lock.json') {
            $locks += [pscustomobject]@{ Path = $relative.Replace('\', '/'); Sha256 = $hash }
        }
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $snapshotHash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($snapshot.ToString()))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    [pscustomobject]@{ Commit = $commit; Dirty = $status.Count -gt 0; SourceSnapshotSha256 = $snapshotHash; SdkVersion = $sdk; DependencyLocks = $locks }
}
finally { Pop-Location }
