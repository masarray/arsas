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
        Assert.Contains("LiveIedCanonicalModelBuilder.Build", canonical, StringComparison.Ordinal);
        Assert.Contains("PublishCanonicalModel(model);", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ClearCanonicalModel();", lifecycle, StringComparison.Ordinal);
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
    }

    [Fact]
    public void EnginePin_MatchesValidatedCanonicalInteroperabilityHead()
    {
        var lockFile = File.ReadAllText(FindRepoFile("engines/ARIEC61850.lock.json"));

        Assert.Contains(
            "\"commit\": \"30c8820cf922028d3d13b6f5a355df1ff60ea455\"",
            lockFile,
            StringComparison.Ordinal);
        Assert.Contains("round-trip association validation", lockFile, StringComparison.Ordinal);
        Assert.Contains("Production promotion remains fail-closed", lockFile, StringComparison.Ordinal);
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
