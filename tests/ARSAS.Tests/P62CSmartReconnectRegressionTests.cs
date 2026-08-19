using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P62CSmartReconnectRegressionTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 15)]
    [InlineData(6, 30)]
    [InlineData(20, 30)]
    public void RetryBackoff_IsBoundedAndDeterministic(int failureCount, int expectedSeconds)
        => Assert.Equal(
            TimeSpan.FromSeconds(expectedSeconds),
            SmartReconnectPolicy.GetRetryDelay(failureCount));

    [Fact]
    public void RecoveryWarmup_ReducesImmediateMmsPressureWithoutChangingSteadyState()
    {
        Assert.Equal(2000, SmartReconnectPolicy.ApplyRecoveryPollFloor(1000, recoveryWarmup: true));
        Assert.Equal(10000, SmartReconnectPolicy.ApplyRecoveryPollFloor(10000, recoveryWarmup: true));
        Assert.Equal(1000, SmartReconnectPolicy.ApplyRecoveryPollFloor(1000, recoveryWarmup: false));
        Assert.Equal(0, SmartReconnectPolicy.GetRecoveryStaggerDelayMs(0));
        Assert.Equal(2000, SmartReconnectPolicy.GetRecoveryStaggerDelayMs(100));
        Assert.Equal(2000, SmartReconnectPolicy.GetRecoveryStaggerDelayMs(1000));
    }

    [Fact]
    public void ReconnectBudgets_AreShorterThanAFieldVisibleStall()
    {
        Assert.True(SmartReconnectPolicy.ClientCleanupBudget <= TimeSpan.FromSeconds(1));
        Assert.True(SmartReconnectPolicy.ConnectBudget <= TimeSpan.FromSeconds(10));
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
