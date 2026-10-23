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
$licensePath = Join-Path $source "LICENSE"

if (-not (Test-Path $cmakeLists -PathType Leaf)) {
    throw "ArdIrec source is invalid; CMakeLists.txt was not found at: $cmakeLists"
}

$cmake = Get-Command cmake -ErrorAction SilentlyContinue
if ($null -eq $cmake) {
    throw "CMake was not found in PATH."
}

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "ardirec-arsas-bridge"
}
$build = [System.IO.Path]::GetFullPath($BuildDirectory)

if (Test-Path $build) {
    Remove-Item $build -Recurse -Force
}
New-Item -ItemType Directory -Path $build -Force | Out-Null

Write-Host "==> Configuring pinned ArdIrec native bridge: $source"
& $cmake.Source `
    -S $source `
    -B $build `
    -A x64 `
    -DARDIREC_BUILD_DESKTOP=OFF `
    -DARDIREC_BUILD_BRIDGE=ON `
    -DARDIREC_BUILD_TESTS=ON `
    -DARDIREC_BUILD_BENCHMARKS=OFF
if ($LASTEXITCODE -ne 0) {
    throw "ArdIrec bridge CMake configure failed with exit code $LASTEXITCODE."
}

Write-Host "==> Building ArdIrec native bridge Release"
& $cmake.Source --build $build --config Release --parallel 2
if ($LASTEXITCODE -ne 0) {
    throw "ArdIrec bridge build failed with exit code $LASTEXITCODE."
}

Write-Host "==> Running ArdIrec core + bridge regression tests"
& ctest --test-dir $build -C Release --output-on-failure
if ($LASTEXITCODE -ne 0) {
    throw "ArdIrec bridge regression tests failed with exit code $LASTEXITCODE."
}

$builtBridge = Join-Path $build "bridge\Release\ardirec_bridge.dll"
if (-not (Test-Path $builtBridge -PathType Leaf)) {
    throw "Built ArdIrec native bridge was not found: $builtBridge"
}

$destination = Join-Path $publish "Tools\ArdIrec"
if (Test-Path $destination) {
    Remove-Item $destination -Recurse -Force
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null

$destinationBridge = Join-Path $destination "ardirec_bridge.dll"
Copy-Item $builtBridge $destinationBridge -Force

if (Test-Path $licensePath -PathType Leaf) {
    Copy-Item $licensePath (Join-Path $destination "LICENSE.txt") -Force
}

if (-not (Test-Path $destinationBridge -PathType Leaf)) {
    throw "ArdIrec bridge staging is incomplete: $destinationBridge"
}

# P1D.5 deliberately removes the Qt desktop fallback. These files must not silently return,
# otherwise installer size and runtime behavior again have two competing COMTRADE viewers.
$forbidden = @(
    "ardirec.exe",
    "Qt6Core.dll",
    "Qt6Gui.dll",
    "Qt6Qml.dll",
    "Qt6Quick.dll",
    "platforms\qwindows.dll"
)
foreach ($relativePath in $forbidden) {
    $runtimePath = Join-Path $destination $relativePath
    if (Test-Path $runtimePath) {
        throw "P1D.5 bridge-only staging unexpectedly contains removed desktop runtime: $runtimePath"
    }
}

Write-Host "==> ArdIrec native analysis bridge staged for ARSAS: $destination"
Write-Output $destination
