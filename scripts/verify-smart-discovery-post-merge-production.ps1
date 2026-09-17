param(
    [Parameter(Mandatory=$true)][string]$MergeManifestPath,
    [Parameter(Mandatory=$true)][string]$PromotionAuthorityPath,
    [Parameter(Mandatory=$true)][string]$PromotionPropsPath,
    [Parameter(Mandatory=$true)][string]$ArsasRepositoryPath,
    [Parameter(Mandatory=$true)][string]$EngineRepositoryPath,
    [Parameter(Mandatory=$true)][string]$OutputJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-File([string]$Path, [string]$Label) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Leaf)) { throw "$Label is not a file: $Path" }
    return $resolved.Path
}

function Resolve-Directory([string]$Path, [string]$Label) {
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $resolved.Path -PathType Container)) { throw "$Label is not a directory: $Path" }
    return $resolved.Path
}

function Assert-Commit([string]$Value, [string]$Label) {
    if ($Value -notmatch '^[0-9a-fA-F]{40}$') { throw "$Label must be a full 40-character Git commit SHA." }
}

function Test-GitAncestor([string]$RepositoryPath, [string]$Ancestor, [string]$Descendant) {
    & git -C $RepositoryPath merge-base --is-ancestor $Ancestor $Descendant 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Get-GitHead([string]$RepositoryPath) {
    $value = (& git -C $RepositoryPath rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $value) { throw "Could not resolve Git HEAD for '$RepositoryPath'." }
    return ([string]$value).Trim().ToLowerInvariant()
}

function Get-XmlChildText($Parent, [string]$Name) {
    $node = @($Parent.ChildNodes | Where-Object { $_.Name -eq $Name } | Select-Object -First 1)
    if ($node.Count -eq 0) { return '' }
    return ([string]$node[0].InnerText).Trim()
}

$manifestFile = Resolve-File $MergeManifestPath 'P0-5h merge manifest'
$promotionFile = Resolve-File $PromotionAuthorityPath 'P0-5g promotion authority'
$propsFile = Resolve-File $PromotionPropsPath 'production promotion props'
$arsasRepo = Resolve-Directory $ArsasRepositoryPath 'ARSAS main checkout'
$engineRepo = Resolve-Directory $EngineRepositoryPath 'ARIEC61850 main checkout'

$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
$promotion = Get-Content -LiteralPath $promotionFile -Raw | ConvertFrom-Json
if ($manifest.Phase -ne 'P0-5h-merge-manifest' -or $manifest.Status -ne 'authorized-for-ordered-merge') {
    throw 'Post-merge verification requires an authorized P0-5h merge manifest.'
}
if ($promotion.Phase -ne 'P0-5g-authority' -or $promotion.Status -ne 'production-promoted') {
    throw 'Post-merge verification requires production-promoted P0-5g authority.'
}
if ([string]$manifest.MergeMethod -and [string]$manifest.MergeMethod -ne 'merge') {
    throw 'P0-5h requires merge-commit semantics.'
}

$expectedArsasHead = ([string]$manifest.Arsas.ExpectedHeadSha).ToLowerInvariant()
$expectedEngineHead = ([string]$manifest.Engine.ExpectedHeadSha).ToLowerInvariant()
Assert-Commit $expectedArsasHead 'manifest ARSAS head'
Assert-Commit $expectedEngineHead 'manifest engine head'
$currentArsasMain = Get-GitHead $arsasRepo
$currentEngineMain = Get-GitHead $engineRepo

$failures = [System.Collections.Generic.List[string]]::new()
if (-not (Test-GitAncestor $engineRepo $expectedEngineHead $currentEngineMain)) {
    $failures.Add('Validated engine PR head is not an ancestor of engine main.')
}
if (-not (Test-GitAncestor $arsasRepo $expectedArsasHead $currentArsasMain)) {
    $failures.Add('Validated ARSAS PR head is not an ancestor of ARSAS main.')
}
if (([string]$promotion.EngineHeadCommit).ToLowerInvariant() -ne $expectedEngineHead) {
    $failures.Add('P0-5g promotion authority engine head differs from P0-5h merge manifest.')
}
if (([string]$promotion.ArsasValidatedHeadCommit).ToLowerInvariant() -ne $expectedArsasHead) {
    $failures.Add('P0-5g promotion authority ARSAS head differs from P0-5h merge manifest.')
}

$promotionHash = (Get-FileHash -LiteralPath $promotionFile -Algorithm SHA256).Hash.ToLowerInvariant()
$manifestHash = (Get-FileHash -LiteralPath $manifestFile -Algorithm SHA256).Hash.ToLowerInvariant()
[xml]$props = Get-Content -LiteralPath $propsFile -Raw
$group = $props.Project.PropertyGroup
$promoted = (Get-XmlChildText $group 'SmartDiscoveryProductionPromoted').ToLowerInvariant()
$propsAuthority = (Get-XmlChildText $group 'SmartDiscoveryPromotionAuthoritySha256').ToLowerInvariant()
$propsEngine = (Get-XmlChildText $group 'SmartDiscoveryValidatedEngineHead').ToLowerInvariant()
if ($promoted -ne 'true') { $failures.Add('Production smart-discovery switch is not enabled on main.') }
if ($propsAuthority -ne $promotionHash) { $failures.Add('Production props are not bound to the merged P0-5g promotion authority.') }
if ($propsEngine -ne $expectedEngineHead) { $failures.Add('Production props validated engine head differs from merge manifest.') }

$result = [ordered]@{
    SchemaVersion = 1
    Phase = 'P0-5h-post-merge'
    Verdict = if ($failures.Count -eq 0) { 'PASS' } else { 'FAIL' }
    MergeManifestSha256 = $manifestHash
    PromotionAuthoritySha256 = $promotionHash
    ExpectedArsasHead = $expectedArsasHead
    CurrentArsasMainHead = $currentArsasMain
    ExpectedEngineHead = $expectedEngineHead
    CurrentEngineMainHead = $currentEngineMain
    EngineHeadIsAncestorOfMain = Test-GitAncestor $engineRepo $expectedEngineHead $currentEngineMain
    ArsasHeadIsAncestorOfMain = Test-GitAncestor $arsasRepo $expectedArsasHead $currentArsasMain
    ProductionSwitchEnabled = $promoted -eq 'true'
    AcceptanceFailures = @($failures)
}

$outputDirectory = Split-Path -Parent $OutputJson
if ($outputDirectory) { New-Item -ItemType Directory -Force $outputDirectory | Out-Null }
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputJson -Encoding utf8
Write-Host "P0-5h post-merge production verification: $($result.Verdict)"
Write-Host "  engine main: $currentEngineMain"
Write-Host "  ARSAS main: $currentArsasMain"
Write-Host "  attestation: $OutputJson"
if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Error $failure -ErrorAction Continue }
    exit 1
}
