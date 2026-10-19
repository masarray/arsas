# M2.17-K: exact, non-physical trial authority for typed C264 IEC 61850 ST reports.
# This guard does not promote the candidate engine to a physically tested R7/R10
# release. Historical R10/SCL provenance remains checked by its original gates.
function Test-M217KTypedStatusTrial {
    param([Parameter(Mandatory = $true)]$Lock)

    $trial = $Lock.m217KTypedStatusReportTrial
    if ($null -eq $trial) { return $false }

    $previous = '036e2c00d83a0815b6160147b14ea05975057965'
    $candidate = '51c2eefd61d6bcbe1116fda6b781b74bb9cf06da'

    # P0-A is a descendant of the exact M2.17-K code trial, never a replacement
    # for the historical physical authority. Re-run the original unmodified
    # M2.17-K guard against its preserved pin before admitting one exact child.
    if ([int]$Lock.sourcePullRequest -eq 177) {
        $identity = $Lock.p0AIdentityTrial
        $saved = $Lock.previousIdentityTrialPin
        if ($null -eq $identity -or $null -eq $saved) { return $false }
        $historicalView = $Lock | Select-Object *
        $historicalView.commit = $candidate
        $historicalView.sourcePullRequest = 173
        $historicalView.purpose = [string]$saved.purpose
        $parentValid = Test-M217KTypedStatusTrial -Lock $historicalView
        return (
            $parentValid -and
            [string]$Lock.repository -ceq 'masarray/ARIEC61850' -and
            [string]$Lock.ref -ceq 'main' -and
            [string]$saved.commit -ceq $candidate -and
            [int]$saved.sourcePullRequest -eq 173 -and
            [string]$saved.purpose -match '^M2.17-K isolated C264 acceptance trial' -and
            [string]$identity.baseEngineCommit -ceq $candidate -and
            [string]$identity.exactCommit -ceq 'ccf31f60095ea65f9095514f0fa0102060962049' -and
            [string]$Lock.commit -ceq [string]$identity.exactCommit -and
            [string]$Lock.purpose -match '^P0-A identity code-only trial' -and
            [int]$identity.enginePullRequest -eq 177 -and
            [int]$identity.consumerPullRequest -eq 624 -and
            [long]$identity.engineCiRun -eq 39228439974 -and
            [string]$identity.status -ceq 'code-trial-not-physical' -and
            [bool]$identity.exactDomainCaseRequired -and
            [bool]$identity.sclShaPinnedRequired -and
            [bool]$identity.exactEndpointAndAccessPointRequired -and
            [bool]$identity.discoveryWritesForbidden -and
            [bool]$identity.physicalQualificationRequired -and
            [bool]$identity.mainlinePromotionForbidden
        )
    }

    # Audit the COMPLETE previously accepted P10.9V code-only ancestry,
    # including physical R10 history, routing, subscriber integrity, canonical
    # ingress, ZIV BSC, commissioning and typed control gates. A new head
    # cannot silently bypass any of the older negative regression fixtures.
    . (Join-Path $PSScriptRoot 'test-p10-9f-core-routing-trial.ps1')
    $historicalView = $Lock | Select-Object *
    $historicalView.commit = $previous
    $historicalView.sourcePullRequest = 169
    $oldPinLineageStillValid = Test-P109FCoreRoutingTrial -Lock $historicalView

    return (
        $oldPinLineageStillValid -and
        [string]$Lock.repository -ceq 'masarray/ARIEC61850' -and
        [string]$Lock.ref -ceq 'main' -and
        [string]$Lock.commit -ceq $candidate -and
        [int]$Lock.sourcePullRequest -eq 173 -and
        [string]$Lock.purpose -match 'M2.17-K isolated C264 acceptance trial' -and
        [string]$trial.exactCommit -ceq $candidate -and
        [string]$trial.previousConsumerPin -ceq $previous -and
        [string]$Lock.p109VLiveBscAuthorityTrial.exactCommit -ceq $previous -and
        [int]$Lock.p109VLiveBscAuthorityTrial.enginePullRequest -eq 169 -and
        [string]$trial.status -ceq 'engine-ci-passed-consumer-ci-pending-not-physical' -and
        [int]$trial.sourcePullRequest -eq 173 -and
        [int]$trial.consumerPullRequest -eq 618 -and
        [long]$trial.engineCiRun -eq 38997437784 -and
        [string]$trial.physicalSource -ceq 'C264_LEGREK_SCC_CSWI3_RPTFIX.iid' -and
        [bool]$trial.typedStatusEnumProjection -and
        [bool]$trial.strictFcAndTupleEvidenceRequired -and
        [bool]$trial.noTextStructureParsing -and
        [bool]$trial.noSyntheticReportValues -and
        [bool]$trial.physicalQualificationRequired -and
        [bool]$trial.mainlinePromotionForbidden -and
        [bool]$trial.cyclicMmsProcessPollingForbidden
    )
}
