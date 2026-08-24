namespace ARSAS.Tests;

public sealed class HybridReportDynamicAttemptP4RegressionTests
{
    [Fact]
    public void Planning_ProjectsEngineAttemptEvidenceInsteadOfSilentPolling()
    {
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");
        var recovery = Read("Services/NativeIec61850Client.HybridReporting.P4.cs");
        var model = Read("Models/NativeHybridReportAcquisitionModels.cs");

        Assert.Contains("MmsHybridDynamicAttemptEvidenceBuilder.Build", bridge, StringComparison.Ordinal);
        Assert.Contains("PointAttemptEvidence = pointAttemptEvidence", bridge, StringComparison.Ordinal);
        Assert.Contains("UnmappedAttemptEvidence", bridge, StringComparison.Ordinal);
        Assert.Contains("CatalogMappingUnavailable", recovery, StringComparison.Ordinal);
        Assert.Contains("FreshReportDiscoveryUnavailable", bridge, StringComparison.Ordinal);
        Assert.Contains("DynamicAttemptDisposition", model, StringComparison.Ordinal);
        Assert.Contains("PollingFallbackReason", model, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticFailure_GetsGuardedAlternateDynamicRecoveryBeforePolling()
    {
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");
        var recovery = Read("Services/NativeIec61850Client.HybridReporting.P4.cs");

        Assert.Contains("StartPersistentReportMonitorWithAttemptEvidenceAsync", bridge, StringComparison.Ordinal);
        Assert.True(Count(bridge, "TryStartDynamicRecoveryAfterStaticFailureP4Async") >= 4);

        // G2.6 may recover a failed static segment, but only through the ARIEC planner and
        // a different RCB with fresh availability evidence. P4 never writes an RCB directly.
        Assert.Contains("alternateSnapshots", recovery, StringComparison.Ordinal);
        Assert.Contains("!SameLiteralReference(snapshot.Reference, authoritative.ReportControlReference)", recovery, StringComparison.Ordinal);
        Assert.Contains("AllowStaticBrcb = false", recovery, StringComparison.Ordinal);
        Assert.Contains("AllowStaticUrcb = false", recovery, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicBrcb = authoritative.Options.AllowDynamicBrcb", recovery, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicUrcb = authoritative.Options.AllowDynamicUrcb", recovery, StringComparison.Ordinal);
        Assert.Contains("RequireExactAvailabilityEvidence = true", recovery, StringComparison.Ordinal);
        Assert.Contains("MmsCapabilityAwareHybridReportAcquisitionPlanner.Build", recovery, StringComparison.Ordinal);
        Assert.Contains("_authoritativeHybridSubscriptions[appPlan.PlanId]", recovery, StringComparison.Ordinal);
        Assert.Contains("return await StartHybridReportMonitorAsync(appPlan", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitorWithAttemptEvidenceAsync", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineNamedVariableList", recovery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaticPostMutationRecovery_RequiresProvenCleanup()
    {
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");
        var recovery = Read("Services/NativeIec61850Client.HybridReporting.P4.cs");

        Assert.Contains("bool staticCleanupProven = false", recovery, StringComparison.Ordinal);
        Assert.Contains("staticMutationWasAttempted", recovery, StringComparison.Ordinal);
        Assert.Contains("StaticCleanupUnproven", recovery, StringComparison.Ordinal);
        Assert.Contains("a second RCB mutation is forbidden", recovery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("staticCleanupProven: attempt.CleanupSucceeded", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void DynamicRecovery_RetainsCircuitBreakerAndPlanIdentity()
    {
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");
        var recovery = Read("Services/NativeIec61850Client.HybridReporting.P4.cs");

        Assert.Contains("DynamicWriteCircuitByDevice.TryGetValue(appPlan.RelayId", recovery, StringComparison.Ordinal);
        Assert.Contains("DynamicWriteCircuitOpen", recovery, StringComparison.Ordinal);
        Assert.Contains("appPlan.EngineAcquisitionKind = dynamicSegment.Kind.ToString()", recovery, StringComparison.Ordinal);
        Assert.Contains("_authoritativeHybridSubscriptions[appPlan.PlanId]", recovery, StringComparison.Ordinal);
        Assert.Contains("DynamicWriteCircuitByDevice[plan.RelayId] = reason", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void PhysicalValidation_PersistsAttemptFailureAndSkipTelemetry()
    {
        var tracker = Read("Services/HybridReportPhysicalValidationTracker.cs");
        var models = Read("Models/NativeHybridReportAcquisitionModels.cs");
        var start = Read("Models/NativeReportMonitorModels.cs");

        Assert.Contains("state.DynamicAttempted = result.DynamicAttempted", tracker, StringComparison.Ordinal);
        Assert.Contains("DynamicAttemptFailedCount", tracker, StringComparison.Ordinal);
        Assert.Contains("DynamicSkippedPointCount", tracker, StringComparison.Ordinal);
        Assert.Contains("PointAttemptEvidence = attemptEvidence", tracker, StringComparison.Ordinal);
        Assert.Contains("FailureReason", models, StringComparison.Ordinal);
        Assert.Contains("PollingFallbackReason", start, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineLock_PinsAttemptAwareEngine()
    {
        var engineLock = Read("engines/ARIEC61850.lock.json");

        Assert.Contains("dynamic-attempt", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", engineLock, StringComparison.OrdinalIgnoreCase);
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
