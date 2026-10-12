using System.IO;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P109QCommandDockCommissioningRegressionTests
{
    private static string Read(string relative)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var path = Path.Combine(current.FullName, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
            current = current.Parent;
        }
        throw new FileNotFoundException(relative);
    }

    [Theory]
    [InlineData("Raise")]
    [InlineData("Lower")]
    public void DirectionalControlMustRequireTwoIndependentUserClicks(string action)
    {
        var xaml = Read("MainWindow.xaml");
        Assert.Contains($"CommandParameter=\"{action}\" Click=\"ControlStageAction_Click\"", xaml);
        Assert.DoesNotContain($"CommandParameter=\"{action}\" Click=\"ControlQuickAction_Click\"", xaml);
        Assert.Contains("Click=\"ControlConfirmAction_Click\"", xaml);
        Assert.Contains("Click=\"ControlCancelAction_Click\"", xaml);
    }

    [Fact]
    public void ConfirmAndCancelPathUseSharedCommandOwnership()
    {
        var code = Read("MainWindow.xaml.cs");
        Assert.Contains("TryStageControlConfirmation(requestedValue, actionLabel", code);
        Assert.Contains("TryClaimControlConfirmation(out var claim", code);
        Assert.Contains("signal.ClearControlConfirmation();", code);
        Assert.Contains("await ExecuteClaimedControlAsync(signal, claim)", code);
        Assert.Contains("signal.IsRaiseLowerControl", code);
    }

    [Fact]
    public void CommandDockHasConstrainedResponsiveViewportAndInspector()
    {
        var xaml = Read("MainWindow.xaml");
        Assert.DoesNotContain("ExplorerCommandRow", xaml);
        Assert.DoesNotContain("CommandPanel_Collapsed", xaml);
        Assert.Contains("RowHeight=\"35\" MinRowHeight=\"34\"", xaml);
        Assert.Contains("Height=\"25\" MinHeight=\"25\"", xaml);
        var styles = Read("App.xaml");
        Assert.Contains("<Setter Property=\"Padding\" Value=\"8,1\"/>", styles);
        Assert.Contains("<StackPanel Margin=\"0,1\" ToolTip=\"{Binding ControlInspectionEvidence}\"", xaml);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("Grid.Column=\"1\" Content=\"Model\"", xaml);
        Assert.Contains("Click=\"ControlDetails_Click\"", xaml);
    }

    [Fact]
    public void ModelButtonOpensSimplePickAndWriteDialog()
    {
        var code = Read("MainWindow.xaml.cs");
        var ui = Read("ControlModelWriteWindow.xaml");
        var handler = Read("ControlModelWriteWindow.xaml.cs");
        Assert.Contains("new ControlModelWriteWindow(", code);
        Assert.Contains("Content=\"Write\"", ui);
        Assert.Contains("ItemsSource=\"{Binding Choices}\"", ui);
        Assert.DoesNotContain("Type the exact object reference", ui);
        Assert.DoesNotContain("I am authorized to change", ui);
        Assert.Contains("Write_Click", ui);
        Assert.Contains("ReadbackVerified", handler);
        Assert.Contains("StatusOnly", handler);
        Assert.DoesNotContain("new ControlCommandWindow(", code);
    }

    [Fact]
    public void StatusOnlyZeroIsAnExplicitWritableControlModel()
    {
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        var editor = Read("ControlModelWriteWindow.xaml.cs");
        Assert.Contains("target is not (Iec61850ControlModelKind.StatusOnly or", runtime);
        Assert.Contains("new(Iec61850ControlModelKind.StatusOnly, \"0  [status-only]\")", editor);
    }

    [Fact]
    public void OnlyEngineOwnsSingleControlModelWriteAndInvalidatesOldDescriptor()
    {
        var client = Read("Services/NativeIec61850Client.cs");
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("InvalidateCachedControlSessionAsync(signal.ObjectReference)", client);
        Assert.Contains("signal.ClearLiveControlAuthority();", client);
        Assert.Contains("signal.ControlModelText = \"Auto-detect\";", client);
        Assert.Contains("OPERATOR_CTL_MODEL_CONFIGURATION", runtime);
        Assert.Contains("_activeControlTargets.TryAdd(targetKey, 0)", runtime);
        Assert.Contains("Interlocked.Increment(ref session.ControlCommandActive)", runtime);
    }
}
