param(
    [string]$Version = "",
    [string]$Runtime = "win-x64",
    [bool]$SingleFile = $true,
    [bool]$SelfContained = $true,
    [string]$EngineProject = "",
    [string]$NpcapProject = "",
    [string]$ArdIrecBridgePath = "",
    [string]$ArdIrecSource = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "ArIED61850Tester.csproj"
$versionPropsPath = Join-Path $root "Directory.Build.props"

if ([string]::IsNullOrWhiteSpace($Version)) {
    if (-not (Test-Path $versionPropsPath)) {
        throw "Canonical version metadata was not found: $versionPropsPath"
    }

    [xml]$versionProps = Get-Content $versionPropsPath -Raw
    $Version = [string]$versionProps.Project.PropertyGroup.Version
}

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
if ($SingleFile -and -not $SelfContained) {
    throw "The public portable build must be self-contained so it can run without an installed .NET runtime."
}

$normalizedVersion = $Version.Trim()
if ($normalizedVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $normalizedVersion = $normalizedVersion.Substring(1)
}
if ($normalizedVersion -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:[-.][0-9A-Za-z.-]+)?$') {
    throw "Invalid version '$Version'. Use a value such as 1.6.19 or v1.6.19."
}
$numericVersion = "$($Matches.major).$($Matches.minor).$($Matches.patch).0"

if ([string]::IsNullOrWhiteSpace($ArdIrecBridgePath)) {
    $bridgeBuilder = Join-Path $root "scripts\build-ardirec-bridge.ps1"
    if (-not (Test-Path $bridgeBuilder -PathType Leaf)) {
        throw "ArdIrec bridge builder was not found: $bridgeBuilder"
    }

    $bridgeOutputDirectory = Join-Path $root "obj\ardirec-native\$Runtime"
    $builderArguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", $bridgeBuilder,
        "-OutputDirectory", $bridgeOutputDirectory
    )
    if (-not [string]::IsNullOrWhiteSpace($ArdIrecSource)) {
        $builderArguments += @("-ArdIrecSource", $ArdIrecSource)
    }

    $powershell = if ($env:OS -eq "Windows_NT") { "powershell" } else { "pwsh" }
    $bridgeBuilderOutput = & $powershell @builderArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned ArdIrec native bridge build failed with exit code $LASTEXITCODE."
    }
    $ArdIrecBridgePath = [string]($bridgeBuilderOutput | Select-Object -Last 1)
}

$ArdIrecBridgePath = [System.IO.Path]::GetFullPath($ArdIrecBridgePath.Trim())
if (-not (Test-Path $ArdIrecBridgePath -PathType Leaf)) {
    throw "Pinned ArdIrec native bridge was not found: $ArdIrecBridgePath"
}
Write-Host "==> Packaging ArdIrec native bridge: $ArdIrecBridgePath"

$outputRoot = Join-Path $root "dist"
$folderPublishDir = Join-Path $outputRoot "ARSAS-$normalizedVersion-$Runtime"
$singlePublishDir = Join-Path $outputRoot ".single-file-$normalizedVersion-$Runtime"
$folderZipPath = Join-Path $outputRoot "ARSAS-$normalizedVersion-$Runtime-portable.zip"
$singleExePath = Join-Path $outputRoot "ARSAS-$normalizedVersion-$Runtime-portable.exe"
$publishDir = if ($SingleFile) { $singlePublishDir } else { $folderPublishDir }

foreach ($path in @($publishDir, $folderZipPath, $singleExePath)) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "==> Restoring ARSAS"
dotnet restore $project `
    -p:ArIec61850Project="$EngineProject" `
    -p:ArIec61850NpcapProject="$NpcapProject"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

Write-Host "==> Publishing $normalizedVersion for $Runtime (single-file: $SingleFile, self-contained: $SelfContained)"
$publishArguments = @(
    "publish", $project,
    "-c", "Release",
    "-r", $Runtime,
    "--self-contained", $SelfContained.ToString().ToLowerInvariant(),
    "-p:PublishSingleFile=$SingleFile",
    "-p:PublishTrimmed=false",
    "-p:UseAppHost=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:Version=$normalizedVersion",
    "-p:AssemblyVersion=$numericVersion",
    "-p:FileVersion=$numericVersion",
    "-p:InformationalVersion=$normalizedVersion",
    "-p:ArIec61850Project=$EngineProject",
    "-p:ArIec61850NpcapProject=$NpcapProject",
    "-p:ArdIrecBridgePath=$ArdIrecBridgePath",
    "-p:ArdIrecBridgeRequired=true",
    "-o", $publishDir
)

if ($SingleFile) {
    # WPF and packet-capture dependencies use reflection, content files and native loading.
    # Keep trimming disabled and let the .NET bundle extract its runtime payload into the
    # current user's writable bundle cache. Distribution still consists of exactly one EXE.
    # ArdIrec's bridge is embedded as a managed resource and materialized by
    # ArdIrecEmbeddedBridgeBootstrap when the native COMTRADE workspace is first available.
    $publishArguments += "-p:IncludeNativeLibrariesForSelfExtract=true"
    $publishArguments += "-p:IncludeAllContentForSelfExtract=true"
    $publishArguments += "-p:EnableCompressionInSingleFile=true"
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $publishDir "ARSAS.exe"
if (-not (Test-Path $exe -PathType Leaf)) {
    throw "Published executable was not found: $exe"
}

if ($SingleFile) {
    $publishedFiles = @(Get-ChildItem $publishDir -Recurse -File)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].FullName -ne (Get-Item $exe).FullName) {
        $names = ($publishedFiles | ForEach-Object { $_.FullName }) -join ", "
        throw "Portable publish is not a real single-file output. Observed: $names"
    }

    Move-Item $exe $singleExePath -Force
    Remove-Item $singlePublishDir -Recurse -Force
    if (-not (Test-Path $singleExePath -PathType Leaf)) {
        throw "Versioned portable single EXE was not produced: $singleExePath"
    }

    Write-Host "==> Real portable single EXE with embedded ArdIrec bridge: $singleExePath"
    Write-Output $singleExePath
    exit 0
}

$requiredInstallerFiles = @(
    "AR.Iec61850.Transports.Npcap.dll",
    "SharpPcap.dll",
    "PacketDotNet.dll",
    "README.txt",
    "LICENSE",
    "COMMERCIAL-LICENSE.md",
    "TRADEMARK.md",
    "COPYRIGHT.md",
    "THIRD_PARTY_NOTICES.md",
    "NOTICE",
    "LICENSING.md",
    "engines\ARIEC61850.lock.json",
    "engines\ARDIREC.lock.json",
    "Tools\ArdIrec\ardirec_bridge.dll"
)
foreach ($runtimeFile in $requiredInstallerFiles) {
    $runtimePath = Join-Path $publishDir $runtimeFile
    if (-not (Test-Path $runtimePath -PathType Leaf)) {
        throw "Installer-source dependency was not published: $runtimePath"
    }
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $folderZipPath -CompressionLevel Optimal
Write-Host "==> Installer source executable: $exe"
Write-Host "==> Legacy folder ZIP for diagnostics: $folderZipPath"
Write-Output $publishDir
