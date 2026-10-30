param([Parameter(Mandatory)][string]$AssetsFile)
$ErrorActionPreference = 'Stop'
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json
foreach ($framework in $assets.project.frameworks.PSObject.Properties.Value) {
    foreach ($dependency in $framework.downloadDependencies) {
        if ($dependency.name -match '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.' -and
            $dependency.version -match '^\[([0-9.]+), \1\]$') {
            [pscustomobject]@{ Pack = $dependency.name; Version = $Matches[1] }
        }
    }
}
