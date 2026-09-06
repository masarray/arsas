namespace ARSAS.Tests;

public sealed class P0FatEvidenceResponsivenessAndRestoreRegressionTests
{
    [Fact]
    public void P0_StartStopAndClose_MoveDurableEvidenceWorkOffDispatcher()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.P0EvidenceResponsiveness.cs"));

        Assert.Contains("Click -= StartSelectedIedSafely_Click", source, StringComparison.Ordinal);
        Assert.Contains("Click -= StopSession_Click", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => Session.Start(selectedIed, liveCaptureScope))", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => Session.Stop())", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() => Session.StopAll(", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(Storage.SaveNow)", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.Yield(DispatcherPriority.Render)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_uiFlushTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetVirtualizationMode", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_RemoveFromFat_HasDeterministicRestoreEntryPointAfterEngineeringCaptionRename()
    {
        var recovery = File.ReadAllText(FindRepoFile("IoListTestingWindow.P0RemovedSignalsRecovery.cs"));
        var v2 = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));
        var restoreWindow = File.ReadAllText(FindRepoFile("RemovedFatSignalsWindow.cs"));

        Assert.Contains("StartsWith(\"Engineering\"", recovery, StringComparison.Ordinal);
        Assert.Contains("Contains(\"Export .arsas\"", recovery, StringComparison.Ordinal);
        Assert.Contains("RemovedSignals_Click", recovery, StringComparison.Ordinal);
        Assert.Contains("point.RemoveFromFat();", v2, StringComparison.Ordinal);
        Assert.Contains("!point.IsIncludedInFat", v2, StringComparison.Ordinal);
        Assert.Contains("row.Point.RestoreToFat();", restoreWindow, StringComparison.Ordinal);
        Assert.Contains("Restore Selected", restoreWindow, StringComparison.Ordinal);
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

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
