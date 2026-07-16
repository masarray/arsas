param(
    [string]$Version = "1.6.16",
    [string]$Runtime = "win-x64",
    [bool]$SingleFile = $false,
    [bool]$SelfContained = $true,
    [string]$EngineProject = "",
    [string]$NpcapProject = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "ArIED61850Tester.csproj"

if ([string]::IsNullOrWhiteSpace($EngineProject)) {
    $EngineProject = Join-Path (Split-Path -Parent $root) "ARIEC61850\src\AR.Iec61850\AR.Iec61850.csproj"
}

if ([string]::IsNullOrWhiteSpace($NpcapProject)) {
    $engineDirectory = Split-Path -Parent $EngineProject
    $engineSourceRoot = Split-Path -Parent $engineDirectory
    $NpcapProject = Join-Path $engineSourceRoot "AR.Iec61850.Transports.Npcap\AR.Iec61850.Transports.Npcap.csproj"
}

if (-not (Test-Path $EngineProject)) {
    throw "ARIEC61850 engine project was not found: $EngineProject. Put the ARSAS source folder beside the ARIEC61850 repository or pass -EngineProject with the full path."
}
if (-not (Test-Path $NpcapProject)) {
    throw "ARIEC61850 Npcap transport project was not found: $NpcapProject. Pass -NpcapProject with the full path to AR.Iec61850.Transports.Npcap.csproj."
}

$normalizedVersion = $Version.Trim()
if ($normalizedVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $normalizedVersion = $normalizedVersion.Substring(1)
}
if ($normalizedVersion -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:[-.][0-9A-Za-z.-]+)?$') {
    throw "Invalid version '$Version'. Use a value such as 1.6.16 or v1.6.16."
}
$numericVersion = "$($Matches.major).$($Matches.minor).$($Matches.patch).0"

$outputRoot = Join-Path $root "dist"
$publishDir = Join-Path $outputRoot "ARSAS-$normalizedVersion-$Runtime"
$zipPath = Join-Path $outputRoot "ARSAS-$normalizedVersion-$Runtime-portable.zip"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "==> Restoring ARSAS"
dotnet restore $project `
    -p:ArIec61850Project="$EngineProject" `
    -p:ArIec61850NpcapProject="$NpcapProject"

Write-Host "==> Publishing $normalizedVersion for $Runtime (single-file: $SingleFile)"
$publishArguments = @(
    "publish", $project,
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", $SelfContained.ToString().ToLowerInvariant(),
    "-p:PublishSingleFile=$SingleFile",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:Version=$normalizedVersion",
    "-p:AssemblyVersion=$numericVersion",
    "-p:FileVersion=$numericVersion",
    "-p:InformationalVersion=$normalizedVersion",
    "-p:ArIec61850Project=$EngineProject",
    "-p:ArIec61850NpcapProject=$NpcapProject",
    "-o", $publishDir
)

if ($SingleFile) {
    # Supported for controlled diagnostics only. The release portable package defaults to
    # multi-file so SharpPcap and the ARIEC61850 Npcap transport remain normal loadable
    # assemblies and startup does not pay the single-file extraction cost.
    $publishArguments += "-p:IncludeNativeLibrariesForSelfExtract=true"
    $publishArguments += "-p:IncludeAllContentForSelfExtract=true"
    $publishArguments += "-p:EnableCompressionInSingleFile=true"
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $publishDir "ARSAS.exe"
if (-not (Test-Path $exe)) {
    throw "Published executable was not found: $exe"
}

if (-not $SingleFile) {
    $requiredGooseFiles = @(
        "AR.Iec61850.Transports.Npcap.dll",
        "SharpPcap.dll",
        "PacketDotNet.dll"
    )
    foreach ($runtimeFile in $requiredGooseFiles) {
        $runtimePath = Join-Path $publishDir $runtimeFile
        if (-not (Test-Path $runtimePath)) {
            throw "GOOSE runtime dependency was not published: $runtimePath"
        }
    }
}

Copy-Item (Join-Path $root "README.md") (Join-Path $publishDir "README.txt") -Force

# Current release packages carry one public software license: GPL-3.0-or-later.
$legalFiles = @("LICENSE", "COMMERCIAL-LICENSE.md", "TRADEMARK.md", "COPYRIGHT.md", "THIRD_PARTY_NOTICES.md", "NOTICE")
foreach ($legalFile in $legalFiles) {
    $sourceLegalFile = Join-Path $root $legalFile
    if (-not (Test-Path $sourceLegalFile)) {
        throw "Required legal file was not found: $sourceLegalFile"
    }

    Copy-Item $sourceLegalFile (Join-Path $publishDir $legalFile) -Force
}

$licensingGuide = Join-Path $root "docs\LICENSING.md"
if (-not (Test-Path $licensingGuide)) {
    throw "Required licensing guide was not found: $licensingGuide"
}
Copy-Item $licensingGuide (Join-Path $publishDir "LICENSING.md") -Force

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "==> Portable executable: $exe"
Write-Host "==> Portable ZIP: $zipPath"
Write-Host "==> Keep the complete extracted folder together; GOOSE capture depends on the published SharpPcap and Npcap transport assemblies."
