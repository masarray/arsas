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
    public void InitialPlanningAndExecutionRevalidation_UseLegacyP16OrNativeP17FieldCapabilityPlanner()
    {
        var guarded = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");

        Assert.Contains("MmsGuardedDynamicReportRuntimePlanner.Build", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportFieldCapabilityStableRuntimePlanner.Build", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportFieldCapabilityPolicy.TryValidate", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportNativeFieldCapabilityStableRuntimePlanner.Build", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportNativeFieldCapabilityPolicy.TryValidate", guarded, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate", guarded, StringComparison.Ordinal);
        Assert.Contains("capability, not member scope", guarded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("general Dynamic RCB coverage", guarded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MmsGuardedDynamicReportLegacySubsetRuntimePlanner.Build", guarded, StringComparison.Ordinal);
        Assert.True(Count(bridge, "BuildCapabilityPlanWithGuardedRuntime(") >= 2);
        Assert.Contains("_guardedRuntimeContexts[appPlan.PlanId] = guardedRuntime.Context", bridge, StringComparison.Ordinal);
        Assert.Contains("TryGetGuardedRuntimeContext(plan.PlanId", bridge, StringComparison.Ordinal);
        Assert.Contains("guardedRuntimeContext", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void GuardedRuntime_DoesNotPromoteOrPersistQualificationEvidence()
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
        Assert.Contains("sidecar witness separate from the qualification profile", guarded, StringComparison.OrdinalIgnoreCase);
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
    public void EngineLock_PinsMergedP17WhileRetainingP16StableGeneralDynamicLineage()
    {
        var engineLock = Read("engines/ARIEC61850.lock.json");

        Assert.Contains("\"commit\": \"c979206988ebcbaf79e62b784895e19547184369\"", engineLock, StringComparison.Ordinal);
        Assert.Contains("\"sourcePullRequest\": 107", engineLock, StringComparison.Ordinal);
        Assert.Contains("PR #104", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("capability evidence rather than permanent member scope", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("all still-uncovered exact-resolved selected signals", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #105", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4d7a896c606194c5533322bf975a2c9c57da7c64", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deterministic AR_HYB_<SHA256-prefix>", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #107", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InformationReportProven", engineLock, StringComparison.Ordinal);
        Assert.Contains("ProductionEligible stays independent", engineLock, StringComparison.OrdinalIgnoreCase);
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