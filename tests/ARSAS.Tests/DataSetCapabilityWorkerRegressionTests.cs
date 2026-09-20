namespace ARSAS.Tests;

public sealed class DataSetCapabilityWorkerRegressionTests
{
    [Fact]
    public void OpenSclAndDiscovery_BothQueueTheSameCapabilityPreparation()
    {
        var main = Read("MainWindow.xaml.cs");

        var sclStart = main.IndexOf("private void ApplySclWorkspaceToDevice", StringComparison.Ordinal);
        var sclEnd = main.IndexOf("private static string BuildSclWorkspaceSummary", sclStart, StringComparison.Ordinal);
        Assert.True(sclStart >= 0 && sclEnd > sclStart);
        Assert.Contains("QueueDataSetCapabilityRefresh(device);", main[sclStart..sclEnd], StringComparison.Ordinal);

        var discoveryStart = main.IndexOf("private async Task<bool> ConnectAndConfigureDeviceAsync", StringComparison.Ordinal);
        var discoveryEnd = main.IndexOf("private async Task<bool> ConnectUsingSavedModelAsync", discoveryStart, StringComparison.Ordinal);
        Assert.True(discoveryStart >= 0 && discoveryEnd > discoveryStart);
        Assert.Contains("QueueDataSetCapabilityRefresh(device);", main[discoveryStart..discoveryEnd], StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilityWorker_IsGenerationScopedAndDoesNotPollMms()
    {
        var worker = Read("MainWindow.DataSetCapabilityIndex.cs");
        var builder = Read("Services/Iec61850DataSetCapabilityIndexBuilder.cs");

        Assert.Contains("var generation = device.ModelGeneration", worker, StringComparison.Ordinal);
        Assert.Contains("device.TryApplyDataSetCapabilityIndex(index)", worker, StringComparison.Ordinal);
        Assert.Contains("previous.Cancel()", worker, StringComparison.Ordinal);
        Assert.Contains("Task.Run", builder, StringComparison.Ordinal);

        Assert.DoesNotContain("ReadValueAsync", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDataSetDirectoriesAsync", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitoring", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadValueAsync", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDataSetDirectoriesAsync", builder, StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilityFingerprint_ExcludesMutableRcbRuntimeState()
    {
        var builder = Read("Services/Iec61850DataSetCapabilityIndexBuilder.cs");

        Assert.Contains("report.TriggerOptions", builder, StringComparison.Ordinal);
        Assert.Contains("report.OptionalFields", builder, StringComparison.Ordinal);
        Assert.Contains("report.IntegrityPeriodMs", builder, StringComparison.Ordinal);

        Assert.DoesNotContain("report.EnabledState", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("report.ReservationState", builder, StringComparison.Ordinal);
        Assert.DoesNotContain("report.ReservationTimeSeconds", builder, StringComparison.Ordinal);
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
