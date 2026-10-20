# P0-G is an SCL Edition 2.2 metadata identification successor of the
# immutable P0-C typed-Read engine. This gate never promotes physical evidence.
function Test-P0GSclEditionTrial {
    param([Parameter(Mandatory = $true)]$Lock)
    $trial = $Lock.p0GSclEditionTrial
    if ($null -eq $trial) { return $false }

    . (Join-Path $PSScriptRoot 'test-p0-c-typed-read-trial.ps1')
    $parent = $Lock | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
    $parent.commit = 'ba21d882f67de22c10d3155b449aac77aa8b4899'
    $parent.sourcePullRequest = 181
    if (-not (Test-P0CTypedReadTrial -Lock $parent)) { return $false }

    return (
        [string]$Lock.repository -ceq 'masarray/ARIEC61850' -and
        [string]$Lock.ref -ceq 'main' -and
        [string]$Lock.commit -ceq 'ded5036029706db1a752bb045d1fbc43b67fe9c6' -and
        [int]$Lock.sourcePullRequest -eq 182 -and
        [string]$trial.status -ceq 'code-trial-not-physical' -and
        [string]$trial.baseEngineCommit -ceq 'ba21d882f67de22c10d3155b449aac77aa8b4899' -and
        [string]$trial.exactCommit -ceq [string]$Lock.commit -and
        [int]$trial.enginePullRequest -eq 182 -and
        [long]$trial.engineCiRun -eq 39368865409 -and
        [bool]$trial.syntheticEdition1_2_21_22MatrixRequired -and
        [bool]$trial.preserveHistoricalSclNamespaceRequired -and
        [bool]$trial.unknownReleaseCannotDowngrade -and
        [bool]$trial.existingEditionEnumOrdinalsPreserved -and
        [bool]$trial.mmsEngineUnchanged -and
        [bool]$trial.physicalQualificationRequired -and
        [bool]$trial.mainlinePromotionForbidden -and
        [bool]$trial.vendorBranchingForbidden -and
        [bool]$trial.cyclicMmsProcessPollingForbidden -and
        [bool]$trial.controlAndRcbWritesForbidden
    )
}
