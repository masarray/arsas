using System.Text.Json;

namespace ARSAS.Tests;

public sealed class SmartDiscoveryAssociationSingleFlightRegressionTests
{
    private const string P05bEngineCommit = "4467124775d8d9d76f3db194f9fbfd97144767a8";

    [Fact]
    public void P05c_CompleteEnrichmentChain_IsAssociationScopedSingleFlight()
    {
        var capture = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SmartDiscoveryCapture.cs"));
        var lifecycle = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SmartDiscoveryLifecycle.cs"));

        Assert.Contains("GetOrCreateSmartDiscoveryAssociationFlight", capture, StringComparison.Ordinal);
        Assert.Contains("RunSmartDiscoveryAssociationFlightAsync", capture, StringComparison.Ordinal);
        Assert.Contains("flight.WaitAsync(cancellationToken)", capture, StringComparison.Ordinal);
        Assert.Contains("_mmsIoGate.WaitAsync(CancellationToken.None)", capture, StringComparison.Ordinal);
        Assert.Contains("DiscoverSmartSingleFlightAsync(smartOptions, CancellationToken.None)", capture, StringComparison.Ordinal);
        Assert.Contains("ProbeSmartAsync(_session, discovery.IedDirectory, smartOptions, CancellationToken.None)", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("ProbeSmartAsync(_session, discovery.IedDirectory, smartOptions, cancellationToken)", capture, StringComparison.Ordinal);

        Assert.Contains("_smartDiscoveryAssociationFlight", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_smartDiscoveryFlightGeneration", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ownerFactory(generation)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(_smartDiscoveryAssociationFlight, flight)", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void P05c_ReconnectAndDispose_InvalidateGenerationAndBlockStalePublish()
    {
        var lifecycle = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SmartDiscoveryLifecycle.cs"));
        var capture = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SmartDiscoveryCapture.cs"));
        var production = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.cs"));

        Assert.Contains("_smartDiscoveryAssociationGeneration++", lifecycle, StringComparison.Ordinal);
        Assert.Contains("generation != _smartDiscoveryAssociationGeneration", lifecycle, StringComparison.Ordinal);
        Assert.Contains("TryPublishSmartDiscoveryAuthority", lifecycle, StringComparison.Ordinal);
        Assert.Contains("IsCurrentSmartDiscoveryAssociationGeneration", capture, StringComparison.Ordinal);
        Assert.Contains("ResetSmartDiscoveryAuthority(); // __P0_5C_CONNECT_RESET__", production, StringComparison.Ordinal);
        Assert.Contains("ResetSmartDiscoveryAuthority(); // __P0_5C_DISPOSE_RESET__", production, StringComparison.Ordinal);
        Assert.Contains("return await DiscoverSignalsSmartForCaptureAsync(cancellationToken, progress).ConfigureAwait(false);", production, StringComparison.Ordinal);
    }

    [Fact]
    public void P05c_PhysicalBaseline_RemainsExactP05bBudgetConvergenceCommit()
    {
        using var lockDocument = JsonDocument.Parse(
            File.ReadAllText(FindRepoFile("engines/ARIEC61850.lock.json")));
        using var targetDocument = JsonDocument.Parse(
            File.ReadAllText(FindRepoFile("evidence/smart-discovery-golden-target.json")));
        var workflow = File.ReadAllText(
            FindRepoFile(".github/workflows/smart-discovery-capture-build.yml"));

        var physicalTargetCommit = targetDocument.RootElement
            .GetProperty("EngineCommit")
            .GetString();
        var previousTrialPin = lockDocument.RootElement
            .GetProperty("previousTrialPin")
            .GetProperty("commit")
            .GetString();
        var currentIntegrationCommit = lockDocument.RootElement
            .GetProperty("commit")
            .GetString();

        Assert.Equal(P05bEngineCommit, physicalTargetCommit);
        Assert.Equal(P05bEngineCommit, previousTrialPin);
        Assert.NotEqual(P05bEngineCommit, currentIntegrationCommit);
        Assert.Contains(
            "$baselineEngineCommit = $lock.previousTrialPin.commit",
            workflow,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            $"if ($baselineEngineCommit -ne '{P05bEngineCommit}')",
            workflow,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ARIEC61850_COMMIT=$integrationEngineCommit", workflow, StringComparison.Ordinal);
        Assert.Contains("ARIEC61850_BASELINE_COMMIT=$baselineEngineCommit", workflow, StringComparison.Ordinal);
        Assert.Contains("Unexpected engine commit", workflow, StringComparison.Ordinal);
        Assert.Contains("LastSmartTypeProbeBudget", workflow, StringComparison.Ordinal);
        Assert.Contains("SuppressedExactRepeatRequests", workflow, StringComparison.Ordinal);
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
