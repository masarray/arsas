#Requires -Version 7.0
<#
Exact-source SG7 P9 field-acceptance helper. This authorizes only the reviewed
P9 engine successor for current-runtime CI routing. It does NOT rewrite,
replace, or reclassify historical R10/SCL physical authority and it does NOT
certify lossless SOE, other IEDs, or future source changes.
#>
function Test-P9Sg7FieldAuthority {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [psobject]$Lock,
        [Parameter(Mandatory)] [psobject]$Evidence
    )

    $p9 = $Lock.p9Sg7FourRcbIntegrationCandidate
    if ($null -eq $p9 -or $null -eq $Evidence.observed -or
        $null -eq $Evidence.qualification) { return $false }

    # Match immutable executable source tree and verified diagnostic fingerprint.
    # App + engine SHA are from the user-tested GitHub PR merge build.
    if ([int]$Evidence.schemaVersion -ne 1 -or
        [string]$Evidence.applicationMergeTestCommit -cne '13284bf6706a42470807368d0e2afcf65e2f95ed' -or
        [string]$Evidence.applicationPullRequestHead -cne '02dc16f0d5012cf174f333fff11151ed195987bc' -or
        [string]$Evidence.applicationSourceTree -cne '8b36922dd0fb64b528380d6e0ba687d0d7214954' -or
        [string]$Evidence.applicationParentMain -cne '63d5e5bf793f580a96f9c752aa58267b0ffc200a' -or
        [string]$Evidence.diagnosticSha256 -cne '91d82b42b8c1e1e9196c6026ede88a04655cf0139d3a4e6111bf28b3dac7eda6' -or
        [string]$Evidence.engineCommit -cne '24077e48e393ce3dc2cd1409db6e96f27aa1e368') {
        return $false
    }

    # Historical R10 anchors remain intact and must be checked independently
    # against interoperability-reference-target.json by the governing workflow.
    if ([string]$Lock.mergedMainCommit -cne '648124097621046f5f127ceb1cf853fea54db730' -or
        [string]$Lock.previousStablePin.commit -cne [string]$Lock.mergedMainCommit -or
        [string]$Lock.physicalTestedCommit -cne '9935d6902d786cc69b299260fe36b835944d5e81' -or
        [int]$Lock.previousStablePin.sourcePullRequest -ne 135 -or
        [string]$Lock.commit -cne [string]$Evidence.engineCommit -or
        [int]$Lock.sourcePullRequest -ne 203 -or
        [string]$p9.exactCommit -cne [string]$Lock.commit -or
        [string]$p9.baselineEngineCommit -cne 'f75e323e9f8443aa20e0a3818a1d5d77255b2c0f' -or
        [string]$p9.status -cne 'source-and-prior-field-trial-proven-current-combined-release-pending' -or
        [long]$p9.engineCiRun -ne 44720030989L -or
        [string]$p9.arsasFieldTrialSourceCommit -cne 'a3dca3e8483a7a8b1cb8047e7034db75380f8baa' -or
        [long]$p9.arsasFieldTrialRun -ne 44741754523L -or
        [int]$p9.fieldObservedReportBackedValues -ne 77 -or
        [int]$p9.fieldIndependentlyRoutedRcbCount -ne 4 -or
        -not [bool]$p9.reportedBrcbBufferOverflow -or
        -not [bool]$p9.cyclicMmsProcessPollingForbidden -or
        -not [bool]$p9.automaticRcbMutationForbidden -or
        -not [bool]$p9.noAutomaticGiRetry -or
        -not [bool]$p9.currentCombinedPhysicalRetestRequired -or
        -not [bool]$p9.losslessSoeNotClaimed) {
        return $false
    }

    $obs = $Evidence.observed
    $qualification = $Evidence.qualification
    if ([int]$obs.staticReportBackedValues -ne 77 -or
        [int]$obs.selectedValues -ne 77 -or
        [int]$obs.pendingValues -ne 0 -or
        [int]$obs.reportFrames -ne 128 -or
        [int]$obs.controlConfirmations -ne 2 -or
        [int]$obs.bufferedOverflowAdvertised -ne 4 -or
        [int]$obs.sequenceDiscontinuities -ne 7 -or
        [int]$obs.missingOptFldsDecoded -ne 0 -or
        [bool]$obs.cyclicMmsProcessPolling -or
        -not [bool]$obs.eventLogControlQualityBlank -or
        (@($obs.independentlyRoutedBrcbs) -join '|') -cne 'BrcbA01|BrcbB01|BrcbD01|BrcbI01' -or
        (@($obs.controlFeedback) -join '|') -cne 'Closed|Open' -or
        -not [bool]$qualification.physicalFunctionalTestPassed -or
        -not [bool]$qualification.exactPrMergeTreeEqualsPrHead -or
        -not [bool]$qualification.neverClaimLosslessSoe -or
        -not [bool]$qualification.timeAndClockQualityNotCertified -or
        -not [bool]$qualification.historicalR10FieldEvidencePreserved -or
        -not [bool]$qualification.prohibitsAutomaticControl -or
        -not [bool]$qualification.noAutomaticGiRetry -or
        -not [bool]$qualification.noAutomaticRcbMutation -or
        -not [bool]$qualification.otherIEDInteroperabilityNotFieldQualified) {
        return $false
    }
    return $true
}
