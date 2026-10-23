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
