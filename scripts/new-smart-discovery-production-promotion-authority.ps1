param(
    [Parameter(Mandatory=$true)][string]$ReadinessJson,
    [Parameter(Mandatory=$true)][string]$TargetPath,
    [Parameter(Mandatory=$true)][string]$PhysicalAuthorityPath,
    [Parameter(Mandatory=$true)][string]$EngineLockPath,
    [Parameter(Mandatory=$true)][string]$OutputAuthorityPath,
    [Parameter(Mandatory=$true)][string]$OutputPropsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-File([string]$Path, [string]$Label) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) { throw "$Label is not a file: $Path" }
    return $resolved.Path
}

function Assert-Sha256([string]$Value, [string]$Label) {
    if ($Value -notmatch '^[0-9a-fA-F]{64}$') { throw "$Label must be a 64-character SHA-256 value." }
}

function Assert-Commit([string]$Value, [string]$Label) {
    if ($Value -notmatch '^[0-9a-fA-F]{40}$') { throw "$Label must be a full 40-character Git commit SHA." }
}

function Assert-PhysicalAuthorityProvenance($Physical) {
    if ([int]$Physical.SchemaVersion -lt 1 -or $Physical.Phase -ne 'P0-5f-authority' -or $Physical.Status -ne 'physical-finalized') {
        throw 'Production promotion requires physical-finalized P0-5f authority.'
    }
    Assert-Commit ([string]$Physical.ArsasCommit) 'P0-5f ARSAS commit'
    Assert-Commit ([string]$Physical.EngineCommit) 'P0-5f engine commit'
    Assert-Sha256 ([string]$Physical.GoldenLockSha256) 'P0-5f golden lock SHA-256'
    Assert-Sha256 ([string]$Physical.RepeatTargetSha256) 'P0-5f repeat target SHA-256'
    Assert-Sha256 ([string]$Physical.FinalizationSha256) 'P0-5f finalization SHA-256'

    $count = [int]$Physical.IndependentAssociations
    if ($count -lt 3) { throw 'P0-5f physical authority must contain at least three independent associations.' }

    $generations = @($Physical.AssociationGenerations)
    $bundleHashes = @($Physical.RunBundleSha256)
    $captureHashes = @($Physical.CaptureSha256)
    $runtimeHashes = @($Physical.RuntimeEvidenceSha256)
    foreach ($entry in @(
        @{ Label = 'association generations'; Values = $generations },
        @{ Label = 'run bundle hashes'; Values = $bundleHashes },
        @{ Label = 'capture hashes'; Values = $captureHashes },
        @{ Label = 'runtime evidence hashes'; Values = $runtimeHashes })) {
        if ($entry.Values.Count -ne $count) { throw "P0-5f physical authority $($entry.Label) count does not match IndependentAssociations." }
        if (@($entry.Values | Sort-Object -Unique).Count -ne $count) { throw "P0-5f physical authority contains reused $($entry.Label)." }
    }
    foreach ($generation in $generations) {
        if ([long]$generation -le 0) { throw 'P0-5f physical authority contains an invalid association generation.' }
    }
    foreach ($hash in @($bundleHashes + $captureHashes + $runtimeHashes)) {
        Assert-Sha256 ([string]$hash) 'P0-5f evidence SHA-256'
    }
    if ($null -eq $Physical.Consensus) { throw 'P0-5f physical authority is missing consensus evidence.' }
}

$readinessFile = Resolve-File $ReadinessJson 'P0-5g readiness JSON'
$targetFile = Resolve-File $TargetPath 'P0-5g promotion target'
$physicalFile = Resolve-File $PhysicalAuthorityPath 'P0-5f physical authority'
$engineLockFile = Resolve-File $EngineLockPath 'ARIEC61850 engine lock'

$readiness = Get-Content -LiteralPath $readinessFile -Raw | ConvertFrom-Json
$target = Get-Content -LiteralPath $targetFile -Raw | ConvertFrom-Json
$physical = Get-Content -LiteralPath $physicalFile -Raw | ConvertFrom-Json
$engineLock = Get-Content -LiteralPath $engineLockFile -Raw | ConvertFrom-Json

if ($readiness.Phase -ne 'P0-5g' -or $readiness.Verdict -ne 'READY_TO_PROMOTE') {
    throw 'Production promotion requires a P0-5g READY_TO_PROMOTE readiness proof.'
}
if ([bool]$readiness.ProductionSwitchEnabled) {
    throw 'Production switch was already enabled before promotion authority creation.'
}
if (@($readiness.Blockers).Count -ne 0) {
    throw 'Production promotion refuses a readiness proof with blockers.'
}
if (-not [bool]$readiness.EngineHeadIsEvidenceCompatibleDescendant) {
    throw 'Engine PR head is not evidence-compatible with the physical discovery baseline.'
}
if ([string]$readiness.EngineHeadCiConclusion -ne 'success') {
    throw 'Engine PR head CI is not green.'
}
Assert-PhysicalAuthorityProvenance $physical
if ($target.Phase -ne 'P0-5g') { throw 'Promotion target is not P0-5g authority.' }

$baseline = ([string]$target.EvidenceEngineBaselineCommit).ToLowerInvariant()
if (([string]$physical.EngineCommit).ToLowerInvariant() -ne $baseline) {
    throw 'Physical P0-5f authority engine commit differs from the P0-5g evidence baseline.'
}
if (([string]$engineLock.commit).ToLowerInvariant() -ne $baseline) {
    throw 'ARSAS engine lock differs from the P0-5g evidence baseline.'
}
Assert-Commit ([string]$readiness.ArsasHeadCommit) 'Readiness ARSAS head commit'
Assert-Commit ([string]$readiness.EngineHeadCommit) 'Readiness engine head commit'

$readinessHash = (Get-FileHash -LiteralPath $readinessFile -Algorithm SHA256).Hash.ToLowerInvariant()
$targetHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash.ToLowerInvariant()
$physicalHash = (Get-FileHash -LiteralPath $physicalFile -Algorithm SHA256).Hash.ToLowerInvariant()
$engineLockHash = (Get-FileHash -LiteralPath $engineLockFile -Algorithm SHA256).Hash.ToLowerInvariant()

$authority = [ordered]@{
    SchemaVersion = 2
    Phase = 'P0-5g-authority'
    Status = 'production-promoted'
    DeviceIdentity = [string]$physical.DeviceIdentity
    PhysicalAuthorityFileName = [IO.Path]::GetFileName($physicalFile)
    PhysicalAuthoritySha256 = $physicalHash
    ReadinessFileName = [IO.Path]::GetFileName($readinessFile)
    ReadinessSha256 = $readinessHash
    PromotionTargetSha256 = $targetHash
    EngineLockSha256 = $engineLockHash
    EvidenceEngineBaselineCommit = $baseline
    EngineHeadCommit = ([string]$readiness.EngineHeadCommit).ToLowerInvariant()
    EngineHeadCiConclusion = 'success'
    ArsasValidatedHeadCommit = ([string]$readiness.ArsasHeadCommit).ToLowerInvariant()
    EngineHeadEvidenceCompatible = $true
    DiscoveryCriticalChanges = @($readiness.DiscoveryCriticalChanges)
    IndependentPhysicalAssociations = [int]$physical.IndependentAssociations
    GoldenConsensus = $physical.Consensus
    P05fGoldenLockSha256 = ([string]$physical.GoldenLockSha256).ToLowerInvariant()
    P05fRepeatTargetSha256 = ([string]$physical.RepeatTargetSha256).ToLowerInvariant()
    P05fFinalizationSha256 = ([string]$physical.FinalizationSha256).ToLowerInvariant()
}

$authorityDirectory = Split-Path -Parent $OutputAuthorityPath
if ($authorityDirectory) { New-Item -ItemType Directory -Force $authorityDirectory | Out-Null }
$authority | ConvertTo-Json -Depth 18 | Set-Content -LiteralPath $OutputAuthorityPath -Encoding utf8
$authorityHash = (Get-FileHash -LiteralPath $OutputAuthorityPath -Algorithm SHA256).Hash.ToLowerInvariant()

$propsDirectory = Split-Path -Parent $OutputPropsPath
if ($propsDirectory) { New-Item -ItemType Directory -Force $propsDirectory | Out-Null }
$props = @"
<Project>
  <PropertyGroup>
    <SmartDiscoveryProductionPromoted>true</SmartDiscoveryProductionPromoted>
    <SmartDiscoveryEvidenceEngineCommit>$baseline</SmartDiscoveryEvidenceEngineCommit>
    <SmartDiscoveryPromotionPhase>P0-5g</SmartDiscoveryPromotionPhase>
    <SmartDiscoveryPromotionAuthoritySha256>$authorityHash</SmartDiscoveryPromotionAuthoritySha256>
    <SmartDiscoveryValidatedEngineHead>$(([string]$readiness.EngineHeadCommit).ToLowerInvariant())</SmartDiscoveryValidatedEngineHead>
  </PropertyGroup>
</Project>
"@
[IO.File]::WriteAllText($OutputPropsPath, $props, (New-Object Text.UTF8Encoding($false)))

Write-Host 'P0-5g golden discovery production promotion: AUTHORIZED'
Write-Host "  physical authority SHA256: $physicalHash"
Write-Host "  evidence engine baseline: $baseline"
Write-Host "  validated engine PR head: $($readiness.EngineHeadCommit)"
Write-Host "  promotion authority SHA256: $authorityHash"
Write-Host "  authority file: $OutputAuthorityPath"
Write-Host "  production props: $OutputPropsPath"
