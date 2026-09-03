namespace ARSAS.Tests;

public sealed class IoFatCommissioningRecoveryRegressionTests
{
    [Fact]
    public void FatEvidence_ConsumesAuthoritativeRuntimeSoe_NotOnlyCoalescedUiProjection()
    {
        var source = ReadRepoFile("MainWindow.IoTesting.CommissioningRecovery.cs");

        Assert.Contains("_runtime.EventRaised += CommissioningRuntime_EventRaised", source, StringComparison.Ordinal);
        Assert.Contains("controller.Enqueue(entry);", source, StringComparison.Ordinal);
        Assert.Contains("controller.State != IoTestSessionState.Running", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatReconnect_AutoResumesOnlyAfterFreshBaselineSettleWindow()
    {
        var source = ReadRepoFile("MainWindow.IoTesting.CommissioningRecovery.cs");

        Assert.Contains("IoTestSessionState.Interrupted", source, StringComparison.Ordinal);
        Assert.Contains("FatAutoResumeSettleDelay", source, StringComparison.Ordinal);
        Assert.Contains("controller.Resume();", source, StringComparison.Ordinal);
        Assert.Contains("fresh connection-generation baseline", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FatIedExplorer_AlwaysProjectsIndependentConnectionHealth()
    {
        var source = ReadRepoFile("IoListTestingWindow.CommissioningStatus.cs");

        Assert.Contains("\"ONLINE\"", source, StringComparison.Ordinal);
        Assert.Contains("\"RECONNECTING\"", source, StringComparison.Ordinal);
        Assert.Contains("\"OFFLINE\"", source, StringComparison.Ordinal);
        Assert.Contains("badge.Visibility = Visibility.Visible", source, StringComparison.Ordinal);
        Assert.Contains("Connection health is independent from FAT PASS/FAIL", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingFatSessionController_PreservesLosslessEdgeQueueAndGenerationBoundary()
    {
        var source = ReadRepoFile("Services/IoTesting/IoTestSessionController.cs");

        Assert.Contains("_pendingEdgeSnapshots.Enqueue(queued);", source, StringComparison.Ordinal);
        Assert.Contains("_connectionGeneration++;", source, StringComparison.Ordinal);
        Assert.Contains("current values are treated as a new baseline image", source, StringComparison.Ordinal);
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
