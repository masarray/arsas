namespace ARSAS.Tests;

public sealed class P0FieldBenchRound2RegressionTests
{
    [Fact]
    public void FatSignalColumn_UsesDeterministicPhaseAwareTemplate()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.SemanticSignalColumnAuthority.cs"));

        Assert.Contains("Window.GetWindow(grid) is not IoListTestingWindow", source, StringComparison.Ordinal);
        Assert.Contains("column.CellTemplate = BuildSemanticSignalTemplate()", source, StringComparison.Ordinal);
        Assert.Contains("IoFatSignalDisplayNameFormatter.Format(point)", source, StringComparison.Ordinal);
        Assert.Contains("new Binding(\".\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RcbProductionPointer_OpensMultiExporterAndRowsToggleIndependently()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.RcbMultiSelectPointerAuthority.cs"));

        Assert.Contains("PreviewMouseLeftButtonDownEvent", source, StringComparison.Ordinal);
        Assert.Contains("window.IedEditRcbMulti_Click(button, e)", source, StringComparison.Ordinal);
        Assert.Contains("Window.GetWindow(grid) is not RcbMultiExportWindow", source, StringComparison.Ordinal);
        Assert.Contains("row.IsSelected = !row.IsSelected", source, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadedComtrade_SelectionTickIsRepaintedAfterPreviewInput()
    {
        var source = File.ReadAllText(FindRepoFile("FaultRecordWindow.RedownloadSelectionAuthority.cs"));

        Assert.Contains("_redownloadSelections.Add(recordId)", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Input", source, StringComparison.Ordinal);
        Assert.Contains("window.ConfigureRecordRow(row)", source, StringComparison.Ordinal);
        Assert.Contains("window.UpdateSmartSelectionUi()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatLiveMirror_IsImmediateWhileEvidenceRemainsEngineeringAuthorized()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatSharedProcessEvidence.cs"));

        var detach = source.IndexOf("_runtime.PointUpdated -= P0FatRuntimePointUpdated", StringComparison.Ordinal);
        var attach = source.IndexOf("_runtime.PointUpdated += P0FatRuntimePointUpdated", StringComparison.Ordinal);
        var liveProjection = source.IndexOf("ProjectSharedEngineeringPointToFat(pointIndex, point)", StringComparison.Ordinal);
        var evidence = source.IndexOf("routeOwner.PrimaryController.Enqueue(committed.Entry)", StringComparison.Ordinal);

        Assert.True(detach >= 0 && attach > detach, "Presentation-only raw LIVE mirror must be re-armed exactly after de-duplication.");
        Assert.Contains("presentation-only", source, StringComparison.OrdinalIgnoreCase);
        Assert.True(liveProjection >= 0 && evidence > liveProjection, "Engineering-authorized evidence must remain downstream of LIVE projection.");
        Assert.Contains("IsFatLiveCommitCurrent", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", source, StringComparison.Ordinal);
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
        throw new FileNotFoundException(relativePath);
    }
}
