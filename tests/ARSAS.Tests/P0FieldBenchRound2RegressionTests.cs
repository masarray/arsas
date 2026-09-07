namespace ARSAS.Tests;

public sealed class P0FieldBenchRound2RegressionTests
{
    [Fact]
    public void FatSignalColumn_ReplacesActualV2TextColumnWithSemanticTemplate()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.SemanticSignalColumnAuthority.cs"));

        Assert.Contains("_fatSignalsGrid.Columns[index] = replacement", source, StringComparison.Ordinal);
        Assert.Contains("existing is DataGridTemplateColumn", source, StringComparison.Ordinal);
        Assert.Contains("SortMemberPath = nameof(IoTestPointPlan.SignalName)", source, StringComparison.Ordinal);
        Assert.Contains("IoFatSignalDisplayNameFormatter.Format(point)", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RcbProductionButton_RemovesLegacyInstanceHandlerAndUsesMultiExporter()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.RcbExportClickAuthority.cs"));
        var window = File.ReadAllText(FindRepoFile("RcbMultiExportWindow.cs"));

        Assert.Contains("button.Click -= IedEditRcb_Click", source, StringComparison.Ordinal);
        Assert.Contains("button.Click += IedEditRcbMulti_Click", source, StringComparison.Ordinal);
        Assert.Contains("select any number of native RCBs", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Binding = new Binding(nameof(RcbExportRow.IsSelected))", window, StringComparison.Ordinal);
        Assert.Contains("_rows.Where(row => row.IsSelected).ToArray()", window, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(FindRepoRoot(), "MainWindow.RcbMultiSelectPointerAuthority.cs")));
    }

    [Fact]
    public void DownloadedComtrade_SelectionTickIsRepaintedAfterFullInputCycle()
    {
        var source = File.ReadAllText(FindRepoFile("FaultRecordWindow.RedownloadSelectionAuthority.cs"));

        Assert.Contains("_redownloadSelections.Add(recordId)", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ContextIdle", source, StringComparison.Ordinal);
        Assert.Contains("PaintTransferSelection", source, StringComparison.Ordinal);
        Assert.Contains("checkBox.IsChecked = selected", source, StringComparison.Ordinal);
        Assert.Contains("checkBox.Content = selected ? \"✓\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatLiveAndEvidence_UseSameRuntimeSnapshotAndDispatcherTurn()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatSharedProcessEvidence.cs"));

        var live = source.IndexOf("ApplyP0FatSnapshot(plan.Runtime, snapshot)", StringComparison.Ordinal);
        var barrier = source.IndexOf("IsAtomicFatLiveCommitCurrent(plans, snapshot)", live, StringComparison.Ordinal);
        var evidence = source.IndexOf("coordinator.PrimaryController.Enqueue(entry)", barrier, StringComparison.Ordinal);

        Assert.True(live >= 0 && barrier > live && evidence > barrier);
        Assert.Contains("_runtime.PointUpdated += P0FatAtomicPointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.DataBind", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_uiFlushTimer.Tick +=", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArIED61850Tester.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string FindRepoFile(string relativePath)
        => Path.Combine(FindRepoRoot(), relativePath);
}
