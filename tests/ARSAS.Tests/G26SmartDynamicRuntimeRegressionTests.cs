namespace ARSAS.Tests;

public sealed class G26SmartDynamicRuntimeRegressionTests
{
    [Fact]
    public void NormalMonitoring_LoadsIdentityCompatibleInformationReportProvenContext()
    {
        var guarded = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");

        Assert.Contains("DynamicReportQualificationIdentity.Build(device, device.Signals.ToArray())", guarded, StringComparison.Ordinal);
        Assert.Contains("DynamicReportQualificationProfileStore", guarded, StringComparison.Ordinal);
        Assert.Contains("LoadAsync(identity", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsDynamicReportQualificationState.InformationReportProven", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsDynamicInformationReportKind.DataChange", guarded, StringComparison.Ordinal);
        Assert.Contains("TryLoadGuardedRuntimeContextAsync(device", bridge, StringComparison.Ordinal);
        Assert.Contains("MmsDynamicReportGuardedRuntimePlanningContext", guarded, StringComparison.Ordinal);
    }

    [Fact]
    public void InitialPlanningAndExecutionRevalidation_UseSameGuardedPlannerFamily()
    {
        var guarded = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");

        Assert.Contains("MmsGuardedDynamicReportRuntimePlanner.Build", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportLegacySubsetRuntimePlanner.Build", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate", guarded, StringComparison.Ordinal);
        Assert.True(Count(bridge, "BuildCapabilityPlanWithGuardedRuntime(") >= 2);
        Assert.Contains("_guardedRuntimeContexts[appPlan.PlanId] = guardedRuntime.Context", bridge, StringComparison.Ordinal);
        Assert.Contains("TryGetGuardedRuntimeContext(plan.PlanId", bridge, StringComparison.Ordinal);
        Assert.Contains("guardedRuntimeContext", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void GuardedRuntime_DoesNotPromoteOrSaveQualificationProfile()
    {
        var guarded = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");
        var recovery = Read("Services/NativeIec61850Client.HybridReporting.P4.cs");

        Assert.DoesNotContain("MarkProductionEligible(", guarded, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible(", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible(", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", guarded, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", recovery, StringComparison.Ordinal);
        Assert.Contains("ProductionEligible certification remains separate", guarded, StringComparison.Ordinal);
        Assert.Contains("No in-memory DataChange rewrite is performed", guarded, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeStillHasFreshRevalidationCircuitBreakerAndPollingFallback()
    {
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");
        var recovery = Read("Services/NativeIec61850Client.HybridReporting.P4.cs");
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");

        Assert.Contains("CheckReportControlAvailabilityAsync", bridge, StringComparison.Ordinal);
        Assert.Contains("DynamicWriteCircuitByDevice", bridge, StringComparison.Ordinal);
        Assert.Contains("DynamicWriteCircuitByDevice", recovery, StringComparison.Ordinal);
        Assert.Contains("AllowPollingFallback = true", bridge, StringComparison.Ordinal);
        Assert.Contains("AllowPollingFallback = true", recovery, StringComparison.Ordinal);
        Assert.Contains("value changed without matching report", runtime, StringComparison.Ordinal);
        Assert.Contains("MMS fallback", runtime, StringComparison.Ordinal);
        Assert.Contains("Live / report verified + MMS validation", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticRecovery_PreservesPlanBoundGuardedContext()
    {
        var recovery = Read("Services/NativeIec61850Client.HybridReporting.P4.cs");

        Assert.Contains("TryGetGuardedRuntimeContext(appPlan.PlanId", recovery, StringComparison.Ordinal);
        Assert.Contains("BuildCapabilityPlanWithGuardedRuntime", recovery, StringComparison.Ordinal);
        Assert.Contains("_authoritativeHybridSubscriptions[appPlan.PlanId]", recovery, StringComparison.Ordinal);
        Assert.Contains("return await StartHybridReportMonitorAsync(appPlan", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible(", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineLock_PinsMergedP15bSubsetCompatibilityEngine()
    {
        var engineLock = Read("engines/ARIEC61850.lock.json");

        Assert.Contains("\"commit\": \"0965f67fe912355b3b29fc8123872a68d4064b04\"", engineLock, StringComparison.Ordinal);
        Assert.Contains("\"sourcePullRequest\": 102", engineLock, StringComparison.Ordinal);
        Assert.Contains("P1.5b", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact ordered subset", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InformationReportProven", engineLock, StringComparison.Ordinal);
        Assert.Contains("never authorizes ProductionEligible", engineLock, StringComparison.OrdinalIgnoreCase);
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
