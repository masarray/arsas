using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P109SRefreshControlDescriptorRegressionTests
{
    [Fact]
    public void ReopeningDockDoesNotReprobeVerifiedStatusOnlyOrBusyControls()
    {
        // A semantic CDC cannot be validated on a free-floating test row.
        // Use a real CO DataObject identity; otherwise the production safety
        // contract correctly rejects AcceptVerifiedLiveControlCdc().
        var signal = new SignalDefinition
        {
            ObjectReference = "LD0/CSWI1.Pos",
            Name = "CSWI1.Pos",
            FunctionalConstraint = "CO",
            IsControlSignal = true,
            IsSelected = true,
            ControlCurrentValue = "-"
        };
        Assert.True(Iec61850ControlIdentity.NeedsInspection(signal, false));
        signal.ControlModelText = "Status only";
        Assert.True(signal.ControlModelResolved);
        Assert.False(Iec61850ControlIdentity.NeedsInspection(signal, false));
        Assert.True(Iec61850ControlIdentity.NeedsInspection(signal, true));
        signal.ControlInspectionBusy = true;
        Assert.False(Iec61850ControlIdentity.NeedsInspection(signal, true));
        signal.ControlInspectionBusy = false;
        signal.ControlModelText = "Direct Operate (DO) • Normal security";
        signal.AcceptVerifiedLiveControlCdc("DPC");
        signal.ControlSemanticVerified = true;
        Assert.True(signal.TryBeginDirectControlCommand("Close", "Close", out _, out _));
        Assert.False(Iec61850ControlIdentity.NeedsInspection(signal, true));
    }

    [Fact]
    public void InspectRebuildsCachedDescriptorBeforeControlModelAndTypedSemanticEvaluation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,"Services","NativeIec61850Client.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code=File.ReadAllText(Path.Combine(root!.FullName,"Services","NativeIec61850Client.cs"));
        var start=code.IndexOf("private async Task<Iec61850ControlCapabilities> InspectControlCoreAsync(",StringComparison.Ordinal);
        var inspect=code[start..code.IndexOf("public async Task<Iec61850ControlCommandResult> ExecuteControlAsync(",start,StringComparison.Ordinal)];
        var wrapper=code[code.IndexOf("public async Task<Iec61850ControlCapabilities> InspectControlAsync(",StringComparison.Ordinal)..start];
        Assert.Contains("await _controlCommandGate.WaitAsync(cancellationToken)",wrapper);
        Assert.Contains("_controlCommandGate.Release();",wrapper);
        Assert.Contains("signal.ClearLiveControlAuthority();",inspect);
        var opened=inspect.IndexOf("control = await GetOrOpenControlSessionAsync(",StringComparison.Ordinal);
        var invalidated=inspect.IndexOf("await InvalidateCachedControlSessionAsync(signal.ObjectReference)",StringComparison.Ordinal);
        Assert.True(invalidated>=0 && opened>invalidated);
        Assert.Contains("ControlSemanticVerified = fullyVerified",inspect);
        Assert.Contains("HasExactDpcBooleanFeedback(descriptor, effectiveCdc)",inspect);
        Assert.Contains("IsOperationallyReady = fullyVerified",inspect);
        Assert.Contains("ControlModelText = modelText",inspect);
        var main=File.ReadAllText(Path.Combine(root.FullName,"MainWindow.xaml.cs"));
        Assert.Contains("await ReinspectConfiguredControlAsync(device, signal, dialog.WriteAssociation);", main);
        Assert.DoesNotContain("await RefreshControlValuesAsync(device, force: true);", main);
    }

    [Fact]
    public void LiveModelMustNotAuthorizeOperateWithoutMatchingTypedSemantics()
    {
        var signal=new SignalDefinition {Name="CSWI1.Pos",ObjectReference="ZIVAVRLD1/CSWI1.Pos",
            FunctionalConstraint="CO",IsControlSignal=true,ControlCdc="DPC"};
        signal.ControlModelText="Direct Operate (DO) • Normal security";
        Assert.True(signal.ControlModelResolved);
        Assert.False(signal.ControlSupportsOperate);
        signal.AcceptVerifiedLiveControlCdc("DPC");
        signal.ControlSemanticVerified=true;
        Assert.True(signal.ControlSupportsOperate);
        signal.ControlSemanticVerified=false;
        Assert.False(signal.ControlSupportsOperate);
    }
}
