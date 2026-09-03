namespace ARSAS.Tests;

public sealed class G27NativeCoverageBudgetRegressionTests
{
    [Fact]
    public void P17_NativeRuntime_UsesEnvelopeBoundEnginePlannerAndAssociationBoundPlanBudget()
    {
        var source = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");

        Assert.Contains("NativeFieldCapabilityAbsoluteDynamicPlanLimit = 64", source, StringComparison.Ordinal);
        Assert.Contains("BuildNativeFieldCapabilityOptions", source, StringComparison.Ordinal);
        Assert.Contains("availability.ReportControls.Count", source, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportNativeFieldCapabilityEnvelopeBoundRuntimePlanner.Build", source, StringComparison.Ordinal);
        Assert.Contains("ProvenSafeMemberCount", source, StringComparison.Ordinal);
        Assert.Contains("exact verified-free slots", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generic budget", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P17_EngineLock_PinsPr110AndRetainsRecoveryLineage()
    {
        var engineLock = Read("engines/ARIEC61850.lock.json");

        Assert.Contains("9b60458ed910a410b843185384f0e04d3ca78ce0", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sourcePullRequest\": 110", engineLock, StringComparison.Ordinal);
        Assert.Contains("PR #108", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #109", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #110", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("envelope-bounded native runtime", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProvenSafeMemberCount", engineLock, StringComparison.Ordinal);
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
