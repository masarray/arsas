using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P109SRefreshControlDescriptorRegressionTests
{
    [Fact]
    public void InspectRebuildsCachedDescriptorBeforeControlModelAndTypedSemanticEvaluation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,"Services","NativeIec61850Client.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code=File.ReadAllText(Path.Combine(root!.FullName,"Services","NativeIec61850Client.cs"));
        var start=code.IndexOf("public async Task<Iec61850ControlCapabilities> InspectControlAsync(",StringComparison.Ordinal);
        var inspect=code[start..code.IndexOf("public async Task<Iec61850ControlCommandResult> ExecuteControlAsync(",start,StringComparison.Ordinal)];
        var opened=inspect.IndexOf("control = await GetOrOpenControlSessionAsync(",StringComparison.Ordinal);
        var invalidated=inspect.IndexOf("await InvalidateCachedControlSessionAsync(signal.ObjectReference)",StringComparison.Ordinal);
        Assert.True(invalidated>=0 && opened>invalidated);
        Assert.Contains("ControlSemanticVerified = semantic.IsCommandSafe",inspect);
        Assert.Contains("ControlModelText = modelText",inspect);
        var main=File.ReadAllText(Path.Combine(root.FullName,"MainWindow.xaml.cs"));
        Assert.Contains("await RefreshControlValuesAsync(device, force: true);",main);
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
