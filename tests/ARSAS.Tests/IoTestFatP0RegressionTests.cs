namespace ARSAS.Tests;

public sealed class IoTestFatP0RegressionTests
{
    [Fact]
    public void FatWorkspace_UsesFastCommissioningPollingWithoutChangingNormalDefault()
    {
        var main = Read("MainWindow.IoTesting.cs");
        var planner = Read("Services/Iec61850ReportPlanner.cs");

        Assert.Contains("private const int IoFatPollingIntervalMs = 250;", main, StringComparison.Ordinal);
        Assert.Contains("_pollingIntervalBeforeIoFat", main, StringComparison.Ordinal);
        Assert.Contains("PollingIntervalMs = Math.Min(PollingIntervalMs, IoFatPollingIntervalMs);", main, StringComparison.Ordinal);
        Assert.Contains("FastCommissioningPollingThresholdMs = 500", planner, StringComparison.Ordinal);
        Assert.Contains("!IsFastCommissioningPoint(point)", planner, StringComparison.Ordinal);
    }

    [Fact]
    public void FatConnect_ReadinessDoesNotBlockOnLegacyEightToEighteenSecondReportSettle()
    {
        var source = Read("MainWindow.IoTesting.AutoConnect.cs");

        Assert.Contains("TimeSpan.FromMilliseconds(2500)", source, StringComparison.Ordinal);
        Assert.Contains("Waiting for first live FAT image", source, StringComparison.Ordinal);
        Assert.Contains("fast MMS verification", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TimeSpan.FromSeconds(8)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromSeconds(10)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("rebuilding the report plan once", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FatEvidenceDrain_PrioritizesFirstEdgeButYieldsContinuationAndBatchesStartupJournal()
    {
        var controller = Read("Services/IoTesting/IoTestSessionController.cs");
        var journal = Read("Services/IoTesting/IoTestEvidenceJournal.cs");
        var main = Read("MainWindow.IoTesting.cs");

        Assert.Contains("MaxSnapshotsPerDrain = 64", controller, StringComparison.Ordinal);
        Assert.Contains("DrainBudgetMilliseconds = 4", controller, StringComparison.Ordinal);
        Assert.Contains("stopwatch.ElapsedMilliseconds < DrainBudgetMilliseconds", controller, StringComparison.Ordinal);
        Assert.Contains("AppendBatchRequired(startupEntries)", controller, StringComparison.Ordinal);
        Assert.Contains("AppendBatch(IEnumerable<IoTestJournalEntry> entries)", journal, StringComparison.Ordinal);
        Assert.Contains("FlushDurable();", journal, StringComparison.Ordinal);

        // Preserve #150's immediate TRUE-edge scheduling while retaining P0's bounded
        // drain/yield behavior for bursts. The first dispatch is DataBind; a reschedule
        // requested while that drain is active is intentionally demoted to Background.
        Assert.Contains("DispatchIoFatEvidence", main, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _ioFatEvidenceDrainDispatchActive) == 0", main, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.DataBind", main, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", main, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment(ref _ioFatEvidenceDrainDispatchActive)", main, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Decrement(ref _ioFatEvidenceDrainDispatchActive)", main, StringComparison.Ordinal);
    }

    [Fact]
    public void NewCleanFatSession_ArchivesOldJournalsAndClearsEveryActiveTimestampSource()
    {
        var service = Read("Services/IoTesting/IoFatCleanSessionService.cs");
        var ui = Read("IoListTestingWindow.SupplementalEvidence.cs");

        Assert.Contains("SearchOption.TopDirectoryOnly", service, StringComparison.Ordinal);
        Assert.Contains("IoTestEvidenceJournal.Verify(path)", service, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(evidenceDirectory, \"archive\")", service, StringComparison.Ordinal);
        Assert.Contains("runtime.OnEvidence = null", service, StringComparison.Ordinal);
        Assert.Contains("runtime.OffEvidence = null", service, StringComparison.Ordinal);
        Assert.Contains("runtime.CurrentIedTimestamp = \"—\"", service, StringComparison.Ordinal);
        Assert.Contains("point.TestEnabled = point.ImportReady", service, StringComparison.Ordinal);
        Assert.Contains("storage.SaveNow()", service, StringComparison.Ordinal);
        Assert.Contains("New Clean FAT", ui, StringComparison.Ordinal);
        Assert.Contains("Session.ResetForCleanRetest()", ui, StringComparison.Ordinal);
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
