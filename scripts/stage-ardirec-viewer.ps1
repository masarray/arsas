param(
    [Parameter(Mandatory = $true)]
    [string]$ArdIrecSource,

    [Parameter(Mandatory = $true)]
    [string]$PublishedDirectory,

    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$source = (Resolve-Path $ArdIrecSource).Path
$publish = (Resolve-Path $PublishedDirectory).Path
$cmakeLists = Join-Path $source "CMakeLists.txt"
$qmlDirectory = Join-Path $source "apps\desktop\qml"
$licensePath = Join-Path $source "LICENSE"

if (-not (Test-Path $cmakeLists -PathType Leaf)) {
    throw "ArdIrec source is invalid; CMakeLists.txt was not found at: $cmakeLists"
}
if (-not (Test-Path $qmlDirectory -PathType Container)) {
    throw "ArdIrec QML directory was not found: $qmlDirectory"
}

$cmake = Get-Command cmake -ErrorAction SilentlyContinue
if ($null -eq $cmake) {
    throw "CMake was not found in PATH."
}

$windeployqt = Get-Command windeployqt -ErrorAction SilentlyContinue
if ($null -eq $windeployqt) {
    throw "windeployqt was not found in PATH. Install the same Qt 6.8.3 MSVC 2022 toolchain used by ArdIrec Windows Build."
}

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "ardirec-arsas-integration"
}
$build = [System.IO.Path]::GetFullPath($BuildDirectory)

if (Test-Path $build) {
    Remove-Item $build -Recurse -Force
}
New-Item -ItemType Directory -Path $build -Force | Out-Null

Write-Host "==> Configuring pinned ArdIrec source: $source"
& $cmake.Source `
    -S $source `
    -B $build `
    -A x64 `
    -DARDIREC_BUILD_DESKTOP=ON `
    -DARDIREC_BUILD_BRIDGE=ON `
    -DARDIREC_BUILD_TESTS=ON
if ($LASTEXITCODE -ne 0) {
    throw "ArdIrec CMake configure failed with exit code $LASTEXITCODE."
}

Write-Host "==> Building ArdIrec Release"
& $cmake.Source --build $build --config Release --parallel 2
if ($LASTEXITCODE -ne 0) {
    throw "ArdIrec build failed with exit code $LASTEXITCODE."
}

Write-Host "==> Running ArdIrec regression and native bridge tests"
& ctest --test-dir $build -C Release --output-on-failure
if ($LASTEXITCODE -ne 0) {
    throw "ArdIrec regression tests failed with exit code $LASTEXITCODE."
}

$builtExe = Join-Path $build "apps\desktop\Release\ardirec.exe"
$builtBridge = Join-Path $build "bridge\Release\ardirec_bridge.dll"
if (-not (Test-Path $builtExe -PathType Leaf)) {
    throw "Built ArdIrec executable was not found: $builtExe"
}
if (-not (Test-Path $builtBridge -PathType Leaf)) {
    throw "Built ArdIrec native bridge was not found: $builtBridge"
}

$destination = Join-Path $publish "Tools\ArdIrec"
if (Test-Path $destination) {
    Remove-Item $destination -Recurse -Force
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null

$destinationExe = Join-Path $destination "ardirec.exe"
$destinationBridge = Join-Path $destination "ardirec_bridge.dll"
Copy-Item $builtExe $destinationExe -Force
Copy-Item $builtBridge $destinationBridge -Force

Write-Host "==> Deploying ArdIrec Qt fallback runtime and MSVC runtime into ARSAS publish tree"
& $windeployqt.Source `
    --release `
    --compiler-runtime `
    --no-translations `
    --qmldir $qmlDirectory `
    $destinationExe
if ($LASTEXITCODE -ne 0) {
    throw "windeployqt failed with exit code $LASTEXITCODE."
}

if (Test-Path $licensePath -PathType Leaf) {
    Copy-Item $licensePath (Join-Path $destination "LICENSE.txt") -Force
}

$requiredRuntimeFiles = @(
    "ardirec_bridge.dll",
    "ardirec.exe",
    "Qt6Core.dll",
    "Qt6Gui.dll",
    "Qt6Qml.dll",
    "Qt6Quick.dll",
    "platforms\qwindows.dll"
)
foreach ($relativePath in $requiredRuntimeFiles) {
    $runtimePath = Join-Path $destination $relativePath
    if (-not (Test-Path $runtimePath -PathType Leaf)) {
        throw "ArdIrec deployment is incomplete; required runtime file was not found: $runtimePath"
    }
}

Write-Host "==> ArdIrec P1 native bridge + Qt fallback staged for ARSAS: $destination"
Write-Output $destination
