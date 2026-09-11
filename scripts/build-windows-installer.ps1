param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Runtime = "win-x64",
    [string]$PublishedDirectory = "",
    [string]$OutputDirectory = "",
    [string]$InnoCompiler = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$normalizedVersion = $Version.Trim()
if ($normalizedVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $normalizedVersion = $normalizedVersion.Substring(1)
}
if ($normalizedVersion -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:[-.][0-9A-Za-z.-]+)?$') {
    throw "Invalid version '$Version'. Use a semantic version such as 1.6.16 or v1.6.16."
}

$numericVersion = "$($Matches.major).$($Matches.minor).$($Matches.patch).0"
if ([string]::IsNullOrWhiteSpace($PublishedDirectory)) {
    $PublishedDirectory = Join-Path $root "dist\ARSAS-$normalizedVersion-$Runtime"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root "dist"
}

$PublishedDirectory = [System.IO.Path]::GetFullPath($PublishedDirectory)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$installerDefinition = Join-Path $root "installer\ArIED61850.iss"
$ardirecLockPath = Join-Path $root "engines\ARDIREC.lock.json"

if (-not (Test-Path $PublishedDirectory -PathType Container)) {
    throw "Published application folder was not found: $PublishedDirectory"
}
if (-not (Test-Path $installerDefinition -PathType Leaf)) {
    throw "Inno Setup definition was not found: $installerDefinition"
}
if (-not (Test-Path $ardirecLockPath -PathType Leaf)) {
    throw "ArdIrec integration lock was not found: $ardirecLockPath"
}

$ardirecLock = Get-Content $ardirecLockPath -Raw | ConvertFrom-Json
if ($ardirecLock.schema -ne 3 -or
    $ardirecLock.repository -notmatch '^[^/]+/[^/]+$' -or
    $ardirecLock.commit -notmatch '^[0-9a-f]{40}$' -or
    $ardirecLock.bridge.abi -ne 1 -or
    $ardirecLock.bridge.relativeLibrary -ne 'Tools/ArdIrec/ardirec_bridge.dll' -or
    $ardirecLock.bridge.mode -ne 'native-only') {
    throw "ArdIrec P1D.5 integration lock is invalid. The installer requires native-only bridge ABI 1."
}

$requiredFiles = @(
    "ARSAS.exe",
    "AR.Iec61850.Transports.Npcap.dll",
    "SharpPcap.dll",
    "PacketDotNet.dll",
    "LICENSE",
    "README.txt",
    "Tools\ArdIrec\ardirec_bridge.dll"
)
foreach ($file in $requiredFiles) {
    $candidate = Join-Path $PublishedDirectory $file
    if (-not (Test-Path $candidate -PathType Leaf)) {
        throw "Installer source is incomplete. Required file was not found: $candidate"
    }
}

$forbiddenDesktopFallbackFiles = @(
    "Tools\ArdIrec\ardirec.exe",
    "Tools\ArdIrec\Qt6Core.dll",
    "Tools\ArdIrec\Qt6Gui.dll",
    "Tools\ArdIrec\Qt6Qml.dll",
    "Tools\ArdIrec\Qt6Quick.dll",
    "Tools\ArdIrec\platforms\qwindows.dll"
)
foreach ($file in $forbiddenDesktopFallbackFiles) {
    $candidate = Join-Path $PublishedDirectory $file
    if (Test-Path $candidate) {
        throw "Installer source violates the P1D.5 bridge-only contract. Removed desktop fallback is present: $candidate"
    }
}

# Run the managed -> C ABI -> ardirec_core smoke on the exact native bridge that is about to be
# packaged. This fixture belongs to ARSAS, so release validation does not depend on an external
# COMTRADE test-data path after the bridge has been staged.
$testProject = Join-Path $root "tests\ARSAS.Tests\ARSAS.Tests.csproj"
$testAssembly = Join-Path $root "tests\ARSAS.Tests\bin\Release\net8.0-windows\ARSAS.Tests.dll"
$fixtureCfg = Join-Path $root "tests\fixtures\comtrade\p1-release-smoke.cfg"
$bridgePath = Join-Path $PublishedDirectory "Tools\ArdIrec\ardirec_bridge.dll"
if (-not (Test-Path $testProject -PathType Leaf) -or
    -not (Test-Path $testAssembly -PathType Leaf) -or
    -not (Test-Path $fixtureCfg -PathType Leaf)) {
    throw "P1 managed bridge smoke prerequisites are missing. Build the Release test project before packaging the installer."
}

$previousBridgePath = $env:ARSAS_ARDIREC_BRIDGE_PATH
$previousFixtureCfg = $env:ARSAS_NATIVE_COMTRADE_TEST_CFG
try {
    $env:ARSAS_ARDIREC_BRIDGE_PATH = $bridgePath
    $env:ARSAS_NATIVE_COMTRADE_TEST_CFG = $fixtureCfg
    Write-Host "==> Validating staged P1D.5 COMTRADE bridge through ARSAS managed interop"
    & dotnet test $testProject `
        -c Release `
        --no-build `
        --no-restore `
        --filter "FullyQualifiedName~ArdIrecNativeBridgeIntegrationTests"
    if ($LASTEXITCODE -ne 0) {
        throw "Staged P1D.5 COMTRADE bridge failed the managed integration smoke test."
    }
}
finally {
    $env:ARSAS_ARDIREC_BRIDGE_PATH = $previousBridgePath
    $env:ARSAS_NATIVE_COMTRADE_TEST_CFG = $previousFixtureCfg
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        $InnoCompiler = $command.Source
    }
    else {
        $candidates = [System.Collections.Generic.List[string]]::new()
        if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
            $candidates.Add((Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"))
        }
        if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
            $candidates.Add((Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"))
        }
        if (-not [string]::IsNullOrWhiteSpace($env:ChocolateyInstall)) {
            $candidates.Add((Join-Path $env:ChocolateyInstall "bin\ISCC.exe"))
        }
        $InnoCompiler = $candidates | Where-Object { Test-Path $_ -PathType Leaf } | Select-Object -First 1
    }
}
if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or -not (Test-Path $InnoCompiler -PathType Leaf)) {
    throw "Inno Setup Compiler (ISCC.exe) was not found. Install Inno Setup 6 or pass -InnoCompiler with its full path."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputBaseName = "ARSAS-$normalizedVersion-$Runtime-setup"
$expectedInstaller = Join-Path $OutputDirectory "$outputBaseName.exe"
if (Test-Path $expectedInstaller) {
    Remove-Item $expectedInstaller -Force
}

Write-Host "==> Building Windows installer $normalizedVersion"
Write-Host "    Source: $PublishedDirectory"
Write-Host "    Output: $expectedInstaller"

$arguments = @(
    "/DAppVersion=$normalizedVersion",
    "/DAppVersionNumeric=$numericVersion",
    "/DSourceDir=$PublishedDirectory",
    "/DOutputDir=$OutputDirectory",
    "/DOutputBaseFilename=$outputBaseName",
    $installerDefinition
)

& $InnoCompiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path $expectedInstaller -PathType Leaf)) {
    throw "Inno Setup completed without producing the expected installer: $expectedInstaller"
}

$hash = Get-FileHash -Path $expectedInstaller -Algorithm SHA256
Write-Host "==> Installer: $expectedInstaller"
Write-Host "==> SHA256: $($hash.Hash)"
Write-Output $expectedInstaller
