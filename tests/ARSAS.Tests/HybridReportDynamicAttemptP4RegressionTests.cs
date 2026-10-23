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
    public void StaticFailure_IsIsolatedAndNeverStartsDynamicMutation()
    {
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");
        var recovery = Read("Services/NativeIec61850Client.HybridReporting.P4.cs");

        // The ordinary residual dynamic path is still attempt-aware.
        Assert.Contains("StartPersistentReportMonitorWithAttemptEvidenceAsync", bridge, StringComparison.Ordinal);
        Assert.True(Count(bridge, "TryStartDynamicRecoveryAfterStaticFailureP4Async") >= 4);

        // P6.1 intentionally keeps the old method name only as a source-compatible,
        // fail-closed hook. Static failure must never create a new DataSet or write another RCB.
        Assert.Contains("baseline static-failure isolation", recovery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no dynamic DataSet/RCB write was attempted", recovery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UsedDynamicDataSet = false", recovery, StringComparison.Ordinal);
        Assert.Contains("DynamicAttempted = false", recovery, StringComparison.Ordinal);
        Assert.Contains("FailureReason = \"StaticActivationFailed\"", recovery, StringComparison.Ordinal);
        Assert.Contains("PollingFallbackReason = \"StaticActivationFailed\"", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowStaticBrcb = false", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowStaticUrcb = false", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsCapabilityAwareHybridReportAcquisitionPlanner.Build", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitorWithAttemptEvidenceAsync", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("DynamicWriteCircuitByDevice[appPlan.RelayId]", recovery, StringComparison.Ordinal);
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
