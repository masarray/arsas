param(
    [Parameter(Mandatory=$true)][string]$ReadinessJson,
    [Parameter(Mandatory=$true)][string]$PromotionAuthorityPath,
    [Parameter(Mandatory=$true)][string]$PhysicalAuthorityPath,
    [Parameter(Mandatory=$true)][string]$PromotionPropsPath,
    [Parameter(Mandatory=$true)][string]$TargetPath,
    [Parameter(Mandatory=$true)][string]$ArsasHeadCommit,
    [Parameter(Mandatory=$true)][string]$ArsasBaseCommit,
    [Parameter(Mandatory=$true)][string]$EngineHeadCommit,
    [Parameter(Mandatory=$true)][string]$EngineBaseCommit,
    [Parameter(Mandatory=$true)][string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-File([string]$Path, [string]$Label) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) { throw "$Label is not a file: $Path" }
    return $resolved.Path
}

function Assert-Commit([string]$Value, [string]$Label) {
    if ($Value -notmatch '^[0-9a-fA-F]{40}$') { throw "$Label must be a full 40-character Git commit SHA." }
}

function Assert-Sha256([string]$Value, [string]$Label) {
    if ($Value -notmatch '^[0-9a-fA-F]{64}$') { throw "$Label must be a 64-character SHA-256 value." }
}

function Get-XmlChildText($Parent, [string]$Name) {
    $node = @($Parent.ChildNodes | Where-Object { $_.Name -eq $Name } | Select-Object -First 1)
    if ($node.Count -eq 0) { return '' }
    return ([string]$node[0].InnerText).Trim()
}

$readinessFile = Resolve-File $ReadinessJson 'P0-5g readiness JSON'
$promotionFile = Resolve-File $PromotionAuthorityPath 'P0-5g promotion authority'
$physicalFile = Resolve-File $PhysicalAuthorityPath 'P0-5f physical authority'
$propsFile = Resolve-File $PromotionPropsPath 'promotion props'
$targetFile = Resolve-File $TargetPath 'P0-5h merge target'

foreach ($entry in @(
    @{ Value = $ArsasHeadCommit; Label = 'ARSAS head commit' },
    @{ Value = $ArsasBaseCommit; Label = 'ARSAS base commit' },
    @{ Value = $EngineHeadCommit; Label = 'engine head commit' },
    @{ Value = $EngineBaseCommit; Label = 'engine base commit' })) {
    Assert-Commit $entry.Value $entry.Label
}

$arsasHead = $ArsasHeadCommit.ToLowerInvariant()
$arsasBase = $ArsasBaseCommit.ToLowerInvariant()
$engineHead = $EngineHeadCommit.ToLowerInvariant()
$engineBase = $EngineBaseCommit.ToLowerInvariant()
$readiness = Get-Content -LiteralPath $readinessFile -Raw | ConvertFrom-Json
$promotion = Get-Content -LiteralPath $promotionFile -Raw | ConvertFrom-Json
$physical = Get-Content -LiteralPath $physicalFile -Raw | ConvertFrom-Json
$target = Get-Content -LiteralPath $targetFile -Raw | ConvertFrom-Json

if ($readiness.Phase -ne 'P0-5g' -or $readiness.Verdict -ne 'READY_FOR_REVIEW') {
    throw 'P0-5h merge execution requires P0-5g READY_FOR_REVIEW.'
}
if (@($readiness.Blockers).Count -ne 0 -or -not [bool]$readiness.ProductionSwitchEnabled) {
    throw 'P0-5h refuses readiness evidence with blockers or a disabled production switch.'
}
if ($promotion.Phase -ne 'P0-5g-authority' -or $promotion.Status -ne 'production-promoted') {
    throw 'P0-5h requires a production-promoted P0-5g authority.'
}
if ($physical.Phase -ne 'P0-5f-authority' -or $physical.Status -ne 'physical-finalized') {
    throw 'P0-5h requires physical-finalized P0-5f authority.'
}
if ($target.Phase -ne 'P0-5h' -or [int]$target.ArsasPullRequest -ne 324 -or [int]$target.EnginePullRequest -ne 134) {
    throw 'P0-5h target repository/PR authority is invalid.'
}
if ([string]$target.MergeMethod -ne 'merge' -or @($target.MergeOrder) -join ',' -ne 'engine,arsas') {
    throw 'P0-5h target must require merge-commit method and engine-first order.'
}
if (([string]$readiness.ArsasHeadCommit).ToLowerInvariant() -ne $arsasHead -or
    ([string]$promotion.ArsasValidatedHeadCommit).ToLowerInvariant() -ne $arsasHead) {
    throw 'P0-5h ARSAS head differs from the P0-5g validated head.'
}
if (([string]$readiness.EngineHeadCommit).ToLowerInvariant() -ne $engineHead -or
    ([string]$promotion.EngineHeadCommit).ToLowerInvariant() -ne $engineHead) {
    throw 'P0-5h engine head differs from the P0-5g validated engine head.'
}
if ([string]$readiness.EngineHeadCiConclusion -ne 'success') {
    throw 'P0-5h requires green engine-head CI evidence.'
}

$physicalHash = (Get-FileHash -LiteralPath $physicalFile -Algorithm SHA256).Hash.ToLowerInvariant()
$promotionHash = (Get-FileHash -LiteralPath $promotionFile -Algorithm SHA256).Hash.ToLowerInvariant()
$readinessHash = (Get-FileHash -LiteralPath $readinessFile -Algorithm SHA256).Hash.ToLowerInvariant()
$targetHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash.ToLowerInvariant()
$propsHash = (Get-FileHash -LiteralPath $propsFile -Algorithm SHA256).Hash.ToLowerInvariant()
if (([string]$promotion.PhysicalAuthoritySha256).ToLowerInvariant() -ne $physicalHash) {
    throw 'P0-5g promotion authority is bound to a different physical authority.'
}

[xml]$props = Get-Content -LiteralPath $propsFile -Raw
$group = $props.Project.PropertyGroup
$promoted = (Get-XmlChildText $group 'SmartDiscoveryProductionPromoted').ToLowerInvariant()
$propsAuthority = (Get-XmlChildText $group 'SmartDiscoveryPromotionAuthoritySha256').ToLowerInvariant()
$propsEngineHead = (Get-XmlChildText $group 'SmartDiscoveryValidatedEngineHead').ToLowerInvariant()
if ($promoted -ne 'true') { throw 'P0-5h production switch is not enabled.' }
Assert-Sha256 $propsAuthority 'promotion props authority SHA-256'
if ($propsAuthority -ne $promotionHash) { throw 'Promotion props are bound to a different P0-5g authority.' }
if ($propsEngineHead -ne $engineHead) { throw 'Promotion props are bound to a different validated engine head.' }

$manifest = [ordered]@{
    SchemaVersion = 1
    Phase = 'P0-5h-merge-manifest'
    Status = 'authorized-for-ordered-merge'
    MergeMethod = 'merge'
    MergeOrder = @('engine','arsas')
    Arsas = [ordered]@{
        Repository = 'masarray/arsas'
        PullRequest = 324
        ExpectedHeadSha = $arsasHead
        ExpectedBaseSha = $arsasBase
    }
    Engine = [ordered]@{
        Repository = 'masarray/ARIEC61850'
        PullRequest = 134
        ExpectedHeadSha = $engineHead
        ExpectedBaseSha = $engineBase
    }
    Provenance = [ordered]@{
        PhysicalAuthoritySha256 = $physicalHash
        PromotionAuthoritySha256 = $promotionHash
        P05gReadinessSha256 = $readinessHash
        P05hTargetSha256 = $targetHash
        PromotionPropsSha256 = $propsHash
    }
    RequiredExecutionChecks = @(
        'both-prs-open-and-mergeable',
        'no-unresolved-review-threads',
        'base-sha-unchanged',
        'expected-head-sha-match',
        'merge-method=merge',
        'engine-merge-first',
        'engine-merge-success-before-arsas-merge',
        'post-merge-production-verification'
    )
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($outputDirectory) { New-Item -ItemType Directory -Force $outputDirectory | Out-Null }
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$manifestHash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host 'P0-5h merge execution manifest: AUTHORIZED'
Write-Host "  merge method: merge"
Write-Host "  engine expected head: $engineHead"
Write-Host "  ARSAS expected head: $arsasHead"
Write-Host "  manifest SHA256: $manifestHash"
Write-Host "  output: $OutputPath"
