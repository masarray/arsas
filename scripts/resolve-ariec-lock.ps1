#Requires -Version 7.0
<#
.SYNOPSIS
Resolve the exact, immutable ARIEC61850 integration SHA from a tracked lock.
.DESCRIPTION
Read-only and fail-closed. Historical physical evidence remains separate from
the current integration pin; this resolver never rewrites either one.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$LockPath,

    [string]$GitHubOutput = ''
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $LockPath -PathType Leaf)) {
    throw "ARIEC61850 lock file does not exist: $LockPath"
}

try {
    $lock = Get-Content -LiteralPath $LockPath -Raw -Encoding UTF8 |
        ConvertFrom-Json -Depth 40
}
catch {
    throw "ARIEC61850 lock cannot be parsed: $($_.Exception.Message)"
}

if ([int]$lock.schemaVersion -ne 1) {
    throw 'ARIEC61850 lock schemaVersion must be 1.'
}
if ([string]$lock.repository -cne 'masarray/ARIEC61850') {
    throw 'ARIEC61850 repository must match the trusted masarray/ARIEC61850 source.'
}
if ([string]$lock.ref -cne 'main') {
    throw 'ARIEC61850 integration ref must remain main; checkout is pinned by SHA.'
}

function Assert-Sha {
    param([string]$Value, [string]$Name)
    if ($Value -cnotmatch '^[0-9a-f]{40}$') {
        throw "Invalid immutable SHA field $Name."
    }
}

Assert-Sha -Value ([string]$lock.commit) -Name 'commit'

if ($null -ne $lock.physicalTestedCommit) {
    Assert-Sha -Value ([string]$lock.physicalTestedCommit) -Name 'physicalTestedCommit'
}
if ($null -ne $lock.mergedMainCommit) {
    Assert-Sha -Value ([string]$lock.mergedMainCommit) -Name 'mergedMainCommit'
}
if ($null -ne $lock.mergedMainTree) {
    Assert-Sha -Value ([string]$lock.mergedMainTree) -Name 'mergedMainTree'
}
if ($null -ne $lock.previousStablePin) {
    Assert-Sha -Value ([string]$lock.previousStablePin.commit) -Name 'previousStablePin.commit'
    if ($null -ne $lock.mergedMainCommit -and
        [string]$lock.previousStablePin.commit -cne [string]$lock.mergedMainCommit) {
        throw 'Previous stable pin no longer matches historical merged-main authority.'
    }
}
if ($null -ne $lock.sclAssociationInteroperability) {
    $association = $lock.sclAssociationInteroperability
    if ($null -ne $association.testedEngineCommit) {
        Assert-Sha -Value ([string]$association.testedEngineCommit) -Name 'sclAssociationInteroperability.testedEngineCommit'
        if ([string]$association.testedEngineCommit -cne [string]$lock.commit) {
            # Historical SCL physical acceptance is immutable. The active
            # runtime SHA may differ ONLY for an exact, ancestry-bound trial.
            $trial = $lock.smartInteroperabilityCodeTrial
            $serverAt = $lock.sclServerAtTrial
            $closure = $lock.datasetFirstDiscoveryTrial
            $p10 = $lock.p10IdentityTrial
            $historicalAnchorValid = (
                [string]$association.testedEngineCommit -ceq '84e9820e5a32690475960e49d5ef74e6637847fd' -and
                [string]$association.mergedEngineCommit -ceq 'e5deed1d8aa11d97991695c6e390baafea7ab797' -and
                [string]$lock.mergedMainCommit -ceq '648124097621046f5f127ceb1cf853fea54db730' -and
                [string]$lock.previousStablePin.commit -ceq [string]$lock.mergedMainCommit -and
                [string]$association.contractId -ceq 'SCL-ASSOC-PARTIAL-ADDR-PHYSICAL' -and
                [bool]$association.preservesR10DiscoveryModelBaseline -and
                [bool]$association.fullDiscoveryFallbackForbidden -and
                [bool]$association.cyclicMmsProcessPollingForbidden
            )
            $trialValid = $null -ne $trial -and
                [string]$trial.exactCommit -ceq '9c5292570f55dd81be1b3a6b56f937e5ba1ed276' -and
                [string]$trial.status -ceq 'code-verified-not-physical' -and
                [int]$trial.integrationPullRequest -eq 148 -and
                [bool]$trial.physicalPromotionRequired -and
                [bool]$trial.fullDiscoveryFallbackForbidden -and
                [bool]$trial.vendorBranchingForbidden
            $serverAtValid = $trialValid -and $null -ne $serverAt -and
                [string]$serverAt.exactCommit -ceq '352c81e6a798635c6addcee0683235ca87ad416d' -and
                [string]$serverAt.baseEngineCommit -ceq [string]$trial.exactCommit -and
                [int]$serverAt.sourcePullRequest -eq 153 -and
                [string]$serverAt.status -ceq 'code-verified-candidate-not-physical' -and
                [bool]$serverAt.physicalQualificationRequired -and
                [bool]$serverAt.noAutomaticMmsFailover
            $closureValid = $serverAtValid -and $null -ne $closure -and
                [string]$closure.exactCommit -ceq '8b0c0f7e695b016834e40a6f068994dcc945cb1a' -and
                [string]$closure.baseEngineCommit -ceq [string]$serverAt.exactCommit -and
                [int]$closure.enginePullRequest -eq 157 -and
                [int]$closure.consumerPullRequest -eq 497 -and
                [string]$closure.status -ceq 'code-trial-not-physical' -and
                [bool]$closure.physicalQualificationRequired -and
                [bool]$closure.fullNameListFallbackForbidden -and
                [bool]$closure.cyclicMmsProcessPollingForbidden
            $p10Valid = $closureValid -and $null -ne $p10 -and
                [string]$p10.exactCommit -ceq '00ad2819b99ffe6b3f885e59aa24a6afb2b4d8e1' -and
                [string]$p10.baseEngineCommit -ceq [string]$closure.exactCommit -and
                [int]$p10.enginePullRequest -eq 161 -and
                [string]$p10.status -ceq 'code-trial-not-physical' -and
                [string]$p10.identitySource -ceq 'TrustedSclExactDomainMatch' -and
                [bool]$p10.physicalQualificationRequired -and
                [bool]$p10.fullNameListFallbackForbidden -and
                [bool]$p10.cyclicMmsProcessPollingForbidden

            . (Join-Path $PSScriptRoot 'test-p10-9f-core-routing-trial.ps1')
            . (Join-Path $PSScriptRoot 'test-m217k-typed-status-trial.ps1')
            $coreRoutingTrialValid = Test-P109FCoreRoutingTrial -Lock $lock
            $m217KTrialValid = Test-M217KTypedStatusTrial -Lock $lock
            . (Join-Path $PSScriptRoot 'test-p0-a-identity-trial.ps1')
            $p0AIdentityTrialValid = Test-P0AIdentityTrial -Lock $lock
            . (Join-Path $PSScriptRoot 'test-p0-b-rcb-trial.ps1')
            $p0BRcbTrialValid = Test-P0BRcbPlannerTrial -Lock $lock
            $acceptedTrial = (
                ($trialValid -and [string]$lock.commit -ceq [string]$trial.exactCommit -and [int]$lock.sourcePullRequest -eq 148) -or
                ($serverAtValid -and [string]$lock.commit -ceq [string]$serverAt.exactCommit -and [int]$lock.sourcePullRequest -eq 153) -or
                ($closureValid -and [string]$lock.commit -ceq [string]$closure.exactCommit -and [int]$lock.sourcePullRequest -eq 157) -or
                ($p10Valid -and [string]$lock.commit -ceq [string]$p10.exactCommit -and [int]$lock.sourcePullRequest -eq 161) -or
                ($p10Valid -and $coreRoutingTrialValid) -or
                $m217KTrialValid -or
                $p0AIdentityTrialValid -or
                $p0BRcbTrialValid
            )
            if (-not $historicalAnchorValid -or -not $acceptedTrial) {
                throw 'SCL association physical-tested engine head differs from integration lock.'
            }
            Write-Host 'Exact stacked code trial recognized; physical SCL-association/R10 authority remains historical and promotion-blocked.'
        }
    }
    if ($null -ne $association.mergedEngineCommit) {
        Assert-Sha -Value ([string]$association.mergedEngineCommit) -Name 'sclAssociationInteroperability.mergedEngineCommit'
    }
}

$resolved = [pscustomobject]@{
    repository = [string]$lock.repository
    ref = [string]$lock.ref
    commit = [string]$lock.commit
    physicalTestedCommit = [string]$lock.physicalTestedCommit
    mergedMainCommit = [string]$lock.mergedMainCommit
    baselineIsCurrent = ([string]$lock.commit -ceq [string]$lock.mergedMainCommit)
}

if ($GitHubOutput) {
    @(
        "repository=$($resolved.repository)"
        "ref=$($resolved.ref)"
        "commit=$($resolved.commit)"
        "physical_tested_commit=$($resolved.physicalTestedCommit)"
        "merged_main_commit=$($resolved.mergedMainCommit)"
        "baseline_is_current=$($resolved.baselineIsCurrent.ToString().ToLowerInvariant())"
    ) | Out-File -LiteralPath $GitHubOutput -Encoding UTF8 -Append
}

Write-Host "Verified immutable ARIEC61850 integration SHA $($resolved.commit)."
Write-Host "Historical engine main authority remains $($resolved.mergedMainCommit)."
return $resolved
