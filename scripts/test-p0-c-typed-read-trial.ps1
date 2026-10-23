# P0-C is a code-only typed-Read successor of the P0-B exact RCB trial.
# No physical promotion, silent source fallback, or engine wildcard pins.
function Test-P0CTypedReadTrial {
    param([Parameter(Mandatory = $true)]$Lock)
    # Stage-attributed P0-C successor: exact trial SHA only, qualified by
    # 1182 Windows engine regressions. Preserve the pre-existing 400ms negative
    # LastApplError observation gate; there is NO faster control qualification.
    # Validate the full immutable ancestry recursively through #183 -> #181
    # -> #179 instead of weakening the SCL/R10 historical guard.
    if ([string]$Lock.commit -ceq '9c7cb40364023a7b217e5ec31ab3105ba90142eb') {
        $successor = $Lock.p0CStageObservationTrial
        if ($null -eq $successor -or
            [int]$Lock.sourcePullRequest -ne 184 -or
            [string]$successor.status -cne 'engine-ci-success-consumer-ci-pending-not-physical' -or
            [string]$successor.baseEngineCommit -cne 'c85d4e20db722bd0c2729f2533b323b13d3ef735' -or
            [string]$successor.exactCommit -cne [string]$Lock.commit -or
            [int]$successor.enginePullRequest -ne 184 -or
            [int]$successor.engineCiOnlyPullRequest -ne 185 -or
            [long]$successor.engineCiRun -ne 39505819831 -or
            [int]$successor.engineTestsPassed -ne 1182 -or
            [int]$successor.consumerIntegrationPullRequest -ne 655 -or
            -not [bool]$successor.sbo400msSafetyGatePreserved -or
            -not [bool]$successor.negativeAndDelayedLastApplErrorRegression -or
            -not [bool]$successor.noUnqualifiedImmediateOperate -or
            -not [bool]$successor.physicalLatencyQualificationRequired -or
            -not [bool]$successor.mainlinePromotionForbidden) { return $false }
        $parent = $Lock | ConvertTo-Json -Depth 100 | ConvertFrom-Json
        $parent.commit = $successor.baseEngineCommit
        $parent.sourcePullRequest = 183
        return (Test-P0CTypedReadTrial -Lock $parent)
    }

    # Explicit, immutable code-only successor; revalidate every historical P0-C
    # invariant against its original pin rather than relabeling it as accepted.
    if ([string]$Lock.commit -ceq 'c85d4e20db722bd0c2729f2533b323b13d3ef735') {
        $successor = $Lock.controlReportIsolationTrial
        if ($null -eq $successor -or
            [int]$Lock.sourcePullRequest -ne 183 -or
            [string]$successor.status -cne 'code-trial-not-physical' -or
            [string]$successor.baseEngineCommit -cne 'ba21d882f67de22c10d3155b449aac77aa8b4899' -or
            [string]$successor.exactCommit -cne $Lock.commit -or
            [int]$successor.enginePullRequest -ne 183 -or
            [int]$successor.consumerPullRequest -ne 635 -or
            -not [bool]$successor.physicalQualificationRequired -or
            -not [bool]$successor.mainlinePromotionForbidden -or
            -not [bool]$successor.controlChecksUnchanged -or
            -not [bool]$successor.controlTimeoutsUnchanged -or
            -not [bool]$successor.preservesTypedReadAuthority) { return $false }
        $original = $Lock | ConvertTo-Json -Depth 100 | ConvertFrom-Json
        $original.commit = $successor.baseEngineCommit
        $original.sourcePullRequest = 181
        return (Test-P0CTypedReadTrial -Lock $original)
    }

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
