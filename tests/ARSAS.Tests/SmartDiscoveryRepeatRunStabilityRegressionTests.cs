using System.Text.Json;

namespace ARSAS.Tests;

public sealed class SmartDiscoveryRepeatRunStabilityRegressionTests
{
    private const string EngineCommit = "4467124775d8d9d76f3db194f9fbfd97144767a8";

    [Fact]
    public void P05f_TargetRequiresThreeFreshIndependentAssociations()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("evidence/smart-discovery-repeat-run-target.json")));
        var root = document.RootElement;

        Assert.Equal("P0-5f", root.GetProperty("Phase").GetString());
        Assert.Equal("AA1E1F06R4", root.GetProperty("DeviceIdentity").GetString());
        Assert.Equal(EngineCommit, root.GetProperty("EngineCommit").GetString());
        Assert.True(root.GetProperty("MinimumIndependentAssociations").GetInt32() >= 3);

        var contract = root.GetProperty("RepeatRunContract");
        Assert.True(contract.GetProperty("RequireFreshAssociationGenerationPerRun").GetBoolean());
        Assert.True(contract.GetProperty("RequireExactConfirmedRequestCountAcrossRuns").GetBoolean());
        Assert.True(contract.GetProperty("RequireExactServiceMixAcrossRuns").GetBoolean());
        Assert.True(contract.GetProperty("RequireExactEngineKpiSignatureAcrossRuns").GetBoolean());
        Assert.True(contract.GetProperty("RequireExactDirectoryModelSignatureAcrossRuns").GetBoolean());
        Assert.True(contract.GetProperty("RequireExactProjectionSignatureAcrossRuns").GetBoolean());
        Assert.True(contract.GetProperty("RequireExactTypeProbeBudgetAcrossRuns").GetBoolean());
    }

    [Fact]
    public void P05f_RuntimeEvidenceIsFreshAssociationOnlyAndZeroTraffic()
    {
        var capture = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SmartDiscoveryCapture.cs"));
        var evidence = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SmartDiscoveryRepeatRunEvidence.cs"));

        Assert.Contains("cached branch", capture, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TryWriteSmartDiscoveryRepeatRunEvidence", capture, StringComparison.Ordinal);
        Assert.Contains("after a fresh", capture, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RefreshSmartDiscoveryModelKpi", evidence, StringComparison.Ordinal);
        Assert.Contains("DirectoryModelSignature", evidence, StringComparison.Ordinal);
        Assert.Contains("ProjectionSignature", evidence, StringComparison.Ordinal);
        Assert.Contains("DeterministicSignature", evidence, StringComparison.Ordinal);
        Assert.Contains("AssociationGeneration", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("GetVariableAccessAttributesAsync", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("GetNameList", evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void P05f_FinalizerRejectsAssociationReuseAndCrossRunDrift()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/finalize-smart-discovery-repeat-run-stability.ps1"));

        Assert.Contains("Repeat set reuses an association generation", source, StringComparison.Ordinal);
        Assert.Contains("wire/engine request accounting mismatch", source, StringComparison.Ordinal);
        Assert.Contains("Confirmed-request drift", source, StringComparison.Ordinal);
        Assert.Contains("MMS service-mix drift", source, StringComparison.Ordinal);
        Assert.Contains("Engine KPI deterministic-signature drift", source, StringComparison.Ordinal);
        Assert.Contains("Directory model signature drift", source, StringComparison.Ordinal);
        Assert.Contains("ARSAS signal projection signature drift", source, StringComparison.Ordinal);
        Assert.Contains("Hierarchy type-probe budget drift", source, StringComparison.Ordinal);
        Assert.Contains("Discovered model-count drift", source, StringComparison.Ordinal);
        Assert.Contains("RepeatTargetSha256", source, StringComparison.Ordinal);
        Assert.Contains("SchemaVersion = 2", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P05f_RunBundleReverifiesGoldenBudgetAndRawCaptureByDefault()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/new-smart-discovery-repeat-run-bundle.ps1"));

        Assert.Contains("verify-smart-discovery-golden-lock.ps1", source, StringComparison.Ordinal);
        Assert.Contains("verify-smart-discovery-pcap.ps1", source, StringComparison.Ordinal);
        Assert.Contains("Production repeat evidence requires raw .pcap/.pcapng input", source, StringComparison.Ordinal);
        Assert.Contains("Wire/engine request accounting mismatch", source, StringComparison.Ordinal);
        Assert.Contains("GoldenLockSha256", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeEvidenceSha256", source, StringComparison.Ordinal);
        Assert.Contains("FixtureEvidence", source, StringComparison.Ordinal);
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

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
