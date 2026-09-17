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
if ($physical.Phase -ne 'P0-5f-authority' -or $physical.Status -ne 'physical-finalized') {
    throw 'Production promotion requires physical-finalized P0-5f authority.'
}
if ($target.Phase -ne 'P0-5g') { throw 'Promotion target is not P0-5g authority.' }

$baseline = ([string]$target.EvidenceEngineBaselineCommit).ToLowerInvariant()
if (([string]$physical.EngineCommit).ToLowerInvariant() -ne $baseline) {
    throw 'Physical P0-5f authority engine commit differs from the P0-5g evidence baseline.'
}
if (([string]$engineLock.commit).ToLowerInvariant() -ne $baseline) {
    throw 'ARSAS engine lock differs from the P0-5g evidence baseline.'
}
if (([string]$readiness.ArsasHeadCommit).ToLowerInvariant() -notmatch '^[0-9a-f]{40}$' -or
    ([string]$readiness.EngineHeadCommit).ToLowerInvariant() -notmatch '^[0-9a-f]{40}$') {
    throw 'Readiness proof does not contain valid exact ARSAS/engine head commits.'
}

$readinessHash = (Get-FileHash -LiteralPath $readinessFile -Algorithm SHA256).Hash.ToLowerInvariant()
$targetHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash.ToLowerInvariant()
$physicalHash = (Get-FileHash -LiteralPath $physicalFile -Algorithm SHA256).Hash.ToLowerInvariant()
$engineLockHash = (Get-FileHash -LiteralPath $engineLockFile -Algorithm SHA256).Hash.ToLowerInvariant()

$authority = [ordered]@{
    SchemaVersion = 1
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
