namespace ARSAS.Tests;

public sealed class SmvFastWorkflowTests
{
    [Fact]
    public void IedCard_ExposesOneClickSmvWorkspaceAndRoutedAdapterSelection()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IedSmvQuickStart.cs"));

        Assert.Contains("ARSAS_IED_SMV_QUICK_START", source, StringComparison.Ordinal);
        Assert.Contains("actionGrid.Columns = Math.Max(7", source, StringComparison.Ordinal);
        Assert.Contains("new SmvSnapshotCaptureService().ListAdapters()", source, StringComparison.Ordinal);
        Assert.Contains("ResolveLocalIpv4ForTarget", source, StringComparison.Ordinal);
        Assert.Contains("CaptureAdapterMatchesNetworkInterface", source, StringComparison.Ordinal);
        Assert.Contains("new SmvViewerWindow(device)", source, StringComparison.Ordinal);
        Assert.Contains("window.EnableFastWorkflow(adapter, routeDetail)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SmvWorkspace_AutoSelectsFirstStreamAndRunsTheExistingGuardedCapture()
    {
        var source = File.ReadAllText(FindRepoFile("SmvViewerWindow.FastWorkflow.cs"));

        Assert.Contains("DispatcherPriority.ContextIdle", source, StringComparison.Ordinal);
        Assert.Contains("SelectedStream ??= Streams.FirstOrDefault()", source, StringComparison.Ordinal);
        Assert.Contains("SelectedAdapter = matchedAdapter", source, StringComparison.Ordinal);
        Assert.Contains("StreamGrid.ScrollIntoView(SelectedStream)", source, StringComparison.Ordinal);
        Assert.Contains("CaptureButton.RaiseEvent", source, StringComparison.Ordinal);
        Assert.Contains("existing P0 stream-identity", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay", source, StringComparison.Ordinal);
    }

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

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
