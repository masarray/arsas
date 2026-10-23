namespace ARSAS.Tests;

public sealed class P0FieldBenchRound2RegressionTests
{
    [Fact]
    public void FatSignalColumn_UsesDeterministicPhaseAwareTemplateAfterV2Rebuild()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.SemanticSignalColumnAuthority.cs"));

        Assert.Contains("Window.GetWindow(grid) is not IoListTestingWindow", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Loaded", source, StringComparison.Ordinal);
        Assert.Contains("column.CellTemplate = BuildSemanticFatSignalTemplate()", source, StringComparison.Ordinal);
        Assert.Contains("IoFatSignalDisplayNameFormatter.Format(point)", source, StringComparison.Ordinal);
        Assert.Contains("new Binding(\".\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RcbProductionPointer_OpensMultiExporterAndUsesOneIndependentSelectionAuthority()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.RcbMultiSelectPointerAuthority.cs"));

        Assert.Contains("PreviewMouseLeftButtonDownEvent", source, StringComparison.Ordinal);
        Assert.Contains("PreviewKeyDownEvent", source, StringComparison.Ordinal);
        Assert.Contains("window.IedEditRcbMulti_Click(button, e)", source, StringComparison.Ordinal);
        Assert.Contains("Window.GetWindow(grid) is not RcbMultiExportWindow", source, StringComparison.Ordinal);
        Assert.Contains("ApplyRcbPointerSelection(", source, StringComparison.Ordinal);
        Assert.Contains("ApplyRcbSelectionForTest(", source, StringComparison.Ordinal);
        Assert.Contains("rows[targetIndex].IsSelected = !rows[targetIndex].IsSelected", source, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.Shift", source, StringComparison.Ordinal);
        Assert.Contains("e.Key != Key.Space", source, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadedComtrade_SelectionTickUsesRowAuthorityAndRepaintsAfterPreviewInput()
    {
        var source = File.ReadAllText(FindRepoFile("FaultRecordWindow.RedownloadSelectionAuthority.cs"));

        Assert.Contains("row.IsSelected = !row.IsSelected", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Input", source, StringComparison.Ordinal);
        Assert.Contains("window.ConfigureRecordRow(row)", source, StringComparison.Ordinal);
        Assert.Contains("window.UpdateSmartSelectionUi()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_redownloadSelections", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatLiveMirror_IsImmediateWhileEvidenceUsesMonotonicEngineeringBarrier()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatSharedProcessEvidence.cs"));

        var detach = source.IndexOf("_runtime.PointUpdated -= P0FatRuntimePointUpdated", StringComparison.Ordinal);
        var attach = source.IndexOf("_runtime.PointUpdated += P0FatRuntimePointUpdated", StringComparison.Ordinal);
        var liveProjection = source.IndexOf("ProjectSharedEngineeringPointToFat(pointIndex, point)", StringComparison.Ordinal);
        var sequenceCommit = source.IndexOf("_p0FatLiveVisibleSequences[plan.TestPointId] = processSequence", liveProjection, StringComparison.Ordinal);
        var evidence = source.IndexOf("coordinator.PrimaryController.Enqueue(committed.Entry)", sequenceCommit, StringComparison.Ordinal);

        Assert.True(detach >= 0 && attach > detach, "Presentation-only raw LIVE mirror must be re-armed exactly after de-duplication.");
        Assert.Contains("presentation-only", source, StringComparison.OrdinalIgnoreCase);
        Assert.True(liveProjection >= 0 && sequenceCommit > liveProjection && evidence > sequenceCommit,
            "Engineering-authorized evidence must remain downstream of LIVE projection and its publication epoch.");
        Assert.Contains("IsFatLiveCommitCurrent", source, StringComparison.Ordinal);
        Assert.Contains("CanPublishEvidenceForTest", source, StringComparison.Ordinal);
        Assert.Contains("liveVisibleSequence >= evidenceProcessSequence", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.Background", source, StringComparison.Ordinal);
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
