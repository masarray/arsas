namespace ARSAS.Tests;

public sealed class SmartInteroperabilityTrialGuardRegressionTests
{
    [Fact]
    public void InteroperabilityGuard_SeparatesCodeTrialFromPhysicalAssociationAuthority()
    {
        var workflow = Read(".github/workflows/interoperability-reference-guard.yml");

        Assert.Contains("$lock.smartInteroperabilityCodeTrial", workflow, StringComparison.Ordinal);
        Assert.Contains("code-verified-not-physical", workflow, StringComparison.Ordinal);
        Assert.Contains("$trial.exactCommit -ne $lock.commit", workflow, StringComparison.Ordinal);
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
            "CodeVerified smart-interoperability trial cannot pass mainline readiness before fresh physical qualification",
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
        Assert.Equal(root.GetProperty("commit").GetString(), trial.GetProperty("exactCommit").GetString());

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
