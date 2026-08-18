namespace ARSAS.Tests;

public sealed class HybridReportDynamicAttemptP4RegressionTests
{
    [Fact]
    public void Planning_ProjectsEngineAttemptEvidenceInsteadOfSilentPolling()
    {
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");
        var model = Read("Models/NativeHybridReportAcquisitionModels.cs");

        Assert.Contains("MmsHybridDynamicAttemptEvidenceBuilder.Build", bridge, StringComparison.Ordinal);
        Assert.Contains("PointAttemptEvidence = pointAttemptEvidence", bridge, StringComparison.Ordinal);
        Assert.Contains("CatalogMappingUnavailable", bridge, StringComparison.Ordinal);
        Assert.Contains("FreshReportDiscoveryUnavailable", bridge, StringComparison.Ordinal);
        Assert.Contains("DynamicAttemptDisposition", model, StringComparison.Ordinal);
        Assert.Contains("PollingFallbackReason", model, StringComparison.Ordinal);
    }

    [Fact]
    public void Activation_UsesAttemptAwareEngineAndRollsStaticFailureIntoDynamicRecovery()
    {
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");
        var recovery = Read("Services/NativeIec61850Client.HybridReporting.P4.cs");

        Assert.Contains("StartPersistentReportMonitorWithAttemptEvidenceAsync", bridge, StringComparison.Ordinal);
        Assert.True(Count(bridge, "TryStartDynamicRecoveryAfterStaticFailureP4Async") >= 4);
        Assert.Contains("AllowStaticBrcb = false", recovery, StringComparison.Ordinal);
        Assert.Contains("AllowStaticUrcb = false", recovery, StringComparison.Ordinal);
        Assert.Contains("MmsCapabilityAwareHybridReportAcquisitionPlanner.Build", recovery, StringComparison.Ordinal);
        Assert.Contains("StartPersistentReportMonitorWithAttemptEvidenceAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("DynamicActivationFailed", recovery, StringComparison.Ordinal);
        Assert.Contains("CleanupAttempted = attempt.CleanupAttempted", recovery, StringComparison.Ordinal);
        Assert.Contains("CleanupSucceeded = attempt.CleanupSucceeded", recovery, StringComparison.Ordinal);
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
    public void EngineLock_PinsP4AttemptAwareEngine()
    {
        var engineLock = Read("engines/ARIEC61850.lock.json");

        Assert.Contains("2a932e183931eb65c775fe01cf8a47bf8a9af458", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sourcePullRequest\": 86", engineLock, StringComparison.Ordinal);
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
