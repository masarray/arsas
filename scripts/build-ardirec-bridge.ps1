param(
    [string]$ArdIrecSource = "",

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$BuildDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $root "engines\ARDIREC.lock.json"
if (-not (Test-Path $lockPath -PathType Leaf)) {
    throw "ArdIrec integration lock was not found: $lockPath"
}

$lock = Get-Content $lockPath -Raw | ConvertFrom-Json
if ($lock.schema -ne 3 -or
    $lock.repository -notmatch '^[^/]+/[^/]+$' -or
    [string]::IsNullOrWhiteSpace([string]$lock.ref) -or
    $lock.ref -notmatch '^[A-Za-z0-9._/-]+$' -or
    $lock.commit -notmatch '^[0-9a-f]{40}$' -or
    $lock.bridge.abi -ne 1 -or
    $lock.bridge.mode -ne 'native-only' -or
    $lock.bridge.relativeLibrary -ne 'Tools/ArdIrec/ardirec_bridge.dll') {
    throw "ArdIrec native bridge lock metadata is invalid."
}

$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$destinationBridge = Join-Path $output "ardirec_bridge.dll"

if (Test-Path $destinationBridge -PathType Leaf) {
    Write-Host "==> Reusing pinned ArdIrec bridge: $destinationBridge"
    Write-Output $destinationBridge
    exit 0
}

if ([string]::IsNullOrWhiteSpace($ArdIrecSource)) {
    $sourceRoot = Join-Path ([System.IO.Path]::GetTempPath()) "arsas-ardirec-$($lock.commit)"
    if (-not (Test-Path (Join-Path $sourceRoot ".git") -PathType Container)) {
        if (Test-Path $sourceRoot) { Remove-Item $sourceRoot -Recurse -Force }
        Write-Host "==> Cloning immutable ArdIrec revision $($lock.commit)"
        git clone --quiet --filter=blob:none --no-checkout "https://github.com/$($lock.repository).git" $sourceRoot
        if ($LASTEXITCODE -ne 0) { throw "Could not clone pinned ArdIrec repository." }
        git -C $sourceRoot fetch --quiet --depth 1 origin $lock.commit
        if ($LASTEXITCODE -ne 0) { throw "Could not fetch pinned ArdIrec revision $($lock.commit)." }
        git -C $sourceRoot checkout --quiet --detach $lock.commit
        if ($LASTEXITCODE -ne 0) { throw "Could not checkout pinned ArdIrec revision $($lock.commit)." }
    }
    $ArdIrecSource = $sourceRoot
}

$source = (Resolve-Path $ArdIrecSource).Path
$actualCommit = (git -C $source rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $lock.commit) {
    throw "ArdIrec source revision mismatch. Expected $($lock.commit), got '$actualCommit'."
}

$cmakeLists = Join-Path $source "CMakeLists.txt"
if (-not (Test-Path $cmakeLists -PathType Leaf)) {
    throw "ArdIrec source is invalid; CMakeLists.txt was not found at: $cmakeLists"
}

$cmake = Get-Command cmake -ErrorAction SilentlyContinue
if ($null -eq $cmake) {
    throw "CMake was not found in PATH."
}

if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "arsas-ardirec-bridge-$($lock.commit)"
}
$build = [System.IO.Path]::GetFullPath($BuildDirectory)
if (Test-Path $build) { Remove-Item $build -Recurse -Force }
New-Item -ItemType Directory -Path $build -Force | Out-Null

Write-Host "==> Configuring pinned ArdIrec native bridge (desktop/Qt disabled)"
& $cmake.Source `
    -S $source `
    -B $build `
    -A x64 `
    -DARDIREC_BUILD_DESKTOP=OFF `
    -DARDIREC_BUILD_BRIDGE=ON `
    -DARDIREC_BUILD_TESTS=ON `
    -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded
if ($LASTEXITCODE -ne 0) {
    throw "ArdIrec bridge CMake configure failed with exit code $LASTEXITCODE."
}

Write-Host "==> Building ArdIrec native bridge Release"
& $cmake.Source --build $build --config Release --parallel 2
if ($LASTEXITCODE -ne 0) {
    throw "ArdIrec bridge build failed with exit code $LASTEXITCODE."
}

Write-Host "==> Running ArdIrec core/bridge regression tests"
& ctest --test-dir $build -C Release --output-on-failure
if ($LASTEXITCODE -ne 0) {
    throw "ArdIrec native regression tests failed with exit code $LASTEXITCODE."
}

$builtBridge = Join-Path $build "bridge\Release\ardirec_bridge.dll"
if (-not (Test-Path $builtBridge -PathType Leaf)) {
    throw "Built ArdIrec native bridge was not found: $builtBridge"
}

Copy-Item $builtBridge $destinationBridge -Force
if (-not (Test-Path $destinationBridge -PathType Leaf)) {
    throw "ArdIrec native bridge could not be staged: $destinationBridge"
}

Write-Host "==> Pinned native-only ArdIrec bridge staged: $destinationBridge"
Write-Output $destinationBridge
