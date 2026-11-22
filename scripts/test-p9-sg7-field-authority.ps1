#Requires -Version 7.0
# Deterministic negative mutation tests: SG7 P9 acceptance is exact and fail-closed.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'p9-sg7-field-authority.ps1')
$root = Split-Path -Parent $PSScriptRoot
$lockJson = Get-Content (Join-Path $root 'engines/ARIEC61850.lock.json') -Raw
$fieldJson = Get-Content (Join-Path $root 'evidence/p9-sg7-20261122-field-acceptance.json') -Raw
function Copy-Object([string]$json) { return ConvertFrom-Json -InputObject $json -Depth 60 }
function Assert-Rejected([psobject]$Lock, [psobject]$Evidence, [string]$Case) {
    if (Test-P9Sg7FieldAuthority -Lock $Lock -Evidence $Evidence) {
        throw "P9 exact-SHA field acceptance failed open: $Case"
    }
}
if (-not (Test-P9Sg7FieldAuthority -Lock (Copy-Object $lockJson) -Evidence (Copy-Object $fieldJson))) {
    throw 'Exact supplied P9 SG7 field acceptance is not recognized.'
}
$count = 1
$badCases = @(
    @{part='Evidence';path='diagnosticSha256';value=('f'*64)},
    @{part='Evidence';path='applicationSourceTree';value=('f'*40)},
    @{part='Evidence';path='applicationMergeTestCommit';value=('f'*40)},
    @{part='Evidence';path='engineCommit';value=('f'*40)},
    @{part='Evidence';path='observed.staticReportBackedValues';value=56},
    @{part='Evidence';path='observed.pendingValues';value=21},
    @{part='Evidence';path='observed.controlConfirmations';value=0},
    @{part='Evidence';path='observed.bufferedOverflowAdvertised';value=0},
    @{part='Evidence';path='observed.sequenceDiscontinuities';value=0},
    @{part='Evidence';path='observed.cyclicMmsProcessPolling';value=$true},
    @{part='Evidence';path='qualification.neverClaimLosslessSoe';value=$false},
    @{part='Evidence';path='qualification.otherIEDInteroperabilityNotFieldQualified';value=$false},
    @{part='Lock';path='commit';value=('f'*40)},
    @{part='Lock';path='sourcePullRequest';value=192},
    @{part='Lock';path='mergedMainCommit';value=('f'*40)},
    @{part='Lock';path='physicalTestedCommit';value=('f'*40)},
    @{part='Lock';path='p9Sg7FourRcbIntegrationCandidate.exactCommit';value=('f'*40)},
    @{part='Lock';path='p9Sg7FourRcbIntegrationCandidate.currentCombinedPhysicalRetestRequired';value=$false},
    @{part='Lock';path='p9Sg7FourRcbIntegrationCandidate.losslessSoeNotClaimed';value=$false}
)
foreach ($bad in $badCases) {
    $lock = Copy-Object $lockJson
    $field = Copy-Object $fieldJson
    $obj = if ($bad.part -eq 'Lock') { $lock } else { $field }
    $parts = $bad.path.Split('.')
    for ($i = 0; $i -lt $parts.Count-1; $i++) { $obj = $obj.($parts[$i]) }
    $obj.($parts[-1]) = $bad.value
    Assert-Rejected -Lock $lock -Evidence $field -Case ($bad.part + '.' + $bad.path)
    $count++
}
Write-Host "P9 SG7 field authority: $count exact-positive/negative regression fixtures PASS."
