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

$installer = Join-Path $DistDirectory "ARSAS-$normalizedVersion-$Runtime-setup.exe"
$portable = Join-Path $DistDirectory "ARSAS-$normalizedVersion-$Runtime-portable.exe"

if (-not (Test-Path $installer -PathType Leaf)) {
    throw "Release installer was not found: $installer"
}

# The standalone installer-validation workflow intentionally builds only the
# installer. The full release workflow builds both assets before invoking this
# script, so the portable EXE is included whenever it is present.
$assets = [System.Collections.Generic.List[string]]::new()
$assets.Add($installer)
if (Test-Path $portable -PathType Leaf) {
    $assets.Add($portable)
}
else {
    Write-Host "==> Portable asset is not present in this installer-only validation run."
}

$lines = foreach ($asset in $assets) {
    $hash = Get-FileHash -Path $asset -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($asset))"
}

$outputPath = Join-Path $DistDirectory "SHA256SUMS.txt"
$lines | Set-Content -Path $outputPath -Encoding ascii
Write-Host "==> Checksums: $outputPath"
Get-Content $outputPath | ForEach-Object { Write-Host "    $_" }
Write-Output $outputPath