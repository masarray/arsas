# P0-C is a code-only typed-Read successor of the P0-B exact RCB trial.
# No physical promotion, silent source fallback, or engine wildcard pins.
function Test-P0CTypedReadTrial {
    param([Parameter(Mandatory = $true)]$Lock)
    $trial = $Lock.p0CTypedReadTrial
    if ($null -eq $trial) { return $false }

    . (Join-Path $PSScriptRoot 'test-p0-b-rcb-trial.ps1')
    $parent = $Lock | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $parent.commit = 'd9e795275447ea1dcc3bea435b57f948595de310'
    $parent.sourcePullRequest = 179
    if (-not (Test-P0BRcbPlannerTrial -Lock $parent)) { return $false }

    return (
        [string]$Lock.repository -ceq 'masarray/ARIEC61850' -and
        [string]$Lock.ref -ceq 'main' -and
        [string]$Lock.commit -ceq 'ba21d882f67de22c10d3155b449aac77aa8b4899' -and
        [int]$Lock.sourcePullRequest -eq 181 -and
        [string]$trial.status -ceq 'code-trial-not-physical' -and
        [string]$trial.baseEngineCommit -ceq 'd9e795275447ea1dcc3bea435b57f948595de310' -and
        [string]$trial.exactCommit -ceq $Lock.commit -and
        [int]$trial.enginePullRequest -eq 181 -and
        [int]$trial.consumerPullRequest -eq 627 -and
        [long]$trial.engineCiRun -eq 39244846312 -and
        [bool]$trial.strictInvokeId -and
        [bool]$trial.strictAccessOrdinal -and
        [bool]$trial.malformedNumericsNeverZero -and
        [bool]$trial.typedGvaRequired -and
        [bool]$trial.perReferenceFailureEvidence -and
        [bool]$trial.sgcbDeferredSeparated -and
        [bool]$trial.observationsSingleSource -and
        [bool]$trial.readOnly -and
        [bool]$trial.physicalQualificationRequired -and
        [bool]$trial.mainlinePromotionForbidden
    )
}
