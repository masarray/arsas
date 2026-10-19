# Private physical acceptance remains independent of a Windows-green engine code trial.
# This checker is intentionally a strict allowlist for one immutable P0 trial SHA.
function Test-P0GlobalInteroperabilityTrial {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)] $Lock)

    $trial = $Lock.p0GlobalInteroperabilityTrial
    $ancestor = $Lock.m217KTypedStatusReportTrial
    if ($null -eq $trial -or $null -eq $ancestor) {
        return $false
    }

    return (
        $trial.status -eq 'engine-windows-ci-passed-not-physical' -and
        [int]$trial.sourcePullRequest -eq 175 -and
        [int]$trial.consumerPullRequest -eq 622 -and
        $trial.exactCommit -ceq '1ed776a689c8707be5db09a6318c4a7613229f95' -and
        $trial.baseEngineCommit -ceq $ancestor.exactCommit -and
        $ancestor.exactCommit -ceq '51c2eefd61d6bcbe1116fda6b781b74bb9cf06da' -and
        [int64]$trial.engineCiRun -eq 39216835641 -and
        [int]$trial.verifiedEngineTests -eq 1145 -and
        $trial.identityEvidence -eq 'synthetic-five-ld' -and
        $trial.rcbEvidence -eq 'synthetic-66-300-rcb-target-scoped-resumable' -and
        [bool]$trial.physicalQualificationRequired -and
        [bool]$trial.mainlinePromotionForbidden -and
        [bool]$trial.vendorBranchingForbidden -and
        [bool]$trial.readOnlyDiscoveryRequired -and
        [bool]$trial.rcbOwnershipProtectionRequired -and
        [bool]$trial.cyclicMmsProcessPollingForbidden -and
        $Lock.commit -ceq $trial.exactCommit -and
        [int]$Lock.sourcePullRequest -eq [int]$trial.sourcePullRequest
    )
}
