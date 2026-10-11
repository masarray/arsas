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
            throw 'SCL association physical-tested engine head differs from integration lock.'
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
