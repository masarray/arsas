using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P62CSmartReconnectRegressionTests
{
    [Theory]
    [InlineData(1, 500)]
    [InlineData(2, 1000)]
    [InlineData(3, 2000)]
    [InlineData(4, 2000)]
    [InlineData(20, 2000)]
    public void RetryBackoff_IsBoundedAndDeterministic(int failureCount, int expectedMilliseconds)
        => Assert.Equal(
            TimeSpan.FromMilliseconds(expectedMilliseconds),
            SmartReconnectPolicy.GetRetryDelay(failureCount));

    [Theory]
    [InlineData("TCP_TIMEOUT", true)]
    [InlineData("TCP_TRANSPORT_CLOSED", true)]
    [InlineData("TCP_CONNECTION_REFUSED", true)]
    [InlineData("NATIVE_ASSOCIATION_CANCELLED", true)]
    [InlineData("NETWORK_UNREACHABLE", false)]
    [InlineData("ACSE_MMS_ASSOCIATION_REJECTED", false)]
    [InlineData("IEC61850_CONNECTION_FAILED", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void InitialDiscovery_RetriesOnlyKnownTransientTransportFailures(
        string? failureKind, bool expected)
    {
        Assert.Equal(expected, SmartReconnectPolicy.CanRetryInitialDiscoveryAssociation(failureKind));
        Assert.Equal(2, SmartReconnectPolicy.InitialDiscoveryAssociationMaxAttempts);
    }

    [Fact]
    public void AssociationRecovery_UsesOneExecutorAndNeverReplaysDiscoveryOrControl()
    {
        var runtime = ReadRepoFile("Services/Iec61850MonitorRuntime.cs");
        var fullDiscoveryCall = runtime.IndexOf(
            "await ConnectInitialDiscoveryAssociationWithRetryAsync(", StringComparison.Ordinal);
        var discoveryCall = runtime.IndexOf(
            "await session.Client.DiscoverSignalsAsync(", StringComparison.Ordinal);
        var cachedCall = runtime.IndexOf(
            "await ConnectCachedAssociationWithRetryAsync(", StringComparison.Ordinal);
        var executor = runtime.IndexOf(
            "private async Task ConnectAssociationWithRetryAsync(", StringComparison.Ordinal);
        var next = runtime.IndexOf(
            "private static async Task ResetAssociationControlAuthorityAsync(", StringComparison.Ordinal);
        Assert.True(fullDiscoveryCall >= 0 && fullDiscoveryCall < discoveryCall);
        Assert.True(cachedCall >= 0 && executor >= 0 && next > executor);

        var owner = runtime[executor..next];
        Assert.DoesNotContain("DiscoverSignalsAsync(", owner, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReport", owner, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteReport", owner, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteControlAsync(", owner, StringComparison.Ordinal);
        Assert.Contains("new NativeIec61850Client()", owner, StringComparison.Ordinal);
        Assert.Contains("InitialAssociationRetryDelay", owner, StringComparison.Ordinal);
        Assert.Contains("ClientCleanupBudget", owner, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", owner, StringComparison.Ordinal);
        Assert.Contains("CanRetryInitialDiscoveryAssociation(failureKind)", owner, StringComparison.Ordinal);
        Assert.Contains("cachedModel ? \"Fast saved-model\" : \"Initial discovery\"", owner, StringComparison.Ordinal);
        Assert.Contains("budget.CancelAfter(SmartReconnectPolicy.ConnectBudget)", owner, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoveryWarmup_RemainsResponsiveForCommissioningReconnect()
    {
        Assert.Equal(500, SmartReconnectPolicy.ApplyRecoveryPollFloor(250, recoveryWarmup: true));
        Assert.Equal(1000, SmartReconnectPolicy.ApplyRecoveryPollFloor(1000, recoveryWarmup: true));
        Assert.Equal(250, SmartReconnectPolicy.ApplyRecoveryPollFloor(250, recoveryWarmup: false));
        Assert.Equal(0, SmartReconnectPolicy.GetRecoveryStaggerDelayMs(0));
        Assert.Equal(500, SmartReconnectPolicy.GetRecoveryStaggerDelayMs(100));
        Assert.Equal(500, SmartReconnectPolicy.GetRecoveryStaggerDelayMs(1000));
    }

    [Fact]
    public void ReconnectBudgets_AreCommissioningResponsive()
    {
        Assert.True(SmartReconnectPolicy.ClientCleanupBudget <= TimeSpan.FromMilliseconds(500));
        Assert.True(SmartReconnectPolicy.ConnectBudget <= TimeSpan.FromSeconds(2));
        Assert.True(SmartReconnectPolicy.GetRetryDelay(20) <= TimeSpan.FromSeconds(2));
        Assert.True(SmartReconnectPolicy.ReportRearmDelay < SmartReconnectPolicy.RecoveryWarmupDuration);
        Assert.True(SmartReconnectPolicy.ReportRearmDeadline <= SmartReconnectPolicy.RecoveryWarmupDuration);
    }

    [Fact]
    public void Runtime_LogsDegradedQualityAsEvidenceWithoutForcingGood()
    {
        var source = ReadRepoFile("Services/Iec61850MonitorRuntime.cs");

        Assert.Contains("QUALITY_EVIDENCE:", source, StringComparison.Ordinal);
        Assert.Contains("Quality is preserved from IED evidence and is not converted to Good.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("quality = \"Good\";", source, StringComparison.Ordinal);
        Assert.DoesNotContain("state.Quality = \"Good\";", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
