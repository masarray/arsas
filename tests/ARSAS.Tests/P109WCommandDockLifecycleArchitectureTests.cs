using System;
using System.IO;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P109WCommandDockLifecycleArchitectureTests
{
    private static string Source(string relative)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null)
        {
            var file = Path.Combine(root.FullName, relative);
            if (File.Exists(file))
                return File.ReadAllText(file);
            root = root.Parent;
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void DiagnosticProseCannotAuthorizeOrRevokeTypedCtlModel()
    {
        var row = new SignalDefinition
        {
            ObjectReference = "LD0/CSWI1.Pos",
            IsControlSignal = true,
            IsSelected = true,
            FunctionalConstraint = "CO",
            ControlCdc = "DPC"
        };
        Assert.False(row.ControlModelResolved);
        row.ControlLastResult = "ctlModel=4; SBO enhanced accepted";
        Assert.False(row.ControlModelResolved);
        Assert.False(row.ControlSupportsOperate);

        row.ControlModelText = "Select Before Operate (SBO) • Enhanced security";
        row.AcceptVerifiedLiveControlCdc("DPC");
        row.ControlSemanticVerified = true;
        Assert.True(row.ControlSupportsOperate);
        row.ControlLastResult = "ctlModel=0; diagnostic text only";
        Assert.True(row.ControlSupportsOperate);
        Assert.Equal(Iec61850ControlModelKind.SboEnhanced, row.ControlModelKind);

        row.ClearLiveControlAuthority();
        Assert.False(row.ControlSupportsOperate);
    }

    [Fact]
    public void RealizedRowsAreInitializedByEventsWithoutPollingVisualTree()
    {
        var ux = Source("MainWindow.CommandPanelUx.cs");
        var natural = Source("MainWindow.IecNaturalSort.cs");
        Assert.DoesNotContain("_commandPanelUxTimer", ux, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandPanelUxTimer_Tick", ux, StringComparison.Ordinal);
        Assert.Contains("grid.LoadingRow += CommandGrid_LoadingRow;", ux, StringComparison.Ordinal);
        Assert.Contains("row.Loaded += CommandGridRow_Loaded;", ux, StringComparison.Ordinal);
        Assert.Contains("foreach (var row in FindVisualChildren<DataGridRow>(grid))", ux, StringComparison.Ordinal);
        Assert.Contains("_configuredCommandRows.TryGetValue", ux, StringComparison.Ordinal);
        Assert.Contains("EnsureDefaultControlSafetyChecks(signal)", ux, StringComparison.Ordinal);
        Assert.Contains("InstallCommandGridBehavior(grid);", natural, StringComparison.Ordinal);
        Assert.Contains("AttachIecNaturalGridSort(grid,", natural, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationIsNotAnMmsReadTrigger_AndDockKeepsOneState()
    {
        var main = Source("MainWindow.xaml.cs");
        var start = main.IndexOf("private void CommandPanel_Expanded(", StringComparison.Ordinal);
        var stop = main.IndexOf("private void CommandPanel_Collapsed(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && stop > start);
        var expanded = main[start..stop];
        Assert.DoesNotContain("RefreshControlValuesAsync", expanded, StringComparison.Ordinal);
        Assert.DoesNotContain("PreloadControlModelsAsync", expanded, StringComparison.Ordinal);

        var shell = Source("MainWindow.PersistentWorkbench.cs");
        Assert.Contains("public bool DockExpanded { get; set; }", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("DockExpandedByWorkspace", shell, StringComparison.Ordinal);
        Assert.Contains("CommandPanelExpander.IsExpanded == state.DockExpanded", shell, StringComparison.Ordinal);
        Assert.Contains("state.DockExpanded = expanded;", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void PreflightIsSerializedCoalescedAndResetsWithNewAssociation()
    {
        var ux = Source("MainWindow.CommandPanelUx.cs");
        var main = Source("MainWindow.xaml.cs");
        Assert.Contains("private bool _controlModelPreloadPending;", ux, StringComparison.Ordinal);
        Assert.Contains("while (_controlModelPreloadPending", ux, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.WhenAll(candidates.Select", ux, StringComparison.Ordinal);
        Assert.Contains("ResetControlPreloadAttempts(device);", main, StringComparison.Ordinal);
        Assert.Contains("_controlModelPreloadAttempts.RemoveWhere", ux, StringComparison.Ordinal);
        Assert.Contains("_ = PreloadControlModelsAsync();", main, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveInspectionBusyRejectsAllCommandClaimsUntilTypedReadFinishes()
    {
        var row = new SignalDefinition
        {
            ObjectReference = "LD0/CSWI1.Pos",
            IsControlSignal = true,
            IsSelected = true,
            FunctionalConstraint = "CO",
            ControlCdc = "DPC",
            ControlModelText = "Select Before Operate (SBO) • Enhanced security"
        };
        row.AcceptVerifiedLiveControlCdc("DPC");
        row.ControlSemanticVerified = true;
        Assert.True(row.TryStageControlConfirmation("Closed [10]", "Close", out _));
        row.ControlInspectionBusy = true;
        Assert.False(row.ControlCanConfirm);
        Assert.False(row.TryClaimControlConfirmation(out _, out var pendingReason));
        Assert.Contains("being verified", pendingReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(row.TryStageControlConfirmation("Open [01]", "Open", out _));
        Assert.False(row.TryBeginDirectControlCommand("Closed [10]", "Close", out _, out _));
        row.ControlInspectionBusy = false;
        Assert.True(row.ControlCanConfirm);
    }

    [Fact]
    public void FirstClickOpenCloseStillUsesReadOnlyModelPreflight()
    {
        var source = Source("MainWindow.CommandPanelUx.cs");
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("if (content is \"Open\" or \"Close\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("or \"Model\"", source, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding ControlCanInspectPosition}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("var enabledBinding = new MultiBinding", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectionPublishesTypedAuthorizationOnlyAfterFinalMmsReads()
    {
        var client = Source("Services/NativeIec61850Client.cs");
        var start = client.IndexOf("private async Task<Iec61850ControlCapabilities> InspectControlCoreAsync(", StringComparison.Ordinal);
        var stop = client.IndexOf("public async Task<Iec61850ControlCommandResult> ExecuteControlAsync(", start, StringComparison.Ordinal);
        Assert.True(start > 0 && stop > start);
        var inspect = client[start..stop];
        var invalidate = inspect.IndexOf("await InvalidateCachedControlSessionAsync(", StringComparison.Ordinal);
        var typed = inspect.IndexOf("var semantic = ArControl.Iec61850ControlSemanticResolver.Resolve(", StringComparison.Ordinal);
        var finalCommit = inspect.LastIndexOf("signal.ClearLiveControlAuthority();", StringComparison.Ordinal);
        var authorize = inspect.IndexOf("signal.ControlSemanticVerified = semantic.IsCommandSafe && descriptor.IsOperationallyReady;", StringComparison.Ordinal);
        Assert.True(invalidate >= 0 && typed > invalidate && finalCommit > typed && authorize > finalCommit);
        Assert.Contains("signal.ControlModelText = probe.Decoded", inspect, StringComparison.Ordinal);
        Assert.Contains("signal.ControlModelText = modelText;", inspect, StringComparison.Ordinal);
        Assert.DoesNotContain("signal.ClearLiveControlAuthority();\n            signal.ControlModelText = \"Auto-detect\";\n            await Invalidate", inspect, StringComparison.Ordinal);
    }
}
