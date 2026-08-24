namespace ARSAS.Tests;

public sealed class G26ShadowEvidenceRecorderRegressionTests
{
    [Fact]
    public void Recorder_IsExactMemberBoundedAndDoesNotSynthesizeQualityOrTimestamp()
    {
        var source = Read("Services/DynamicReportShadowEvidenceRecorder.cs");

        Assert.Contains("ValidateExactMember(dataSetIndex, memberReference)", source, StringComparison.Ordinal);
        Assert.Contains("MaximumReportObservations = 4096", source, StringComparison.Ordinal);
        Assert.Contains("MaximumPollObservations = 16384", source, StringComparison.Ordinal);
        Assert.Contains("Quality = NormalizeOptional(quality)", source, StringComparison.Ordinal);
        Assert.Contains("DeviceTimestampUtc = deviceTimestampUtc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Quality = \"good\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeviceTimestampUtc = DateTimeOffset.UtcNow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Recorder_TracksReconnectRecoveryAndDynamicAttemptsExplicitly()
    {
        var source = Read("Services/DynamicReportShadowEvidenceRecorder.cs");

        Assert.Contains("RecordDynamicActivationAttempt", source, StringComparison.Ordinal);
        Assert.Contains("RecordReconnectAttempt", source, StringComparison.Ordinal);
        Assert.Contains("RecordReconnectSuccess", source, StringComparison.Ordinal);
        Assert.Contains("ReportResubscriptionsAfterReconnect = _reportResubscriptionsAfterReconnect", source, StringComparison.Ordinal);
        Assert.Contains("PollReferenceRecoveriesAfterReconnect = _pollReferenceRecoveriesAfterReconnect", source, StringComparison.Ordinal);
        Assert.Contains("DynamicActivationAttempts = _dynamicActivationAttempts", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Recorder_PerformsNoNetworkOrProfileMutation()
    {
        var source = Read("Services/DynamicReportShadowEvidenceRecorder.cs");

        Assert.DoesNotContain("MmsClientSession", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteControlAsync", source, StringComparison.Ordinal);
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
