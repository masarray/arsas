namespace ARSAS.Tests;

public sealed class IoTestFatP0RegressionTests
{
    [Fact]
    public void FatWorkspace_UsesFastVerificationCadenceWithoutBypassingReportFirstPlanning()
    {
        var main = Read("MainWindow.IoTesting.cs");
        var planner = Read("Services/Iec61850ReportPlanner.cs");

        // Legacy workbook-only FAT may temporarily tighten MMS verification cadence, but
        // that cadence must not classify fast status/protection points as polling-only.
        // Shared SCL workspaces are guarded separately: FAT consumes the Engineering
        // acquisition session and never owns a second polling runtime.
        Assert.Contains("private const int IoFatPollingIntervalMs = 250;", main, StringComparison.Ordinal);
        Assert.Contains("_pollingIntervalBeforeIoFat", main, StringComparison.Ordinal);
        Assert.Contains("PollingIntervalMs = Math.Min(PollingIntervalMs, IoFatPollingIntervalMs);", main, StringComparison.Ordinal);
        Assert.Contains("var reportCandidates = all;", planner, StringComparison.Ordinal);
        Assert.Contains("PollingIntervalMs describes the fallback/verification cadence", planner, StringComparison.Ordinal);
        Assert.Contains("var dynamicMembers = reportCandidates", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("FastCommissioningPollingThresholdMs", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("fastPollingPoints", planner, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedSclFat_ReadinessDoesNotRestartMonitorOrWaitForLegacyReportSettle()
    {
        var source = Read("MainWindow.IoTesting.AutoConnect.cs");

        Assert.Contains("reuseSharedSclAcquisition", source, StringComparison.Ordinal);
        Assert.Contains("FAT attached to the existing shared acquisition session · no monitor restart", source, StringComparison.Ordinal);
        Assert.Contains("StartDeviceMonitorAsync(device, navigateToExplorer: false)", source, StringComparison.Ordinal);
        Assert.Contains("Compatibility path for non-shared/legacy FAT only", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds(2500)", source, StringComparison.Ordinal);
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

        // Every bounded drain yields to operator input. Edge samples remain in the
        // dedicated lossless queue, so responsiveness does not weaken FAT evidence.
        Assert.Contains("DispatchIoFatEvidence", main, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", main, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.DataBind", main, StringComparison.Ordinal);
        Assert.Contains("_pendingEdgeSnapshots", controller, StringComparison.Ordinal);
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
        Assert.DoesNotContain("point.TestEnabled =", service, StringComparison.Ordinal);
        Assert.Contains("Selection belongs to the operator", service, StringComparison.Ordinal);
        Assert.Contains("storage.SaveNow()", service, StringComparison.Ordinal);
        Assert.Contains("New Clean FAT", ui, StringComparison.Ordinal);
        Assert.Contains("Session.ResetForCleanRetest()", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void FatUiRefresh_DoesNotRefilterOrAutosaveForEveryLiveSample()
    {
        var ux = Read("IoListTestingWindow.FatV2Ux.cs");
        var persistence = Read("Services/IoTesting/IoTestProjectPersistenceService.cs");
        var controller = Read("Services/IoTesting/IoTestSessionController.cs");

        Assert.Contains("RefreshFatV2WorkspaceUx(bool refreshRows = false)", ux, StringComparison.Ordinal);
        Assert.Contains("if (refreshRows)", ux, StringComparison.Ordinal);
        Assert.Contains("nameof(IoTestPointRuntime.CurrentValue)", persistence, StringComparison.Ordinal);
        Assert.Contains("if (progressChanged)", controller, StringComparison.Ordinal);
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
