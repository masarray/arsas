param(
    [string]$Version = "1.6.5",
    [string]$Runtime = "win-x64",
    [bool]$SingleFile = $true,
    [bool]$SelfContained = $true,
    [string]$EngineProject = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "ArIED61850Tester.csproj"

if ([string]::IsNullOrWhiteSpace($EngineProject)) {
    $EngineProject = Join-Path (Split-Path -Parent $root) "ARIEC61850\src\AR.Iec61850\AR.Iec61850.csproj"
}

if (-not (Test-Path $EngineProject)) {
    throw "ARIEC61850 engine project was not found: $EngineProject. Put the ArIED source folder beside the ARIEC61850 repository or pass -EngineProject with the full path."
}

$normalizedVersion = $Version.Trim()
if ($normalizedVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $normalizedVersion = $normalizedVersion.Substring(1)
}
if ($normalizedVersion -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') {
    throw "Invalid version '$Version'. Use a value such as 1.6.5 or v1.6.5."
}

$outputRoot = Join-Path $root "dist"
$publishDir = Join-Path $outputRoot "ArIED61850-$normalizedVersion-$Runtime"
$zipPath = Join-Path $outputRoot "ArIED61850-$normalizedVersion-$Runtime-portable.zip"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "==> Restoring ArIED 61850"
dotnet restore $project -p:ArIec61850Project="$EngineProject"

Write-Host "==> Publishing $normalizedVersion for $Runtime"
dotnet publish $project `
    -c Release `
    -r $Runtime `
    --self-contained $SelfContained `
    -p:PublishSingleFile=$SingleFile `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$normalizedVersion `
    -p:ArIec61850Project="$EngineProject" `
    -o $publishDir

$exe = Join-Path $publishDir "ArIED61850.exe"
if (-not (Test-Path $exe)) {
    throw "Published executable was not found: $exe"
}

Copy-Item (Join-Path $root "README.md") (Join-Path $publishDir "README.txt") -Force
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "==> Portable executable: $exe"
Write-Host "==> Portable ZIP: $zipPath"
