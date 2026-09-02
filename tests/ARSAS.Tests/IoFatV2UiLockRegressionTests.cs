namespace ARSAS.Tests;

public sealed class IoFatV2UiLockRegressionTests
{
    [Fact]
    public void RuntimeGrid_LocksOnlyTheIedThatOwnsPreparationOrEvidenceScope()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));

        Assert.Contains("DataContext.SelectedCanEditPlan", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext.CanEditPlan", source, StringComparison.Ordinal);
        Assert.Contains("public bool SelectedCanEditPlan => SelectedIed is not null && CanEditIedPlan(SelectedIed);", source, StringComparison.Ordinal);
        Assert.Contains("!ied.IsPreparing", source, StringComparison.Ordinal);
        Assert.Contains("!Session.IsSessionActive || !ReferenceEquals(Session.ActiveIed, ied)", source, StringComparison.Ordinal);
        Assert.Contains("if (!CanEditPointPlan(point))", source, StringComparison.Ordinal);
        Assert.Contains("point.RemoveFromFat();", source, StringComparison.Ordinal);

        // Operator snapshot capture remains the intentional evidence action and is not
        // disabled by the TEST-checkbox edit lock.
        Assert.Contains("CanCaptureOperatorSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("Session.CaptureOperatorSnapshot(point, slot)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedSignals_RestoreLockIsEvaluatedPerOwningIed()
    {
        var workspace = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));
        var removed = File.ReadAllText(FindRepoFile("RemovedFatSignalsWindow.cs"));

        Assert.Contains("new RemovedFatSignalsWindow(Project, CanEditPointPlan)", workspace, StringComparison.Ordinal);
        Assert.Contains("Func<IoTestPointPlan, bool>? canEditPoint", removed, StringComparison.Ordinal);
        Assert.Contains("canEditPoint?.Invoke(point) ?? true", removed, StringComparison.Ordinal);
        Assert.Contains("new Binding(nameof(RemovedSignalRow.CanEdit))", removed, StringComparison.Ordinal);
        Assert.Contains("_view.Cast<RemovedSignalRow>().Where(row => row.CanEdit)", removed, StringComparison.Ordinal);
        Assert.Contains("_rows.Where(row => row.CanEdit && row.IsSelected)", removed, StringComparison.Ordinal);
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
