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
        Assert.Contains("SCL Interoperability R7 Build", buildTargets, StringComparison.Ordinal);
        Assert.Contains("EnableSmartDiscoveryCaptureRoute", buildTargets, StringComparison.Ordinal);
    }


    [Fact]
    public void EnginePin_MatchesPhysicalSclRepairHead()
    {
        var lockFile = File.ReadAllText(FindRepoFile("engines/ARIEC61850.lock.json"));

        Assert.Contains(
            "\"commit\": \"4900427cb1710b433fb74d3ad99099b28ab27ef7\"",
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
