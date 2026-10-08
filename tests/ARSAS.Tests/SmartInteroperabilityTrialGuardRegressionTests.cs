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
        Assert.Equal(root.GetProperty("commit").GetString(),
            serverAt.GetProperty("exactCommit").GetString());
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
