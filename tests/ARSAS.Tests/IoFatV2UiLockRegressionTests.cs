using System.Globalization;
using ArIED61850Tester.Models.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatV2UiLockRegressionTests
{
    [Fact]
    public void RuntimeGrid_LocksOnlyTheIedThatOwnsPreparationOrEvidenceScope()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.FatV2Ux.cs"));

        Assert.DoesNotContain("DataContext.CanEditPlan", source, StringComparison.Ordinal);
        Assert.Contains("public bool SelectedCanEditPlan => SelectedIed is not null && CanEditIedPlan(SelectedIed);", source, StringComparison.Ordinal);
        Assert.Contains("!ied.IsPreparing", source, StringComparison.Ordinal);
        Assert.Contains("!Session.IsSessionActive || !ReferenceEquals(Session.ActiveIed, ied)", source, StringComparison.Ordinal);

        // The TEST checkbox binds directly to the selected IED and session owner so the
        // owning IED locks synchronously; another IED remains editable without waiting for
        // a dispatcher-level window-property refresh.
        Assert.Contains("var editability = new MultiBinding", source, StringComparison.Ordinal);
        Assert.Contains("DataContext.SelectedIed", source, StringComparison.Ordinal);
        Assert.Contains("DataContext.Session.ActiveIed", source, StringComparison.Ordinal);
        Assert.Contains("DataContext.Session.IsSessionActive", source, StringComparison.Ordinal);
        Assert.Contains("DataContext.SelectedIed.IsPreparing", source, StringComparison.Ordinal);
        Assert.Contains("IoFatSelectedIedPlanEditabilityConverter", source, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(activeIed, selectedIed)", source, StringComparison.Ordinal);

        Assert.Contains("if (!CanEditPointPlan(point))", source, StringComparison.Ordinal);
        Assert.Contains("point.RemoveFromFat();", source, StringComparison.Ordinal);

        // Operator snapshot capture remains the intentional evidence action and is not
        // disabled by the TEST-checkbox edit lock.
        Assert.Contains("CanCaptureOperatorSnapshot", source, StringComparison.Ordinal);
        Assert.Contains("Session.CaptureOperatorSnapshot(point, slot)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EditabilityConverter_AllowsOtherIedWhileActiveOwnerRemainsLocked()
    {
        var activeIed = new IoTestIedPlan { IedName = "IED-A", IpAddress = "192.0.2.10" };
        var otherIed = new IoTestIedPlan { IedName = "IED-B", IpAddress = "192.0.2.11" };
        var converter = new ArIED61850Tester.IoFatSelectedIedPlanEditabilityConverter();

        bool Evaluate(IoTestIedPlan selected, IoTestIedPlan? active, bool sessionActive, bool preparing)
            => Assert.IsType<bool>(converter.Convert(
                new object[] { selected, active!, sessionActive, preparing },
                typeof(bool),
                null!,
                CultureInfo.InvariantCulture));

        Assert.False(Evaluate(activeIed, activeIed, sessionActive: true, preparing: false));
        Assert.True(Evaluate(otherIed, activeIed, sessionActive: true, preparing: false));
        Assert.False(Evaluate(otherIed, activeIed, sessionActive: true, preparing: true));
        Assert.True(Evaluate(activeIed, active: null, sessionActive: false, preparing: false));
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
