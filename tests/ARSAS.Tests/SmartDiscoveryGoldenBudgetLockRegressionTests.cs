using System.Text.Json;

namespace ARSAS.Tests;

public sealed class SmartDiscoveryGoldenBudgetLockRegressionTests
{
    private const string P05bEngineCommit = "4467124775d8d9d76f3db194f9fbfd97144767a8";

    [Fact]
    public void P05e_TargetLocksSameIedSemanticAuthorityWithoutInventingWireBudget()
    {
        var path = FindRepoFile("evidence/smart-discovery-golden-target.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        Assert.Equal("P0-5e", root.GetProperty("Phase").GetString());
        Assert.Equal("awaiting-fresh-physical-budget-lock", root.GetProperty("Status").GetString());
        Assert.Equal("AA1E1F06R4", root.GetProperty("DeviceIdentity").GetString());
        Assert.Equal(P05bEngineCommit, root.GetProperty("EngineCommit").GetString());

        var target = root.GetProperty("SemanticTarget");
        Assert.Equal(32, target.GetProperty("LogicalDevices").GetInt32());
        Assert.Equal(119, target.GetProperty("LogicalNodes").GetInt32());
        Assert.Equal(4925, target.GetProperty("SemanticLeaves").GetInt32());
        Assert.Equal(2, target.GetProperty("DataSets").GetInt32());
        Assert.Equal(58, target.GetProperty("OrderedFcdaMembers").GetInt32());
        Assert.Equal(32, target.GetProperty("LogicalReportControls").GetInt32());
        Assert.Equal(34, target.GetProperty("RuntimeReportControlInstances").GetInt32());
        Assert.Equal(2, target.GetProperty("IndexedBufferedFamilyMax").GetInt32());
        Assert.Equal(2, target.GetProperty("IndexedUnbufferedFamilyMax").GetInt32());
        Assert.Equal(0, target.GetProperty("SyntheticReportControlInstancesAllowed").GetInt32());

        var budgetAuthority = root.GetProperty("BudgetAuthority");
        Assert.Equal(JsonValueKind.Null, budgetAuthority.GetProperty("BudgetValues").ValueKind);
        Assert.Equal("P0-5d", budgetAuthority.GetProperty("RequiredProofPhase").GetString());
        Assert.Equal("PASS", budgetAuthority.GetProperty("RequiredProofVerdict").GetString());
        Assert.True(budgetAuthority.GetProperty("RequireRawCaptureSha256").GetBoolean());
    }

    [Fact]
    public void P05e_LockWriterDerivesBudgetFromPassProofAndHashesRawCapture()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/new-smart-discovery-golden-lock.ps1"));

        Assert.Contains("P0-5d", source, StringComparison.Ordinal);
        Assert.Contains("$proof.Verdict -ne 'PASS'", source, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -LiteralPath $capture -Algorithm SHA256", source, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -LiteralPath $proofPath -Algorithm SHA256", source, StringComparison.Ordinal);
        Assert.Contains("MaxConfirmedRequests = $confirmedRequests", source, StringComparison.Ordinal);
        Assert.Contains("MaxServiceRequests = $serviceBudget", source, StringComparison.Ordinal);
        Assert.Contains("ForbidUnexpectedServices = $true", source, StringComparison.Ordinal);
        Assert.Contains("ForbidSecondGetNameListSweep = $true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxConfirmedRequests = 100", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P05e_AcceptanceRejectsBudgetGrowthUnexpectedServicesAndIdentityMismatch()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/verify-smart-discovery-golden-lock.ps1"));

        Assert.Contains("Confirmed request budget exceeded", source, StringComparison.Ordinal);
        Assert.Contains("Service budget exceeded", source, StringComparison.Ordinal);
        Assert.Contains("Unexpected MMS service", source, StringComparison.Ordinal);
        Assert.Contains("Second GetNameList sweep is forbidden", source, StringComparison.Ordinal);
        Assert.Contains("Device identity mismatch", source, StringComparison.Ordinal);
        Assert.Contains("AllowDifferentEngineCommit", source, StringComparison.Ordinal);
        Assert.Contains("Candidate evidence must be a P0-5d PASS proof", source, StringComparison.Ordinal);
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
