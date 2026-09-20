namespace ARSAS.Tests;

public sealed class CanonicalLiveSclExportRegressionTests
{
    [Fact]
    public void NativeClient_RetainsAcceptedAssociationCanonicalSnapshot()
    {
        var canonical = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.CanonicalModel.cs"));
        var lifecycle = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SmartDiscoveryLifecycle.cs"));

        Assert.Contains("public LiveIedCanonicalModel? LastCanonicalModel", canonical, StringComparison.Ordinal);
        Assert.Contains("_session.GetAcceptedCommunicationEvidence", canonical, StringComparison.Ordinal);
        Assert.Contains("LiveIedCanonicalModelBuilder.Build(model, communication, initialRead)", canonical, StringComparison.Ordinal);
        Assert.Contains("PublishCanonicalModel(model, initialRead);", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ClearCanonicalModel();", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void SmartDiscovery_DefersEagerFcValueReadsFromStructuralScan()
    {
        var capture = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SmartDiscoveryCapture.cs"));
        var lifecycle = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SmartDiscoveryLifecycle.cs"));

        Assert.Contains("SmartDiscoveryStructuralFreezeContract = \"P0-R9-STRUCTURAL\"", capture, StringComparison.Ordinal);
        Assert.Contains("CreateP0FrozenSmartDiscoveryOptions()", capture, StringComparison.Ordinal);
        Assert.Contains("MaxConcurrentChains = 8", capture, StringComparison.Ordinal);
        Assert.Contains("UnknownPeerMaxConcurrentChains = 4", capture, StringComparison.Ordinal);
        Assert.Contains("MaxNameListPages = 64", capture, StringComparison.Ordinal);
        Assert.Contains("ProbeReportAttributes = true", capture, StringComparison.Ordinal);
        Assert.Contains("MaxReportAttributeProbes = 64", capture, StringComparison.Ordinal);
        Assert.Contains("ReadDataSetDirectories = true", capture, StringComparison.Ordinal);
        Assert.Contains("MaxDataSetDirectoryReads = 64", capture, StringComparison.Ordinal);
        Assert.Contains("GetOrCreateSmartDiscoveryAssociationFlight(", capture, StringComparison.Ordinal);
        Assert.Contains("DiscoverSmartSingleFlightAsync(smartOptions, CancellationToken.None)", capture, StringComparison.Ordinal);
        Assert.Contains("ProbeSmartAsync(_session, discovery.IedDirectory, smartOptions, CancellationToken.None)", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("_session.DiscoverAsync(", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscoverDomainVariableNamesAsync", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("TryBuildSupplementalGetNameListSnapshotAsync", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscoverDomainVariableTypeTreeNamesAsync", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("AddAdaptiveLogicalNodeSiblingProbeSignalsAsync", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("EnrichEngineeringUnitsAsync", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialFcReadPlanner.FromSclModel", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteInitialFcReadPlanSmartAsync", capture, StringComparison.Ordinal);
        Assert.Contains("initialFcRoots=deferred", capture, StringComparison.Ordinal);
        Assert.Contains("ArMms.InitialFcReadExecutionResult? initialRead = null", capture, StringComparison.Ordinal);
        Assert.Contains("TryPublishSmartDiscoveryAuthority(", capture, StringComparison.Ordinal);
        Assert.Contains("ArMms.InitialFcReadExecutionResult? initialRead", lifecycle, StringComparison.Ordinal);
        Assert.Contains("PublishCanonicalModel(model, initialRead);", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceWorkspace_ReceivesCanonicalSnapshotFromSameClientSession()
    {
        var runtime = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        var model = File.ReadAllText(FindRepoFile("Models/MonitorModels.cs"));

        Assert.Contains("public LiveIedCanonicalModel? LiveCanonicalModel", model, StringComparison.Ordinal);
        Assert.Contains("device.LiveCanonicalModel = session.Client.LastCanonicalModel;", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveSave_UsesCanonicalExporterAndFailsClosedOnStaleOrMissingEvidence()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));
        var saveMethodStart = source.IndexOf(
            "private void SaveTypedModelAsScl(",
            StringComparison.Ordinal);
        Assert.True(saveMethodStart >= 0);

        var saveMethod = source[saveMethodStart..];
        var canonicalIndex = saveMethod.IndexOf(
            "CanonicalLiveIedSclExporter.WriteFiles",
            StringComparison.Ordinal);
        var staleGuardIndex = saveMethod.IndexOf(
            "The canonical association snapshot does not belong to the current live discovery model.",
            StringComparison.Ordinal);
        var missingGuardIndex = saveMethod.IndexOf(
            "The live discovery model is not bound to accepted MMS association evidence.",
            StringComparison.Ordinal);

        Assert.True(missingGuardIndex >= 0);
        Assert.True(staleGuardIndex >= 0);
        Assert.True(canonicalIndex > staleGuardIndex);
        Assert.Contains("if (device.SclWorkspace == null)", saveMethod, StringComparison.Ordinal);
        Assert.Contains("profile: \"full-model\"", saveMethod, StringComparison.Ordinal);
        Assert.Contains("canonical round-trip verified", saveMethod, StringComparison.Ordinal);
        Assert.Contains("instanceEvidence={canonicalExportEvidence.InstanceValues.Count}", saveMethod, StringComparison.Ordinal);
        Assert.Contains("runtimeRCB={canonicalExportEvidence.Discovery.ReportControls.Count}", saveMethod, StringComparison.Ordinal);
        Assert.Contains("logicalExportRCB={result.ReportControlCount}", saveMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveSave_UsesPureWorkspaceReloadValidatorBeforeSuccess()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));
        var validator = File.ReadAllText(FindRepoFile("Services/CanonicalSclReloadValidator.cs"));
        var saveMethodStart = source.IndexOf(
            "private void SaveTypedModelAsScl(",
            StringComparison.Ordinal);
        var successStart = source.IndexOf(
            "private void ShowSclSaveSuccess(",
            saveMethodStart,
            StringComparison.Ordinal);

        Assert.True(saveMethodStart >= 0 && successStart > saveMethodStart);
        var saveMethod = source[saveMethodStart..successStart];

        var exportIndex = saveMethod.IndexOf("CanonicalLiveIedSclExporter.WriteFiles", StringComparison.Ordinal);
        var reloadIndex = saveMethod.IndexOf("CanonicalSclReloadValidator.Validate", StringComparison.Ordinal);
        Assert.True(exportIndex >= 0 && reloadIndex > exportIndex);
        Assert.Contains("DeleteFailedCanonicalExportArtifacts(result)", saveMethod, StringComparison.Ordinal);
        Assert.Contains("reloadLD=", saveMethod, StringComparison.Ordinal);
        Assert.Contains("reloadLN=", saveMethod, StringComparison.Ordinal);
        Assert.Contains("reloadDataSet=", saveMethod, StringComparison.Ordinal);
        Assert.Contains("reloadRCB=", saveMethod, StringComparison.Ordinal);

        var cleanupStart = source.IndexOf(
            "private static void DeleteFailedCanonicalExportArtifacts(",
            StringComparison.Ordinal);
        Assert.True(cleanupStart >= 0);
        var cleanup = source[cleanupStart..successStart];
        Assert.Contains("result.SclPath", cleanup, StringComparison.Ordinal);
        Assert.Contains("result.ReportPath", cleanup, StringComparison.Ordinal);
        Assert.Contains("result.SummaryPath", cleanup, StringComparison.Ordinal);
        Assert.Contains("result.ExcludedAttributesPath", cleanup, StringComparison.Ordinal);
        Assert.Contains("File.Delete(path)", cleanup, StringComparison.Ordinal);

        Assert.Contains("workspaceService.Open(", validator, StringComparison.Ordinal);
        Assert.Contains("IedName = canonical.IedName", validator, StringComparison.Ordinal);
        Assert.Contains("AccessPointName = canonical.AccessPointName", validator, StringComparison.Ordinal);
        Assert.Contains("endpoint.Port != 102", validator, StringComparison.Ordinal);
        Assert.Contains("reloadCoverage.LogicalDeviceCount != result.LogicalDeviceCount", validator, StringComparison.Ordinal);
        Assert.Contains("reloadCoverage.LogicalNodeCount != result.LogicalNodeCount", validator, StringComparison.Ordinal);
        Assert.Contains("workspace.DataSets.Count != result.DataSetCount", validator, StringComparison.Ordinal);
        Assert.Contains("workspace.ReportControls.Count != result.ReportControlCount", validator, StringComparison.Ordinal);
        Assert.Contains("SclAssistedConnectionPreparationBuilder.Build(", validator, StringComparison.Ordinal);
        Assert.Contains("preparation.IsSuccess", validator, StringComparison.Ordinal);
        Assert.Contains("preparation.AssociationPlan", validator, StringComparison.Ordinal);
        Assert.Contains("Generated SCL cannot rebuild the ARSAS SCL-assisted reconnect plan", validator, StringComparison.Ordinal);
        Assert.Contains("canonicalAssociation.ApTitle", validator, StringComparison.Ordinal);
        Assert.Contains("canonicalAssociation.AeQualifier", validator, StringComparison.Ordinal);
        Assert.Contains("canonicalAssociation.PresentationSelector", validator, StringComparison.Ordinal);
        Assert.Contains("canonicalAssociation.SessionSelector", validator, StringComparison.Ordinal);
        Assert.Contains("canonicalAssociation.TransportSelector", validator, StringComparison.Ordinal);
        Assert.Contains("Generated SCL reconnect association drifted from accepted canonical wire evidence", validator, StringComparison.Ordinal);
        Assert.Contains("result.Profile", validator, StringComparison.Ordinal);
        Assert.Contains("full-model", validator, StringComparison.Ordinal);
        Assert.Contains("MmsLocalAssociationProfile.ExistingRuntimeDefault", validator, StringComparison.Ordinal);
        Assert.Contains("changed the proven calling-side runtime identity", validator, StringComparison.Ordinal);
        Assert.Contains("BuildAssociationProfiles()", validator, StringComparison.Ordinal);
        Assert.Contains("reproduce accepted association profile", validator, StringComparison.Ordinal);
    }

    [Fact]
    public void SclAssistedReconnect_UsesNativeCallingIdentityAndSafeParallelValueReads()
    {
        var preparation = File.ReadAllText(FindRepoFile("Services/SclAssistedConnectionPreparation.cs"));
        var client = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SclAssisted.cs"));

        Assert.Contains("MmsLocalAssociationProfile.ExistingRuntimeDefault", preparation, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsLocalAssociationProfile.SclInteroperabilityDefault", preparation, StringComparison.Ordinal);
        Assert.Contains("IsSafeTrustedSclInitialReadFc", client, StringComparison.Ordinal);
        Assert.Contains("ExecuteInitialFcReadPlanSmartAsync", client, StringComparison.Ordinal);
        Assert.Contains("\"ST\" or \"MX\" or \"SP\" or \"SV\" or \"CF\" or \"DC\" or \"EX\" or \"BL\" or \"OR\" or \"SR\"", client, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteInitialFcReadPlanAsync(", client, StringComparison.Ordinal);
    }

    [Fact]
    public void P0StructuralDiscoveryFreeze_LocksR9WireAndModelBudget()
    {
        var contract = File.ReadAllText(FindRepoFile("evidence/iedscout-convergence-target.json"));

        Assert.Contains("\"contractId\": \"P0-R9-STRUCTURAL\"", contract, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"implemented-and-r9-physically-proven\"", contract, StringComparison.Ordinal);
        Assert.Contains("\"associationScopedSingleFlightRequired\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"eagerInitialFcReadsForbidden\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"supplementalLegacyBrowseForbidden\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"maxConcurrentChains\": 8", contract, StringComparison.Ordinal);
        Assert.Contains("\"unknownPeerMaxConcurrentChains\": 4", contract, StringComparison.Ordinal);
        Assert.Contains("\"referenceConfirmedMmsRequests\": 323", contract, StringComparison.Ordinal);
        Assert.Contains("\"maximumConfirmedMmsRequests\": 417", contract, StringComparison.Ordinal);
        Assert.Contains("\"referenceGetVariableAccessAttributes\": 119", contract, StringComparison.Ordinal);
        Assert.Contains("\"maximumGetVariableAccessAttributes\": 119", contract, StringComparison.Ordinal);
        Assert.Contains("\"referenceReads\": 64", contract, StringComparison.Ordinal);
        Assert.Contains("\"maximumReads\": 156", contract, StringComparison.Ordinal);
        Assert.Contains("\"topLevelDataObjects\": 860", contract, StringComparison.Ordinal);
        Assert.Contains("\"dataObjectsIncludingSdo\": 906", contract, StringComparison.Ordinal);
        Assert.Contains("\"scalarLeaves\": 4925", contract, StringComparison.Ordinal);
        Assert.Contains("\"dataSets\": 2", contract, StringComparison.Ordinal);
        Assert.Contains("\"fcda\": 58", contract, StringComparison.Ordinal);
        Assert.Contains("\"logicalReportControls\": 32", contract, StringComparison.Ordinal);
        Assert.Contains("\"settingControls\": 1", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void P1ProjectionOrderRepair_LocksPhysicalRootCauseAndExactStrategy()
    {
        var contract = File.ReadAllText(FindRepoFile("evidence/iedscout-convergence-target.json"));

        Assert.Contains("\"contractId\": \"P1-CF-DO-SCOPED\"", contract, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"physically-proven-r10\"", contract, StringComparison.Ordinal);
        Assert.Contains("\"projectionErrors\": 46", contract, StringComparison.Ordinal);
        Assert.Contains("\"affectedFcRoots\": 18", contract, StringComparison.Ordinal);
        Assert.Contains("\"affectedSclLeaves\": 191", contract, StringComparison.Ordinal);
        Assert.Contains("\"functionalConstraint\": \"CF\"", contract, StringComparison.Ordinal);
        Assert.Contains("\"sclArrayCountAttributes\": 0", contract, StringComparison.Ordinal);
        Assert.Contains("\"projectionErrors\": 0", contract, StringComparison.Ordinal);
        Assert.Contains("\"noSilentCrossDoValueSwap\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"noAdditionalDiscoveryGva\": true", contract, StringComparison.Ordinal);
        Assert.Contains("LN$CF$DO", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void P2CaseSensitiveValuePipeline_LocksLosslessExactPathIdentity()
    {
        var contract = File.ReadAllText(FindRepoFile("evidence/iedscout-convergence-target.json"));
        var client = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SclAssisted.cs"));

        Assert.Contains("\"contractId\": \"P2-CASE-EXACT-VALUES\"", contract, StringComparison.Ordinal);
        Assert.Contains("\"observedCacheLoss\": 11", contract, StringComparison.Ordinal);
        Assert.Contains("\"edition2CacheLoss\": 0", contract, StringComparison.Ordinal);
        Assert.Contains("\"edition1CacheLoss\": 0", contract, StringComparison.Ordinal);
        Assert.Contains("\"ltrkLowercaseTAndUppercaseTRemainDistinct\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"noAdditionalDiscoveryGva\": true", contract, StringComparison.Ordinal);

        var cacheDeclaration = client.IndexOf("_trustedSclInitialValues =", StringComparison.Ordinal);
        Assert.True(cacheDeclaration >= 0);
        var cacheSegment = client.Substring(cacheDeclaration, Math.Min(260, client.Length - cacheDeclaration));
        Assert.Contains("StringComparer.Ordinal", cacheSegment, StringComparison.Ordinal);
        Assert.DoesNotContain("StringComparer.OrdinalIgnoreCase", cacheSegment, StringComparison.Ordinal);

        Assert.Contains("projectedInitialValueKeys", client, StringComparison.Ordinal);
        Assert.Contains("projectedUniqueValues", client, StringComparison.Ordinal);
        Assert.Contains("initialValueCacheLoss", client, StringComparison.Ordinal);
        Assert.Contains("cacheLoss={initialValueCacheLoss}", client, StringComparison.Ordinal);
        Assert.Contains("NormalizeTrustedSclReference", client, StringComparison.Ordinal);
    }

    [Fact]
    public void R9PhysicalReuse_LocksWorkingPathAndKeepsSemanticProjectionGapOpen()
    {
        var contract = File.ReadAllText(FindRepoFile("evidence/iedscout-convergence-target.json"));

        Assert.Contains("\"confirmedMmsRequests\": 323", contract, StringComparison.Ordinal);
        Assert.Contains("\"getVariableAccessAttributes\": 119", contract, StringComparison.Ordinal);
        Assert.Contains("\"topLevelDataObjects\": 860", contract, StringComparison.Ordinal);
        Assert.Contains("\"dataObjectsIncludingSdo\": 906", contract, StringComparison.Ordinal);
        Assert.Contains("\"scalarLeaves\": 4925", contract, StringComparison.Ordinal);

        Assert.Contains("\"fcRoots\": 563", contract, StringComparison.Ordinal);
        Assert.Contains("\"successfulReads\": 563", contract, StringComparison.Ordinal);
        Assert.Contains("\"fcRoots\": 562", contract, StringComparison.Ordinal);
        Assert.Contains("\"successfulReads\": 562", contract, StringComparison.Ordinal);
        Assert.Contains("\"reportBackedRuntimePoints\": 58", contract, StringComparison.Ordinal);
        Assert.Contains("\"unresolvedRuntimePoints\": 0", contract, StringComparison.Ordinal);
        Assert.Contains("\"actualInformationReportObserved\": true", contract, StringComparison.Ordinal);

        Assert.Contains("\"targetProjectionErrors\": 0", contract, StringComparison.Ordinal);
        Assert.Contains("\"observedEdition2\": 46", contract, StringComparison.Ordinal);
        Assert.Contains("\"observedEdition1\": 46", contract, StringComparison.Ordinal);
        Assert.Contains("\"observedProjectedMinusCached\": 11", contract, StringComparison.Ordinal);
        Assert.Contains("\"observedWithoutDataSet\": 30", contract, StringComparison.Ordinal);
        Assert.Contains("\"observedR9ExportValCount\": 0", contract, StringComparison.Ordinal);

        var client = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SclAssisted.cs"));
        Assert.Contains("projectionErrorSamples", client, StringComparison.Ordinal);
        Assert.Contains("SCL initial projection:", client, StringComparison.Ordinal);
        Assert.Contains("fullDiscovery=skipped", client, StringComparison.Ordinal);
    }

    [Fact]
    public void R10PhysicalReuse_ClosesP1P2AndLocksReportBackedRoundTrip()
    {
        var contract = File.ReadAllText(FindRepoFile("evidence/iedscout-convergence-target.json"));

        Assert.Contains("\"arsasR10\"", contract, StringComparison.Ordinal);
        Assert.Contains("\"initialTargets\": 709", contract, StringComparison.Ordinal);
        Assert.Contains("\"fcRootTargets\": 525", contract, StringComparison.Ordinal);
        Assert.Contains("\"doScopedTargets\": 184", contract, StringComparison.Ordinal);
        Assert.Contains("\"projectedUniqueValues\": 4441", contract, StringComparison.Ordinal);
        Assert.Contains("\"initialValueCache\": 4441", contract, StringComparison.Ordinal);
        Assert.Contains("\"initialTargets\": 708", contract, StringComparison.Ordinal);
        Assert.Contains("\"fcRootTargets\": 524", contract, StringComparison.Ordinal);
        Assert.Contains("\"projectedUniqueValues\": 4246", contract, StringComparison.Ordinal);
        Assert.Contains("\"initialValueCache\": 4246", contract, StringComparison.Ordinal);
        Assert.Contains("\"cacheLoss\": 0", contract, StringComparison.Ordinal);
        Assert.Contains("\"projectionErrors\": 0", contract, StringComparison.Ordinal);
        Assert.Contains("\"reportBackedRuntimePoints\": 58", contract, StringComparison.Ordinal);
        Assert.Contains("\"unresolvedRuntimePoints\": 0", contract, StringComparison.Ordinal);
        Assert.Contains("\"actualInformationReportObserved\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"cyclicMmsProcessPolling\": 0", contract, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"resolved-r10-physical\"", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void R7Workflow_BindsArtifactToExactSourceHeadAndReloadContracts()
    {
        var workflow = File.ReadAllText(
            FindRepoFile(".github/workflows/scl-interoperability-r7.yml"));

        Assert.Contains(
            "ARSAS_SOURCE_SHA: ${{ github.event.pull_request.head.sha || github.sha }}",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "git -C .\\ARSAS checkout --quiet --detach $env:ARSAS_SOURCE_SHA",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "if ($arsasCommit -ne $env:ARSAS_SOURCE_SHA)",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "git clone --quiet --depth 1 --branch $ref",
            workflow,
            StringComparison.Ordinal);

        Assert.Contains(
            "Services/CanonicalSclReloadValidator.cs",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "tests/ARSAS.Tests/CanonicalSclReloadValidatorTests.cs",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "CanonicalSclReloadValidator\\.Validate",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "ExportedCanonicalScl_ReopensThroughArsasWorkspaceWithoutStructuralDrift",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "GoldenRcbShape_RoundTripsThirtyFourRuntimeAsThirtyTwoLogicalWithPhysicalCapacity",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "34 runtime -> 32 logical with ConfReportControl max=34",
            workflow,
            StringComparison.Ordinal);

        var buildTargets = File.ReadAllText(FindRepoFile("Directory.Build.targets"));
        Assert.Contains("VerifyPhysicalProvenSmartDiscoveryRoute", buildTargets, StringComparison.Ordinal);
        Assert.Contains("-VerifyOnly", buildTargets, StringComparison.Ordinal);
        Assert.DoesNotContain("GITHUB_WORKFLOW", buildTargets, StringComparison.Ordinal);
        Assert.DoesNotContain("SmartDiscoveryProductionPromoted", buildTargets, StringComparison.Ordinal);
        Assert.Contains("evidence/iedscout-convergence-target.json", workflow, StringComparison.Ordinal);
        Assert.Contains("IEDScout convergence source contract regressed", workflow, StringComparison.Ordinal);
        Assert.Contains("TryResolveStandardSubDataObjectCdc", workflow, StringComparison.Ordinal);
        Assert.Contains("IsEdition2ServiceTrackingCdc", workflow, StringComparison.Ordinal);
        Assert.Contains("TryBuildSupplementalGetNameListSnapshotAsync", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void IedScoutConvergenceContract_PhysicalRetestPassedAndMergeReady()
    {
        var contract = File.ReadAllText(FindRepoFile("evidence/iedscout-convergence-target.json"));
        var documentation = File.ReadAllText(FindRepoFile("docs/IEDSCOUT_CONVERGENCE.md"));

        Assert.Contains("\"status\": \"physical-retest-passed-merge-ready\"", contract, StringComparison.Ordinal);
        Assert.Contains("\"pullRequest\": 134", contract, StringComparison.Ordinal);
        Assert.Contains("\"pullRequest\": 135", contract, StringComparison.Ordinal);
        Assert.Contains("\"pullRequest\": 324", contract, StringComparison.Ordinal);
        Assert.Contains("648124097621046f5f127ceb1cf853fea54db730", contract, StringComparison.Ordinal);
        Assert.Contains("\"exactlyOneAssociation\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"supplementalLegacyAssociationForbidden\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"recursivePerLeafGvaStormForbidden\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"reopenInArsasRequired\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"physicalReconnectRequired\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"productionPromoted\": false", contract, StringComparison.Ordinal);
        Assert.Contains("\"mergeAllowedBeforePhysicalRetest\": false", contract, StringComparison.Ordinal);
        Assert.Contains("\"mergeAllowedAfterPhysicalRetest\": true", contract, StringComparison.Ordinal);
        Assert.Contains("\"physicalRetestRequired\": false", contract, StringComparison.Ordinal);
        Assert.Contains("\"physicalRetestPassed\": true", contract, StringComparison.Ordinal);
        Assert.Contains("Merged proven baseline", documentation, StringComparison.Ordinal);
        Assert.Contains("The field result, not test count alone", documentation, StringComparison.Ordinal);

        var guard = File.ReadAllText(FindRepoFile(".github/workflows/iedscout-convergence-guard.yml"));
        Assert.Contains("name: IEDScout Convergence Guard", guard, StringComparison.Ordinal);
        Assert.Contains("name: iedscout-convergence-contract", guard, StringComparison.Ordinal);
        Assert.Contains("Merged engine authority must remain PR #134 + PR #135 with ARSAS #324 provenance", guard, StringComparison.Ordinal);
        Assert.Contains("P0 structural discovery freeze regressed away from the accepted IEDScout convergence path", guard, StringComparison.Ordinal);
        Assert.Contains("Canonical IEC model / SCL semantic authority regressed", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceClean_GuardsApprovedFirstPartyConvergenceAuthorities()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/verify-source-clean.ps1"));

        Assert.Contains("$ApprovedConvergenceIdentifierPaths", source, StringComparison.Ordinal);
        Assert.Contains("docs/IEDSCOUT_CONVERGENCE.md", source, StringComparison.Ordinal);
        Assert.Contains("evidence/iedscout-convergence-target.json", source, StringComparison.Ordinal);
        Assert.Contains("CanonicalLiveSclExportRegressionTests.cs", source, StringComparison.Ordinal);
        Assert.Contains("identifierScanExempt", source, StringComparison.Ordinal);
    }


    [Fact]
    public void EnginePin_MatchesPhysicalSclRepairHead()
    {
        var lockFile = File.ReadAllText(FindRepoFile("engines/ARIEC61850.lock.json"));

        Assert.Contains(
            "\"commit\": \"648124097621046f5f127ceb1cf853fea54db730\"",
            lockFile,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"physicalTestedCommit\": \"9935d6902d786cc69b299260fe36b835944d5e81\"",
            lockFile,
            StringComparison.Ordinal);
        Assert.Contains("\"sourcePullRequest\": 135", lockFile, StringComparison.Ordinal);
        Assert.Contains("exact association request bytes accepted by the IED", lockFile, StringComparison.Ordinal);
        Assert.Contains("accepted COTP destination selector", lockFile, StringComparison.Ordinal);
        Assert.Contains("runtime-mutable", lockFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DataSet/ConfRev/domain/LN/buffered identity", lockFile, StringComparison.Ordinal);
        Assert.Contains("full-model SCL", lockFile, StringComparison.Ordinal);
        Assert.Contains("CDC-aware WYE/DEL/SEQ SDO", lockFile, StringComparison.Ordinal);
        Assert.Contains("FC ownership", lockFile, StringComparison.Ordinal);
        Assert.Contains("TCTR/TVTR/LTIM/EEName/MltLev", lockFile, StringComparison.Ordinal);
        Assert.Contains("LTRK service-tracking", lockFile, StringComparison.Ordinal);
        Assert.Contains("Edition-1 schema downgrade protection", lockFile, StringComparison.Ordinal);
        Assert.Contains("TypeSpecification declaration order", lockFile, StringComparison.Ordinal);
        Assert.Contains("SG/SE as setting data", lockFile, StringComparison.Ordinal);
        Assert.Contains("MHAI THD phase groups as WYE/CMV", lockFile, StringComparison.Ordinal);
        Assert.Contains("bounded FC-read policy", lockFile, StringComparison.Ordinal);
        Assert.Contains("complete PR #134 smart-discovery performance head", lockFile, StringComparison.Ordinal);
        Assert.Contains("Production promotion remains fail-closed", lockFile, StringComparison.Ordinal);
        Assert.Contains(
            "\"commit\": \"4467124775d8d9d76f3db194f9fbfd97144767a8\"",
            lockFile,
            StringComparison.Ordinal);
    }


    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
