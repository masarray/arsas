namespace ARSAS.Tests;

public sealed class SmartInteroperabilityTrialGuardRegressionTests
{
    [Fact]
    public void InteroperabilityGuard_SeparatesCodeTrialFromPhysicalAssociationAuthority()
    {
        var workflow = Read(".github/workflows/interoperability-reference-guard.yml");

        Assert.Contains("$lock.smartInteroperabilityCodeTrial", workflow, StringComparison.Ordinal);
        Assert.Contains("code-verified-not-physical", workflow, StringComparison.Ordinal);
        // P6.1 remains frozen, and exactly one P6.2 SHA is accepted only as
        // a stacked CodeVerified candidate with physical promotion blocked.
        Assert.Contains("$lock.sclServerAtTrial", workflow, StringComparison.Ordinal);
        Assert.Contains("$serverAt.baseEngineCommit -eq $trial.exactCommit", workflow, StringComparison.Ordinal);
        Assert.Contains("$serverAt.physicalQualificationRequired", workflow, StringComparison.Ordinal);
        Assert.Contains("$serverAt.noAutomaticMmsFailover", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "$lock.sclAssociationInteroperability.testedEngineCommit -ne $association.engine.testedHead",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Code trial replaced or mutated the physically accepted SCL-association ancestry",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainlineReadiness_AllowsStackedTrialButFailsClosedWhenRetargetedToMain()
    {
        var workflow = Read(".github/workflows/smart-discovery-mainline-readiness.yml");

        Assert.Contains("PR_BASE_REF: ${{ github.base_ref }}", workflow, StringComparison.Ordinal);
        Assert.Contains("$lock.smartInteroperabilityCodeTrial", workflow, StringComparison.Ordinal);
        Assert.Contains("$env:PR_BASE_REF -eq 'main'", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "Unqualified P6.1/P6.2 trial cannot pass mainline readiness before fresh physical qualification",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Stacked consumer trial accepted for CI",
            workflow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TrialLock_PreservesHistoricalPhysicalAuthorityAndExplicitlyRequiresPhysicalPromotion()
    {
        var lockJson = Read("engines/ARIEC61850.lock.json");

        using var document = System.Text.Json.JsonDocument.Parse(lockJson);
        var root = document.RootElement;
        var trial = root.GetProperty("smartInteroperabilityCodeTrial");
        var association = root.GetProperty("sclAssociationInteroperability");

        Assert.Equal("code-verified-not-physical", trial.GetProperty("status").GetString());
        Assert.True(trial.GetProperty("physicalPromotionRequired").GetBoolean());
        Assert.Equal("9c5292570f55dd81be1b3a6b56f937e5ba1ed276",
            trial.GetProperty("exactCommit").GetString());
        var serverAt = root.GetProperty("sclServerAtTrial");
        Assert.Equal("code-verified-candidate-not-physical", serverAt.GetProperty("status").GetString());
        Assert.Equal(153, serverAt.GetProperty("sourcePullRequest").GetInt32());
        Assert.Equal(trial.GetProperty("exactCommit").GetString(),
            serverAt.GetProperty("baseEngineCommit").GetString());
        var closure = root.GetProperty("datasetFirstDiscoveryTrial");
        Assert.Equal("code-trial-not-physical", closure.GetProperty("status").GetString());
        Assert.Equal(157, closure.GetProperty("enginePullRequest").GetInt32());
        Assert.Equal(serverAt.GetProperty("exactCommit").GetString(),
            closure.GetProperty("baseEngineCommit").GetString());
        var p10 = root.GetProperty("p10IdentityTrial");
        Assert.Equal(closure.GetProperty("exactCommit").GetString(),
            p10.GetProperty("baseEngineCommit").GetString());
        var codeTrial = root.GetProperty("coreReportRoutingTrial");
        Assert.Equal(p10.GetProperty("exactCommit").GetString(),
            codeTrial.GetProperty("baseEngineCommit").GetString());
        var subscriberTrial = root.GetProperty("reportSubscriberIntegrityTrial");
        Assert.Equal(codeTrial.GetProperty("exactCommit").GetString(),
            subscriberTrial.GetProperty("baseEngineCommit").GetString());
        var canonicalTrial = root.GetProperty("canonicalReportIngressIntegrityTrial");
        Assert.Equal(subscriberTrial.GetProperty("exactCommit").GetString(),
            canonicalTrial.GetProperty("baseEngineCommit").GetString());
        Assert.Equal("9bd01908ebd6fdf50a74f0a421b4e79613b878d4",
            canonicalTrial.GetProperty("exactCommit").GetString());
        var zivTrial = root.GetProperty("zivSclBscInteroperabilityTrial");
        Assert.Equal(canonicalTrial.GetProperty("exactCommit").GetString(),
            zivTrial.GetProperty("baseEngineCommit").GetString());
        Assert.Equal("67c6635b7e16139c0661e559a2e6946527c1442a",
            zivTrial.GetProperty("exactCommit").GetString());
        var genericTrial = root.GetProperty("genericControlInteroperabilityTrial");
        Assert.Equal(zivTrial.GetProperty("exactCommit").GetString(),
            genericTrial.GetProperty("baseEngineCommit").GetString());
        var recovery = root.GetProperty("p109LControlRecoveryTrial");
        Assert.Equal(genericTrial.GetProperty("exactCommit").GetString(),
            recovery.GetProperty("baseEngineCommit").GetString());
        var commissioning = root.GetProperty("p109QControlModelCommissioningTrial");
        Assert.Equal(recovery.GetProperty("exactCommit").GetString(),
            commissioning.GetProperty("baseEngineCommit").GetString());
        var simpleEditor = root.GetProperty("p109RSimpleCtlModelEditorTrial");
        Assert.Equal(commissioning.GetProperty("exactCommit").GetString(),
            simpleEditor.GetProperty("baseEngineCommit").GetString());
        var position = root.GetProperty("p109TPositionControlInteroperabilityTrial");
        Assert.Equal(simpleEditor.GetProperty("exactCommit").GetString(),
            position.GetProperty("baseEngineCommit").GetString());
        var positionStatus = root.GetProperty("p109UPositionStatusNameListRecoveryTrial");
        Assert.Equal(position.GetProperty("exactCommit").GetString(),
            positionStatus.GetProperty("baseEngineCommit").GetString());
        // P10.9U remains immutable historical evidence. P10.9V is the
        // exact tested successor, not a rewrite of that accepted lineage.
        Assert.Equal("ad43399ed411cc72e6eb68b67e198db5e7b95381",
            positionStatus.GetProperty("exactCommit").GetString());
        var typedBscTrial = root.GetProperty("p109VLiveBscAuthorityTrial");
        Assert.Equal(positionStatus.GetProperty("exactCommit").GetString(),
            typedBscTrial.GetProperty("baseEngineCommit").GetString());
        Assert.Equal("036e2c00d83a0815b6160147b14ea05975057965",
            typedBscTrial.GetProperty("exactCommit").GetString());
        var typedStatusTrial = root.GetProperty("m217KTypedStatusReportTrial");
        Assert.Equal(typedBscTrial.GetProperty("exactCommit").GetString(),
            typedStatusTrial.GetProperty("previousConsumerPin").GetString());
        Assert.Equal("51c2eefd61d6bcbe1116fda6b781b74bb9cf06da",
            typedStatusTrial.GetProperty("exactCommit").GetString());
        var identityTrial = root.GetProperty("p0AIdentityTrial");
        Assert.Equal(typedStatusTrial.GetProperty("exactCommit").GetString(),
            identityTrial.GetProperty("baseEngineCommit").GetString());
        var rcbTrial = root.GetProperty("p0BRcbPlannerTrial");
        Assert.Equal(identityTrial.GetProperty("exactCommit").GetString(),
            rcbTrial.GetProperty("baseEngineCommit").GetString());
        var typedReadTrial = root.GetProperty("p0CTypedReadTrial");
        Assert.Equal(rcbTrial.GetProperty("exactCommit").GetString(),
            typedReadTrial.GetProperty("baseEngineCommit").GetString());
        var controlTrial = root.GetProperty("controlReportIsolationTrial");
        Assert.Equal(typedReadTrial.GetProperty("exactCommit").GetString(),
            controlTrial.GetProperty("baseEngineCommit").GetString());
        Assert.Equal("c85d4e20db722bd0c2729f2533b323b13d3ef735",
            controlTrial.GetProperty("exactCommit").GetString());
        // The current code-only stage observation trial must be descended
        // EXACTLY from historical #183. Physical acceptance does not transfer.
        var stageTrial = root.GetProperty("p0CStageObservationTrial");
        Assert.Equal(controlTrial.GetProperty("exactCommit").GetString(),
            stageTrial.GetProperty("baseEngineCommit").GetString());
        Assert.Equal("9c7cb40364023a7b217e5ec31ab3105ba90142eb",
            stageTrial.GetProperty("exactCommit").GetString());
        // The immutable #184 SBOw trial is now the parent of an exact
        // code-only read-only discovery trial; do not relabel #184 as #186.
        var windowTrial = root.GetProperty("p0SiebcuDiscoveryWindowTrial");
        Assert.Equal(stageTrial.GetProperty("exactCommit").GetString(),
            windowTrial.GetProperty("baseEngineCommit").GetString());
        Assert.Equal("d095b3ac9bdf6687f7ab9407f7e28eca9c168c94",
            windowTrial.GetProperty("exactCommit").GetString());
        Assert.Equal(windowTrial.GetProperty("exactCommit").GetString(),
            root.GetProperty("commit").GetString());
        Assert.Equal(186, root.GetProperty("sourcePullRequest").GetInt32());
        Assert.Equal(186, windowTrial.GetProperty("enginePullRequest").GetInt32());
        Assert.Equal("engine-ci-success-consumer-ci-pending-not-physical",
            windowTrial.GetProperty("status").GetString());
        Assert.True(windowTrial.GetProperty("readOnlyRcbEnrichment").GetBoolean());
        Assert.True(windowTrial.GetProperty("negotiatedWindowRequired").GetBoolean());
        Assert.True(windowTrial.GetProperty("perRcbSequentialFallbackRequired").GetBoolean());
        Assert.True(windowTrial.GetProperty("noControlOrRcbWrites").GetBoolean());
        Assert.True(windowTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(windowTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.Equal(184, stageTrial.GetProperty("enginePullRequest").GetInt32());
        Assert.Equal(185, stageTrial.GetProperty("engineCiOnlyPullRequest").GetInt32());
        Assert.Equal(1182, stageTrial.GetProperty("engineTestsPassed").GetInt32());
        Assert.True(stageTrial.GetProperty("sbo400msSafetyGatePreserved").GetBoolean());
        Assert.True(stageTrial.GetProperty("negativeAndDelayedLastApplErrorRegression").GetBoolean());
        Assert.True(stageTrial.GetProperty("noUnqualifiedImmediateOperate").GetBoolean());
        Assert.True(stageTrial.GetProperty("physicalLatencyQualificationRequired").GetBoolean());
        Assert.True(stageTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.True(controlTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(controlTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.True(controlTrial.GetProperty("controlChecksUnchanged").GetBoolean());
        Assert.True(controlTrial.GetProperty("controlTimeoutsUnchanged").GetBoolean());
        Assert.True(typedReadTrial.GetProperty("strictInvokeId").GetBoolean());
        Assert.True(typedReadTrial.GetProperty("strictAccessOrdinal").GetBoolean());
        Assert.True(typedReadTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(typedReadTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.True(rcbTrial.GetProperty("writeFreeReconciliation").GetBoolean());
        Assert.True(rcbTrial.GetProperty("boundedContinuationRequired").GetBoolean());
        Assert.True(rcbTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(rcbTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.True(identityTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(identityTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.Equal(618, typedStatusTrial.GetProperty("consumerPullRequest").GetInt32());
        Assert.True(typedStatusTrial.GetProperty("strictFcAndTupleEvidenceRequired").GetBoolean());
        Assert.True(typedStatusTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(typedStatusTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.Equal("engine-ci-success-consumer-ci-pending-not-physical",
            typedBscTrial.GetProperty("status").GetString());
        Assert.Equal(38328335496L, typedBscTrial.GetProperty("engineCiRun").GetInt64());
        Assert.True(typedBscTrial.GetProperty("staticSpcNeverOverridesExactLiveBsc").GetBoolean());
        Assert.True(typedBscTrial.GetProperty("typedTwoBitCtlValAndExactStepStatusRequired").GetBoolean());
        Assert.True(typedBscTrial.GetProperty("wrongOrCrossObjectStatusFailsClosed").GetBoolean());
        Assert.True(typedBscTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(typedBscTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.True(typedBscTrial.GetProperty("cyclicMmsProcessPollingForbidden").GetBoolean());
        Assert.True(positionStatus.GetProperty("standardPositionOnlyProbe").GetBoolean());
        Assert.True(positionStatus.GetProperty("operateRequiresExplicitConfirmation").GetBoolean());
        Assert.True(position.GetProperty("structuredStatusRequiresTypedMmsRead").GetBoolean());
        Assert.True(simpleEditor.GetProperty("simpleSelectWriteUi").GetBoolean());
        Assert.True(commissioning.GetProperty("singleWriteNoRetry").GetBoolean());
        Assert.True(commissioning.GetProperty("commandOperateForbidden").GetBoolean());
        Assert.Equal("code-trial-not-physical", recovery.GetProperty("status").GetString());
        Assert.True(recovery.GetProperty("noControlWriteOnProbe").GetBoolean());
        Assert.Equal("code-verified-not-physical", codeTrial.GetProperty("status").GetString());
        Assert.Equal("code-verified-not-physical", subscriberTrial.GetProperty("status").GetString());
        Assert.True(subscriberTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(subscriberTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.Equal("code-verified-not-physical", canonicalTrial.GetProperty("status").GetString());
        Assert.Equal(166, canonicalTrial.GetProperty("enginePullRequest").GetInt32());
        Assert.Equal(536, canonicalTrial.GetProperty("issue").GetInt32());
        Assert.Equal(38214165576L, canonicalTrial.GetProperty("engineCiRun").GetInt64());
        Assert.True(canonicalTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(canonicalTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.True(canonicalTrial.GetProperty("fullDiscoveryFallbackForbidden").GetBoolean());
        Assert.True(canonicalTrial.GetProperty("cyclicMmsProcessPollingForbidden").GetBoolean());
        Assert.True(codeTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(codeTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.Equal("code-verified-not-physical", zivTrial.GetProperty("status").GetString());
        Assert.Equal(538, zivTrial.GetProperty("issue").GetInt32());
        Assert.Equal(167, zivTrial.GetProperty("enginePullRequest").GetInt32());
        Assert.Equal(38219429896L, zivTrial.GetProperty("engineCiRun").GetInt64());
        Assert.True(zivTrial.GetProperty("sclApTitlePackedPrefixOnly").GetBoolean());
        Assert.True(zivTrial.GetProperty("bscLiveSemanticGateRequired").GetBoolean());
        Assert.True(zivTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(zivTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.True(zivTrial.GetProperty("fullDiscoveryFallbackForbidden").GetBoolean());
        Assert.True(zivTrial.GetProperty("cyclicMmsProcessPollingForbidden").GetBoolean());
        // Historical C264 remains PR #173; current P0-A identity trial is #177.
        Assert.Equal(173, typedStatusTrial.GetProperty("sourcePullRequest").GetInt32());
        Assert.Equal(177, root.GetProperty("p0AIdentityTrial").GetProperty("enginePullRequest").GetInt32());
        Assert.Equal(169, typedBscTrial.GetProperty("enginePullRequest").GetInt32());
        Assert.Equal(540, genericTrial.GetProperty("issue").GetInt32());
        Assert.Equal(38231327892L, genericTrial.GetProperty("engineCiRun").GetInt64());
        Assert.True(genericTrial.GetProperty("typedSemanticGateRequired").GetBoolean());
        Assert.True(genericTrial.GetProperty("associationScopedInventoryRequired").GetBoolean());
        Assert.True(genericTrial.GetProperty("vendorDispatchForbidden").GetBoolean());
        Assert.True(genericTrial.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(genericTrial.GetProperty("mainlinePromotionForbidden").GetBoolean());
        Assert.Equal("code-trial-not-physical",p10.GetProperty("status").GetString());
        Assert.True(p10.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(p10.GetProperty("cyclicMmsProcessPollingForbidden").GetBoolean());
        Assert.True(closure.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(closure.GetProperty("cyclicMmsProcessPollingForbidden").GetBoolean());
        Assert.True(serverAt.GetProperty("physicalQualificationRequired").GetBoolean());
        Assert.True(serverAt.GetProperty("noAutomaticMmsFailover").GetBoolean());

        Assert.Equal(143, association.GetProperty("sourcePullRequest").GetInt32());
        Assert.Equal(
            "84e9820e5a32690475960e49d5ef74e6637847fd",
            association.GetProperty("testedEngineCommit").GetString());
        Assert.Equal(
            "e5deed1d8aa11d97991695c6e390baafea7ab797",
            association.GetProperty("mergedEngineCommit").GetString());
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
