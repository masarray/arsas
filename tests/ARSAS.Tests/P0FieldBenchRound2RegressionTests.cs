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
    public void RcbProductionButton_ClassClickConsumesLegacyAndUsesMultiExporter()
    {
        var authority = File.ReadAllText(FindRepoFile("MainWindow.RcbExportClickAuthority.cs"));
        var route = File.ReadAllText(FindRepoFile("MainWindow.MultiRcbExport.cs"));
        var window = File.ReadAllText(FindRepoFile("RcbMultiExportWindow.cs"));

        Assert.Contains("Button.ClickEvent", authority, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", authority, StringComparison.Ordinal);
        Assert.Contains("var resolvedSender = new Button { Tag = device }", authority, StringComparison.Ordinal);
        Assert.Contains("window.IedEditRcbMulti_Click(resolvedSender, e)", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("button.Tag == null", authority, StringComparison.Ordinal);

        Assert.Contains("button.Click -= window.IedEditRcb_Click", route, StringComparison.Ordinal);
        Assert.Contains("button.Click += window.IedEditRcbMulti_Click", route, StringComparison.Ordinal);
        Assert.Contains("select any number of native RCBs", route, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Binding = new Binding(nameof(RcbExportRow.IsSelected))", window, StringComparison.Ordinal);
        Assert.Contains("_rows.Where(row => row.IsSelected).ToArray()", window, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(FindRepoRoot(), "MainWindow.RcbMultiSelectPointerAuthority.cs")));
    }

    [Fact]
    public void DownloadedComtrade_UsesOneNormalCheckboxWithoutExtraGlyph()
    {
        var authority = File.ReadAllText(FindRepoFile("FaultRecordWindow.RedownloadSelectionAuthority.cs"));
        var transfer = File.ReadAllText(FindRepoFile("FaultRecordWindow.RedownloadUx.cs"));

        Assert.Contains("FindRedownloadSelectionAncestor<CheckBox>(source) != null", authority, StringComparison.Ordinal);
        Assert.Contains("window.SetDownloadedTransferSelection(row, !window.IsDownloadedTransferSelected(row))", authority, StringComparison.Ordinal);
        Assert.Contains("window.UpdateSmartSelectionUi()", authority, StringComparison.Ordinal);
        Assert.Contains("window.RefreshFaultRecordHeaderSelection()", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("PaintTransferSelection", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("checkBox.Content", authority, StringComparison.Ordinal);

        Assert.Contains("RedownloadGrid_PreviewMouseLeftButtonDown", transfer, StringComparison.Ordinal);
        Assert.Contains("checkBox.IsChecked = _redownloadSelections.Contains", transfer, StringComparison.Ordinal);
        Assert.Contains("_redownloadSelections.Add(row.Record.RecordId)", transfer, StringComparison.Ordinal);
        Assert.Contains(".arsas-redownload-", transfer, StringComparison.Ordinal);
        Assert.Contains("CommitFreshRecordDirectory", transfer, StringComparison.Ordinal);
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
