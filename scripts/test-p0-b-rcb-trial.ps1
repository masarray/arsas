# P0-B is an exact, code-only descendant of the P0-A identity trial.
# Ancestry and safety flags are mandatory; no physical promotion is implied.
function Test-P0BRcbPlannerTrial {
    param([Parameter(Mandatory = $true)]$Lock)

    $trial = $Lock.p0BRcbPlannerTrial
    if ($null -eq $trial) { return $false }

    . (Join-Path $PSScriptRoot 'test-p0-a-identity-trial.ps1')
    $parent = $Lock | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $parent.commit = 'ccf31f60095ea65f9095514f0fa0102060962049'
    $parent.sourcePullRequest = 177
    if (-not (Test-P0AIdentityTrial -Lock $parent)) { return $false }

    return (
        [string]$Lock.repository -ceq 'masarray/ARIEC61850' -and
        [string]$Lock.ref -ceq 'main' -and
        [string]$Lock.commit -ceq 'edf929470ab50a3e3eec363718ca43585dd60be8' -and
        [int]$Lock.sourcePullRequest -eq 179 -and
        [string]$trial.status -ceq 'code-trial-not-physical' -and
        [string]$trial.baseEngineCommit -ceq 'ccf31f60095ea65f9095514f0fa0102060962049' -and
        [string]$trial.exactCommit -ceq $Lock.commit -and
        [int]$trial.enginePullRequest -eq 179 -and
        [int]$trial.consumerPullRequest -eq 626 -and
        [bool]$trial.targetScopedOnly -and
        [bool]$trial.globalCandidateAbortForbidden -and
        [bool]$trial.writeFreeReconciliation -and
        [bool]$trial.boundedContinuationRequired -and
        [bool]$trial.foreignReservationProtectionRequired -and
        [bool]$trial.occupiedForeignRcbRegression -and
        [bool]$trial.exactNameContradictoryDatasetBlocked -and
        [bool]$trial.indexedInstanceReadPriorityOnly -and
        [bool]$trial.physicalQualificationRequired -and
        [bool]$trial.mainlinePromotionForbidden
    )
}
