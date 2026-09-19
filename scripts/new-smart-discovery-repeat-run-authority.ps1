param(
    [Parameter(Mandatory=$true)][string]$GoldenLockPath,
    [Parameter(Mandatory=$true)][string]$RepeatTargetPath,
    [Parameter(Mandatory=$true)][string]$FinalizationJson,
    [Parameter(Mandatory=$true)][string[]]$RunBundlePaths,
    [Parameter(Mandatory=$true)][string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-File([string]$Path, [string]$Label) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) { throw "$Label is not a file: $Path" }
    return $resolved.Path
}

$goldenFile = Resolve-File $GoldenLockPath 'P0-5e golden lock'
$targetFile = Resolve-File $RepeatTargetPath 'P0-5f repeat target'
$finalFile = Resolve-File $FinalizationJson 'P0-5f finalization JSON'
$golden = Get-Content -LiteralPath $goldenFile -Raw | ConvertFrom-Json
$target = Get-Content -LiteralPath $targetFile -Raw | ConvertFrom-Json
$final = Get-Content -LiteralPath $finalFile -Raw | ConvertFrom-Json

if ($golden.Phase -ne 'P0-5e' -or $golden.Status -ne 'locked') { throw 'Physical authority requires an active P0-5e golden lock.' }
if (-not [bool]$golden.GoldenSource.RawCaptureReverified) { throw 'Physical authority rejects a fixture/non-reverified P0-5e golden lock.' }
if ($target.Phase -ne 'P0-5f') { throw 'Repeat target is not P0-5f authority.' }
if ($final.Phase -ne 'P0-5f' -or $final.Verdict -ne 'PASS') { throw 'Physical authority requires a P0-5f PASS finalization.' }
if ([int]$final.SchemaVersion -lt 2) { throw 'Physical authority requires P0-5f finalization schema v2 or newer.' }

$goldenHash = (Get-FileHash -LiteralPath $goldenFile -Algorithm SHA256).Hash.ToLowerInvariant()
$targetHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash.ToLowerInvariant()
$finalHash = (Get-FileHash -LiteralPath $finalFile -Algorithm SHA256).Hash.ToLowerInvariant()
if ([string]$final.GoldenLockSha256 -ne $goldenHash) { throw 'Finalization is bound to a different P0-5e golden lock.' }
if ([string]$final.RepeatTargetSha256 -ne $targetHash) { throw 'Finalization is bound to a different P0-5f repeat target.' }
if ([string]$final.DeviceIdentity -ne [string]$target.DeviceIdentity -or [string]$final.DeviceIdentity -ne [string]$golden.DeviceIdentity) {
    throw 'Device identity differs across golden lock, repeat target, and finalization.'
}
if ([string]$final.ArsasCommit -ne [string]$golden.GoldenSource.ArsasCommit) { throw 'Finalization ARSAS commit differs from golden authority.' }
if ([string]$final.EngineCommit -ne [string]$golden.GoldenSource.EngineCommit) { throw 'Finalization engine commit differs from golden authority.' }

$minimumRuns = [int]$target.MinimumIndependentAssociations
if ([int]$final.ObservedRunBundles -lt $minimumRuns -or $RunBundlePaths.Count -lt $minimumRuns) {
    throw "Physical authority requires at least $minimumRuns independent run bundles."
}
if ([int]$final.ObservedRunBundles -ne $RunBundlePaths.Count) {
    throw 'Supplied run-bundle count differs from the reviewed finalization.'
}

$expectedBundleHashes = @($final.Runs | ForEach-Object { [string]$_.BundleSha256 } | Sort-Object)
$observedBundleHashes = @()
$associationGenerations = @()
$captureHashes = @()
$runtimeHashes = @()
foreach ($path in $RunBundlePaths) {
    $bundleFile = Resolve-File $path 'P0-5f run bundle'
    $bundle = Get-Content -LiteralPath $bundleFile -Raw | ConvertFrom-Json
    if ($bundle.Phase -ne 'P0-5f-run-bundle' -or $bundle.Verdict -ne 'PASS') { throw "Run bundle '$bundleFile' is not PASS evidence." }
    if ([bool]$bundle.FixtureEvidence) { throw "Physical authority rejects fixture run bundle '$bundleFile'." }
    if ([string]$bundle.GoldenLockSha256 -ne $goldenHash) { throw "Run bundle '$bundleFile' is bound to a different golden lock." }
    if ([string]$bundle.DeviceIdentity -ne [string]$golden.DeviceIdentity) { throw "Run bundle '$bundleFile' device identity mismatch." }
    $observedBundleHashes += (Get-FileHash -LiteralPath $bundleFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $associationGenerations += [long]$bundle.Runtime.AssociationGeneration
    $captureHashes += [string]$bundle.Provenance.CaptureSha256
    $runtimeHashes += [string]$bundle.Provenance.RuntimeEvidenceSha256
}
$observedBundleHashes = @($observedBundleHashes | Sort-Object)
if (($expectedBundleHashes -join '|') -ne ($observedBundleHashes -join '|')) { throw 'Supplied run bundles do not exactly match the reviewed finalization bundle hashes.' }
if (@($associationGenerations | Sort-Object -Unique).Count -ne $RunBundlePaths.Count) { throw 'Physical authority rejects reused association generations.' }
if (@($captureHashes | Sort-Object -Unique).Count -ne $RunBundlePaths.Count) { throw 'Physical authority rejects reused raw capture evidence.' }
if (@($runtimeHashes | Sort-Object -Unique).Count -ne $RunBundlePaths.Count) { throw 'Physical authority rejects reused runtime evidence.' }

$authority = [ordered]@{
    SchemaVersion = 1
    Phase = 'P0-5f-authority'
    Status = 'physical-finalized'
    DeviceIdentity = [string]$golden.DeviceIdentity
    ArsasCommit = [string]$golden.GoldenSource.ArsasCommit
    EngineCommit = [string]$golden.GoldenSource.EngineCommit
    GoldenLockSha256 = $goldenHash
    RepeatTargetSha256 = $targetHash
    FinalizationFileName = [IO.Path]::GetFileName($finalFile)
    FinalizationSha256 = $finalHash
    IndependentAssociations = $RunBundlePaths.Count
    AssociationGenerations = @($associationGenerations)
    Consensus = $final.Consensus
    RunBundleSha256 = @($observedBundleHashes)
    CaptureSha256 = @($captureHashes)
    RuntimeEvidenceSha256 = @($runtimeHashes)
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) { New-Item -ItemType Directory -Force $outputDirectory | Out-Null }
$authority | ConvertTo-Json -Depth 18 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host 'P0-5f physical repeat-run authority: FINALIZED'
Write-Host "  independent associations: $($RunBundlePaths.Count)"
Write-Host "  finalization SHA256: $finalHash"
Write-Host "  authority file: $OutputPath"
