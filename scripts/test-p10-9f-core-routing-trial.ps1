# PowerShell 5.1/7 compatible. Code-verification does NOT grant physical acceptance.
# Exact audited code-only candidates (P10.9F/G/I/J/K); no wildcard SHA or runtime overrides.
function Test-P109FCoreRoutingTrial {
    param([Parameter(Mandatory = $true)] [object] $Lock)
    $candidate = $Lock.coreReportRoutingTrial
    $subscriber = $Lock.reportSubscriberIntegrityTrial
    $previousHead = (
        [string]$Lock.commit -ceq 'fa93c9fdc7cd98fd3624227ee21c3eefc68bcac7' -and
        [int]$Lock.sourcePullRequest -eq 164
    )
    # P10.9G is a precisely scoped successor to the P10.9F code-only pin,
    # NOT a promotion to physical or mainline acceptance.
    $subscriberIntegrityValid = (
        $null -ne $subscriber -and
        [string]$subscriber.status -ceq 'code-verified-not-physical' -and
        [int]$subscriber.issue -eq 532 -and
        [int]$subscriber.enginePullRequest -eq 165 -and
        [string]$subscriber.baseEngineCommit -ceq 'fa93c9fdc7cd98fd3624227ee21c3eefc68bcac7' -and
        [string]$subscriber.exactCommit -ceq 'd700b0df741b466afb8abe6cee1d70b1619d7b4d' -and
        [long]$subscriber.engineCiRun -eq 38180732078 -and
        [bool]$subscriber.physicalQualificationRequired -and
        [bool]$subscriber.mainlinePromotionForbidden -and
        [bool]$subscriber.fullDiscoveryFallbackForbidden -and
        [bool]$subscriber.cyclicMmsProcessPollingForbidden
    )
    $subscriberHead = (
        [string]$Lock.commit -ceq 'd700b0df741b466afb8abe6cee1d70b1619d7b4d' -and
        [int]$Lock.sourcePullRequest -eq 165 -and $subscriberIntegrityValid
    )
    # P10.9I is allowed only as the exact successor to the independently
    # qualified P10.9G engine record; neither entry grants physical authority.
    $canonical = $Lock.canonicalReportIngressIntegrityTrial
    $canonicalIntegrityValid = (
        $subscriberIntegrityValid -and
        $null -ne $canonical -and
        [string]$canonical.status -ceq 'code-verified-not-physical' -and
        [int]$canonical.issue -eq 536 -and
        [int]$canonical.enginePullRequest -eq 166 -and
        [string]$canonical.baseEngineCommit -ceq 'd700b0df741b466afb8abe6cee1d70b1619d7b4d' -and
        [string]$canonical.exactCommit -ceq '9bd01908ebd6fdf50a74f0a421b4e79613b878d4' -and
        [long]$canonical.engineCiRun -eq 38214165576 -and
        [bool]$canonical.physicalQualificationRequired -and
        [bool]$canonical.mainlinePromotionForbidden -and
        [bool]$canonical.fullDiscoveryFallbackForbidden -and
        [bool]$canonical.cyclicMmsProcessPollingForbidden
    )
    $canonicalHead = (
        [string]$Lock.commit -ceq '9bd01908ebd6fdf50a74f0a421b4e79613b878d4' -and
        [int]$Lock.sourcePullRequest -eq 166 -and $canonicalIntegrityValid
    )
    # P10.9J is admitted only if P10.9I still satisfies *every* explicit
    # historical guard; neither the valid OID notation nor BSC metadata
    # grants production/physical authority.
    $ziv = $Lock.zivSclBscInteroperabilityTrial
    $zivIntegrityValid = (
        $canonicalIntegrityValid -and $null -ne $ziv -and
        [string]$ziv.status -ceq 'code-verified-not-physical' -and
        [int]$ziv.issue -eq 538 -and
        [int]$ziv.enginePullRequest -eq 167 -and
        [string]$ziv.baseEngineCommit -ceq '9bd01908ebd6fdf50a74f0a421b4e79613b878d4' -and
        [string]$ziv.exactCommit -ceq '67c6635b7e16139c0661e559a2e6946527c1442a' -and
        [long]$ziv.engineCiRun -eq 38219429896 -and
        [bool]$ziv.sclApTitlePackedPrefixOnly -and
        [bool]$ziv.bscLiveSemanticGateRequired -and
        [bool]$ziv.physicalQualificationRequired -and
        [bool]$ziv.mainlinePromotionForbidden -and
        [bool]$ziv.fullDiscoveryFallbackForbidden -and
        [bool]$ziv.cyclicMmsProcessPollingForbidden
    )
    $zivHead = (
        [string]$Lock.commit -ceq '67c6635b7e16139c0661e559a2e6946527c1442a' -and
        [int]$Lock.sourcePullRequest -eq 167 -and $zivIntegrityValid
    )
    # P10.9K does not replace any earlier physical/provenance gate.
    $generic = $Lock.genericControlInteroperabilityTrial
    $genericHead = (
        [string]$Lock.commit -ceq 'a43b9b4864a9565c02f544d4135fe157c2fac577' -and
        [int]$Lock.sourcePullRequest -eq 168 -and
        $zivIntegrityValid -and $null -ne $generic -and
        [string]$generic.status -ceq 'code-verified-not-physical' -and
        [int]$generic.issue -eq 540 -and
        [int]$generic.enginePullRequest -eq 168 -and
        [string]$generic.baseEngineCommit -ceq [string]$ziv.exactCommit -and
        [string]$generic.exactCommit -ceq 'a43b9b4864a9565c02f544d4135fe157c2fac577' -and
        [long]$generic.engineCiRun -eq 38231327892 -and
        [bool]$generic.typedSemanticGateRequired -and
        [bool]$generic.associationScopedInventoryRequired -and
        [bool]$generic.vendorDispatchForbidden -and
        [bool]$generic.physicalQualificationRequired -and
        [bool]$generic.mainlinePromotionForbidden -and
        [bool]$generic.fullDiscoveryFallbackForbidden -and
        [bool]$generic.cyclicMmsProcessPollingForbidden
    )
    # Exact P10.9L read-only engine candidate, preserving every historical gate.
    $probe = $Lock.p109LControlRecoveryTrial
    $probeHead = (
        [string]$Lock.commit -ceq '55fa34cef9a4abee589b9a6d94be1d0361375d71' -and
        [int]$Lock.sourcePullRequest -eq 169 -and
        $zivIntegrityValid -and $null -ne $generic -and $null -ne $probe -and
        [string]$generic.exactCommit -ceq 'a43b9b4864a9565c02f544d4135fe157c2fac577' -and
        [string]$generic.baseEngineCommit -ceq [string]$ziv.exactCommit -and
        [int]$generic.enginePullRequest -eq 168 -and
        [long]$generic.engineCiRun -eq 38231327892 -and
        [bool]$generic.typedSemanticGateRequired -and
        [bool]$generic.associationScopedInventoryRequired -and
        [bool]$generic.vendorDispatchForbidden -and
        [bool]$generic.physicalQualificationRequired -and
        [bool]$generic.mainlinePromotionForbidden -and
        [bool]$generic.fullDiscoveryFallbackForbidden -and
        [bool]$generic.cyclicMmsProcessPollingForbidden -and
        [string]$probe.status -ceq 'code-trial-not-physical' -and
        [int]$probe.issue -eq 525 -and
        [int]$probe.enginePullRequest -eq 169 -and
        [string]$probe.baseEngineCommit -ceq [string]$generic.exactCommit -and
        [string]$probe.exactCommit -ceq '55fa34cef9a4abee589b9a6d94be1d0361375d71' -and
        [bool]$probe.readOnlyCtlModelProbeRequired -and
        [bool]$probe.noControlWriteOnProbe -and
        [bool]$probe.physicalQualificationRequired -and
        [bool]$probe.mainlinePromotionForbidden -and
        [bool]$probe.fullDiscoveryFallbackForbidden -and
        [bool]$probe.cyclicMmsProcessPollingForbidden
    )
    if (-not ($previousHead -or $subscriberHead -or $canonicalHead -or $zivHead -or $genericHead -or $probeHead) -or
        [string]$Lock.repository -cne 'masarray/ARIEC61850' -or
        [string]$Lock.ref -cne 'main' -or $null -eq $candidate) { return $false }

    # A code-only candidate cannot be interpreted as physical authority,
    # including CI builds targeting or running on main.
    foreach ($ref in @($env:PR_BASE_REF, $env:SOURCE_REF_NAME,
                        $env:GITHUB_BASE_REF, $env:GITHUB_REF_NAME)) {
        if ([string]$ref -ceq 'main' -or [string]$ref -ceq 'refs/heads/main') {
            return $false
        }
    }
    $p10 = $Lock.p10IdentityTrial
    $closure = $Lock.datasetFirstDiscoveryTrial
    $association = $Lock.sclAssociationInteroperability
    $historical = $Lock.smartInteroperabilityCodeTrial
    $serverAt = $Lock.sclServerAtTrial
    if ($null -eq $p10 -or $null -eq $closure -or $null -eq $association -or
        $null -eq $historical -or $null -eq $serverAt) { return $false }

    return (
        [string]$candidate.status -ceq 'code-verified-not-physical' -and
        [int]$candidate.issue -eq 531 -and
        [int]$candidate.simulatorPullRequest -eq 163 -and
        [int]$candidate.corePullRequest -eq 164 -and
        [string]$candidate.baseEngineCommit -ceq '00ad2819b99ffe6b3f885e59aa24a6afb2b4d8e1' -and
        [string]$candidate.simulatorFifoCommit -ceq 'ef0fe44f3b40e47dc18b1aac4476b635e67d531e' -and
        [string]$candidate.exactCommit -ceq 'fa93c9fdc7cd98fd3624227ee21c3eefc68bcac7' -and
        [long]$candidate.simulatorCiRun -eq 38141582981 -and
        [long]$candidate.coreCiRun -eq 38142115574 -and
        [bool]$candidate.physicalQualificationRequired -and
        [bool]$candidate.mainlinePromotionForbidden -and
        [bool]$candidate.fullDiscoveryFallbackForbidden -and
        [bool]$candidate.cyclicMmsProcessPollingForbidden -and
        [string]$p10.exactCommit -ceq [string]$candidate.baseEngineCommit -and
        [string]$p10.exactCommit -ceq '00ad2819b99ffe6b3f885e59aa24a6afb2b4d8e1' -and
        [int]$p10.enginePullRequest -eq 161 -and
        [string]$p10.baseEngineCommit -ceq [string]$closure.exactCommit -and
        [string]$p10.identitySource -ceq 'TrustedSclExactDomainMatch' -and
        [string]$p10.status -ceq 'code-trial-not-physical' -and
        [bool]$p10.physicalQualificationRequired -and
        [bool]$p10.fullNameListFallbackForbidden -and
        [bool]$p10.cyclicMmsProcessPollingForbidden -and
        [string]$closure.exactCommit -ceq '8b0c0f7e695b016834e40a6f068994dcc945cb1a' -and
        [string]$closure.baseEngineCommit -ceq [string]$serverAt.exactCommit -and
        [string]$serverAt.exactCommit -ceq '352c81e6a798635c6addcee0683235ca87ad416d' -and
        [string]$serverAt.baseEngineCommit -ceq [string]$historical.exactCommit -and
        [string]$historical.exactCommit -ceq '9c5292570f55dd81be1b3a6b56f937e5ba1ed276' -and
        [string]$association.testedEngineCommit -ceq '84e9820e5a32690475960e49d5ef74e6637847fd' -and
        [string]$association.mergedEngineCommit -ceq 'e5deed1d8aa11d97991695c6e390baafea7ab797' -and
        [string]$association.contractId -ceq 'SCL-ASSOC-PARTIAL-ADDR-PHYSICAL' -and
        [bool]$association.preservesR10DiscoveryModelBaseline -and
        [bool]$association.fullDiscoveryFallbackForbidden -and
        [bool]$association.cyclicMmsProcessPollingForbidden -and
        [string]$Lock.mergedMainCommit -ceq '648124097621046f5f127ceb1cf853fea54db730' -and
        [string]$Lock.previousStablePin.commit -ceq [string]$Lock.mergedMainCommit
    )
}
