using System.Text.Json;

namespace ARSAS.Tests;

public sealed class HybridReportAssociationCapabilityP3RegressionTests
{
    [Fact]
    public void HybridPlanning_UsesAssociationCapabilityForInitialPlanAndFreshRevalidation()
    {
        var source = Read("Services/NativeIec61850Client.HybridReporting.cs");
        var guarded = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");

        Assert.True(Count(source, "BuildCapabilityPlanWithGuardedRuntime(") >= 2);
        Assert.Contains("ArMms.MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(", guarded, StringComparison.Ordinal);
        Assert.Contains("ArMms.MmsGuardedDynamicReportRuntimePlanner.Build(", guarded, StringComparison.Ordinal);
        Assert.True(Count(source, "_session.LastNegotiatedCapabilities") >= 2);
        Assert.Contains("var enginePlan = capabilityAwarePlan.AcquisitionPlan;", source, StringComparison.Ordinal);
        Assert.Contains("var associationCapability = capabilityAwarePlan.AssociationCapability;", source, StringComparison.Ordinal);
        Assert.Contains("Summary = $\"{enginePlan.Summary} {associationCapability.Summary}\"", source, StringComparison.Ordinal);
        Assert.Contains("var revalidatedPlan = revalidatedCapabilityAwarePlan.AcquisitionPlan;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ArMms.MmsHybridReportAcquisitionPlanner.Build(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ArMms.MmsHybridReportAcquisitionPlanner.Build(", guarded, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineLock_PreservesP62BStabilityHistoryAcrossLaterReviewedEnginePins()
    {
        var source = Read("engines/ARIEC61850.lock.json");
        using var document = JsonDocument.Parse(source);
        var root = document.RootElement;

        Assert.Equal("masarray/ARIEC61850", root.GetProperty("repository").GetString());
        Assert.Equal("main", root.GetProperty("ref").GetString());
        Assert.Matches("^[0-9a-f]{40}$", root.GetProperty("commit").GetString() ?? string.Empty);
        Assert.True(root.GetProperty("sourcePullRequest").GetInt32() >= 89);
        Assert.Contains("PR #87", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("baseline-safe static precedence", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #88", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DefineNamedVariableList", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GetNamedVariableListAttributes", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeleteNamedVariableList", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single-member", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cleanup evidence", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #89", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quarantines automatic full dynamic DataSet activation", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("successful one-member NVL probation does not guarantee association survival", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("instMag/mag", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("instCVal/cVal", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ambiguous structures remain raw", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #100", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact proven dynamic RCB/member envelope", source, StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

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

        throw new FileNotFoundException(relativePath);
    }
}
