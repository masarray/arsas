param(
    [Parameter(Mandatory=$true)][string]$TargetPath,
    [Parameter(Mandatory=$true)][string]$EngineLockPath,
    [Parameter(Mandatory=$true)][string]$PromotionPropsPath,
    [Parameter(Mandatory=$true)][string]$ArsasRepositoryPath,
    [Parameter(Mandatory=$true)][string]$EngineRepositoryPath,
    [Parameter(Mandatory=$true)][string]$ArsasHeadCommit,
    [Parameter(Mandatory=$true)][string]$EngineHeadCommit,
    [Parameter(Mandatory=$true)][string]$EngineHeadCiConclusion,
    [string]$PhysicalAuthorityPath,
    [string]$PromotionAuthorityPath,
    [string]$OutputJson,
    [switch]$NoFailExit
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

function Get-GitHead([string]$RepositoryPath) {
    $value = (& git -C $RepositoryPath rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $value) { throw "Could not resolve Git HEAD for '$RepositoryPath'." }
    return ([string]$value).Trim().ToLowerInvariant()
}

function Test-GitAncestor([string]$RepositoryPath, [string]$Ancestor, [string]$Descendant) {
    & git -C $RepositoryPath merge-base --is-ancestor $Ancestor $Descendant 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Get-GitChangedPaths([string]$RepositoryPath, [string]$BaseCommit, [string]$HeadCommit, [string[]]$PathFilters) {
    $args = @('-C', $RepositoryPath, 'diff', '--name-only', "$BaseCommit..$HeadCommit", '--') + @($PathFilters)
    $lines = @(& git @args 2>$null)
    if ($LASTEXITCODE -ne 0) { throw "git diff failed for $BaseCommit..$HeadCommit." }
    return @($lines | ForEach-Object { ([string]$_).Trim().Replace('\\','/') } | Where-Object { $_ } | Sort-Object -Unique)
}

function Get-PromotionProps([string]$PropsFile) {
    [xml]$xml = Get-Content -LiteralPath $PropsFile -Raw
    $group = $xml.Project.PropertyGroup
    $node = $group.SmartDiscoveryProductionPromoted
    if ($null -eq $node) { throw 'Promotion props does not define SmartDiscoveryProductionPromoted.' }
    $promotedText = ([string]$node).Trim().ToLowerInvariant()
    if ($promotedText -notin @('true','false')) { throw 'SmartDiscoveryProductionPromoted is not a boolean.' }
    return [pscustomobject]@{
        Promoted = $promotedText -eq 'true'
        EvidenceEngineCommit = ([string]$group.SmartDiscoveryEvidenceEngineCommit).Trim().ToLowerInvariant()
        Phase = ([string]$group.SmartDiscoveryPromotionPhase).Trim()
        AuthoritySha256 = ([string]$group.SmartDiscoveryPromotionAuthoritySha256).Trim().ToLowerInvariant()
        ValidatedEngineHead = ([string]$group.SmartDiscoveryValidatedEngineHead).Trim().ToLowerInvariant()
    }
}

$targetFile = Resolve-File $TargetPath 'P0-5g promotion target'
$engineLockFile = Resolve-File $EngineLockPath 'ARIEC61850 engine lock'
$propsFile = Resolve-File $PromotionPropsPath 'P0-5g promotion props'
$arsasRepo = Resolve-Directory $ArsasRepositoryPath 'ARSAS repository'
$engineRepo = Resolve-Directory $EngineRepositoryPath 'ARIEC61850 repository'
Assert-Commit $ArsasHeadCommit 'ARSAS head commit'
Assert-Commit $EngineHeadCommit 'Engine head commit'
$arsasHead = $ArsasHeadCommit.ToLowerInvariant()
$engineHead = $EngineHeadCommit.ToLowerInvariant()

$target = Get-Content -LiteralPath $targetFile -Raw | ConvertFrom-Json
$engineLock = Get-Content -LiteralPath $engineLockFile -Raw | ConvertFrom-Json
$promotionProps = Get-PromotionProps $propsFile
$targetHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash.ToLowerInvariant()
$engineLockHash = (Get-FileHash -LiteralPath $engineLockFile -Algorithm SHA256).Hash.ToLowerInvariant()
$blockers = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

if ($target.Phase -ne 'P0-5g') { $blockers.Add('Promotion target is not P0-5g authority.') }
if ([string]$target.EngineRepository -ne 'masarray/ARIEC61850' -or [int]$target.EnginePullRequest -ne 134) {
    $blockers.Add('Promotion target engine repository/PR authority changed unexpectedly.')
}
$baseline = ([string]$target.EvidenceEngineBaselineCommit).ToLowerInvariant()
Assert-Commit $baseline 'Evidence engine baseline commit'

if (Get-GitHead $arsasRepo -ne $arsasHead) { $blockers.Add('ARSAS repository HEAD differs from the supplied readiness head.') }
if (Get-GitHead $engineRepo -ne $engineHead) { $blockers.Add('Engine repository HEAD differs from the supplied PR head.') }
if ([string]$engineLock.repository -ne [string]$target.EngineRepository) { $blockers.Add('ARSAS engine lock repository differs from the promotion target.') }
if (([string]$engineLock.commit).ToLowerInvariant() -ne $baseline) {
    $blockers.Add('ARSAS engine lock no longer points at the physical-evidence engine baseline.')
}
if ($promotionProps.EvidenceEngineCommit -and $promotionProps.EvidenceEngineCommit -ne $baseline) {
    $blockers.Add('Promotion props evidence engine commit differs from the P0-5g baseline.')
}
if ($promotionProps.Phase -and $promotionProps.Phase -ne 'P0-5g') {
    $blockers.Add('Promotion props phase differs from P0-5g.')
}

$engineIsDescendant = Test-GitAncestor $engineRepo $baseline $engineHead
if (-not $engineIsDescendant) {
    $blockers.Add('Engine PR head is not a descendant of the physical-evidence engine baseline.')
}

$criticalPaths = @($target.DiscoveryCriticalEnginePaths | ForEach-Object { [string]$_ })
$criticalChanges = @()
if ($engineIsDescendant) {
    $criticalChanges = Get-GitChangedPaths $engineRepo $baseline $engineHead $criticalPaths
    if ($criticalChanges.Count -gt 0) {
        $blockers.Add("Engine head changed discovery-critical evidence paths after the physical baseline: $($criticalChanges -join ', ').")
    }
}

if ($EngineHeadCiConclusion.Trim().ToLowerInvariant() -ne 'success') {
    $blockers.Add("Engine PR head CI is not green: '$EngineHeadCiConclusion'.")
}

$productionSwitch = [bool]$promotionProps.Promoted
$physicalAuthority = $null
$physicalAuthorityFile = $null
if ([string]::IsNullOrWhiteSpace($PhysicalAuthorityPath) -or -not (Test-Path -LiteralPath $PhysicalAuthorityPath -PathType Leaf)) {
    $blockers.Add('P0-5f physical-finalized authority is missing.')
} else {
    $physicalAuthorityFile = Resolve-File $PhysicalAuthorityPath 'P0-5f physical authority'
    $physicalAuthority = Get-Content -LiteralPath $physicalAuthorityFile -Raw | ConvertFrom-Json
    if ($physicalAuthority.Phase -ne 'P0-5f-authority' -or $physicalAuthority.Status -ne 'physical-finalized') {
        $blockers.Add('P0-5f authority is not physical-finalized production evidence.')
    }
    if (([string]$physicalAuthority.EngineCommit).ToLowerInvariant() -ne $baseline) {
        $blockers.Add('P0-5f authority engine commit differs from the evidence baseline.')
    }

    $authorityArsas = ([string]$physicalAuthority.ArsasCommit).ToLowerInvariant()
    if ($authorityArsas -notmatch '^[0-9a-f]{40}$') {
        $blockers.Add('P0-5f authority does not contain a valid ARSAS commit.')
    } elseif (-not (Test-GitAncestor $arsasRepo $authorityArsas $arsasHead)) {
        $blockers.Add('Current ARSAS head is not a descendant of the physically validated ARSAS commit.')
    } else {
        $allChanged = Get-GitChangedPaths $arsasRepo $authorityArsas $arsasHead @('.')
        $allowed = @($target.AllowedPostPhysicalAuthorityPaths | ForEach-Object { ([string]$_).Replace('\\','/') })
        $notAllowed = @($allChanged | Where-Object { $allowed -notcontains $_ })
        if ($notAllowed.Count -gt 0) {
            $blockers.Add("Runtime/source changed after physical authority outside the promotion-only allowlist: $($notAllowed -join ', ').")
        }
    }
}

$promotionAuthority = $null
$promotionAuthorityFile = $null
if (-not [string]::IsNullOrWhiteSpace($PromotionAuthorityPath) -and (Test-Path -LiteralPath $PromotionAuthorityPath -PathType Leaf)) {
    $promotionAuthorityFile = Resolve-File $PromotionAuthorityPath 'P0-5g production authority'
    $promotionAuthority = Get-Content -LiteralPath $promotionAuthorityFile -Raw | ConvertFrom-Json
    if ($promotionAuthority.Phase -ne 'P0-5g-authority' -or $promotionAuthority.Status -ne 'production-promoted') {
        $blockers.Add('P0-5g promotion authority is not production-promoted.')
    }
    if (([string]$promotionAuthority.EvidenceEngineBaselineCommit).ToLowerInvariant() -ne $baseline) {
        $blockers.Add('P0-5g promotion authority evidence baseline differs from the current target.')
    }
    if (([string]$promotionAuthority.PromotionTargetSha256).ToLowerInvariant() -ne $targetHash) {
        $blockers.Add('P0-5g promotion authority is bound to a different promotion target.')
    }
    if (([string]$promotionAuthority.EngineLockSha256).ToLowerInvariant() -ne $engineLockHash) {
        $blockers.Add('P0-5g promotion authority is bound to a different engine lock.')
    }
    if ($null -eq $physicalAuthorityFile) {
        $blockers.Add('P0-5g promotion authority exists without P0-5f physical authority.')
    } else {
        $physicalHash = (Get-FileHash -LiteralPath $physicalAuthorityFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if (([string]$promotionAuthority.PhysicalAuthoritySha256).ToLowerInvariant() -ne $physicalHash) {
            $blockers.Add('P0-5g promotion authority is bound to a different P0-5f physical authority.')
        }
    }
    if (([string]$promotionAuthority.EngineHeadCommit).ToLowerInvariant() -ne $engineHead) {
        $blockers.Add('P0-5g promotion authority is bound to a different engine PR head.')
    }
    if (-not $productionSwitch) {
        $blockers.Add('P0-5g authority exists but the tracked production switch is still false.')
    } else {
        $authorityHash = (Get-FileHash -LiteralPath $promotionAuthorityFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($promotionProps.AuthoritySha256 -notmatch '^[0-9a-f]{64}$') {
            $blockers.Add('Production promotion props do not contain a valid promotion-authority SHA-256.')
        } elseif ($promotionProps.AuthoritySha256 -ne $authorityHash) {
            $blockers.Add('Production promotion props are bound to a different P0-5g promotion authority.')
        }
        if ($promotionProps.ValidatedEngineHead -notmatch '^[0-9a-f]{40}$') {
            $blockers.Add('Production promotion props do not contain a valid validated engine head.')
        } elseif ($promotionProps.ValidatedEngineHead -ne $engineHead) {
            $blockers.Add('Production promotion props are bound to a different validated engine head.')
        }
    }
} elseif ($productionSwitch) {
    $blockers.Add('Production switch is true without a tracked P0-5g promotion authority.')
}

$status = 'BLOCKED'
if ($blockers.Count -eq 0) {
    $status = if ($null -ne $promotionAuthority) { 'READY_FOR_REVIEW' } else { 'READY_TO_PROMOTE' }
}
if ($status -eq 'READY_TO_PROMOTE' -and $productionSwitch) {
    $blockers.Add('Production switch must remain false until promotion authority is generated.')
    $status = 'BLOCKED'
}

$result = [ordered]@{
    SchemaVersion = 3
    Phase = 'P0-5g'
    Verdict = $status
    ArsasHeadCommit = $arsasHead
    EngineEvidenceBaselineCommit = $baseline
    EngineHeadCommit = $engineHead
    EngineHeadCiConclusion = $EngineHeadCiConclusion
    EngineHeadIsEvidenceCompatibleDescendant = $engineIsDescendant -and $criticalChanges.Count -eq 0
    DiscoveryCriticalChanges = @($criticalChanges)
    PromotionTargetSha256 = $targetHash
    EngineLockSha256 = $engineLockHash
    ProductionSwitchEnabled = $productionSwitch
    PromotionAuthoritySha256 = $promotionProps.AuthoritySha256
    PromotionValidatedEngineHead = $promotionProps.ValidatedEngineHead
    PhysicalAuthorityPath = $physicalAuthorityFile
    PromotionAuthorityPath = $promotionAuthorityFile
    Blockers = @($blockers)
    Warnings = @($warnings)
}

if ([string]::IsNullOrWhiteSpace($OutputJson)) {
    $OutputJson = Join-Path $arsasRepo 'P0-5G-production-readiness.json'
}
$outputDirectory = Split-Path -Parent $OutputJson
if ($outputDirectory) { New-Item -ItemType Directory -Force $outputDirectory | Out-Null }
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputJson -Encoding utf8

Write-Host "P0-5g production readiness: $($result.Verdict)"
Write-Host "  ARSAS head: $arsasHead"
Write-Host "  engine baseline: $baseline"
Write-Host "  engine head: $engineHead"
Write-Host "  critical engine changes: $($criticalChanges.Count)"
Write-Host "  production switch: $productionSwitch"
foreach ($blocker in $blockers) { Write-Host "  BLOCKER: $blocker" }
Write-Host "  readiness JSON: $OutputJson"

if ($result.Verdict -eq 'BLOCKED' -and -not $NoFailExit) { exit 1 }
