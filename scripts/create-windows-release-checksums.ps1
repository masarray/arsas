param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Runtime = "win-x64",
    [string]$DistDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$normalizedVersion = $Version.Trim()
if ($normalizedVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $normalizedVersion = $normalizedVersion.Substring(1)
}
if ($normalizedVersion -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "Invalid release version '$Version'."
}

if ([string]::IsNullOrWhiteSpace($DistDirectory)) {
    $DistDirectory = Join-Path $root "dist"
}
$DistDirectory = [System.IO.Path]::GetFullPath($DistDirectory)

$assets = @(
    (Join-Path $DistDirectory "ArIED61850-$normalizedVersion-$Runtime-portable.zip")
    (Join-Path $DistDirectory "ArIED61850-$normalizedVersion-$Runtime-setup.exe")
)

$lines = foreach ($asset in $assets) {
    if (-not (Test-Path $asset -PathType Leaf)) {
        throw "Release asset was not found: $asset"
    }

    $hash = Get-FileHash -Path $asset -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($asset))"
}

$outputPath = Join-Path $DistDirectory "SHA256SUMS.txt"
$lines | Set-Content -Path $outputPath -Encoding ascii
Write-Host "==> Checksums: $outputPath"
Get-Content $outputPath | ForEach-Object { Write-Host "    $_" }
Write-Output $outputPath
