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
        Assert.Contains("ExplorerCommandRow", xaml);
        Assert.Contains("CommandPanel_Collapsed", xaml);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("Grid.Column=\"1\" Content=\"Model\"", xaml);
        Assert.Contains("Click=\"ControlDetails_Click\"", xaml);
    }

    [Fact]
    public void ConfigurationCannotBeSentWithoutTypedExactReferenceAndUserConfirmation()
    {
        var code = Read("ControlCommandWindow.xaml.cs");
        var xaml = Read("ControlCommandWindow.xaml");
        Assert.Contains("ConfigurationConfirmationText.Trim().Equals(_signal.ObjectReference, StringComparison.Ordinal)", code);
        Assert.Contains("ConfirmConfigurationAuthorization", code);
        Assert.Contains("MessageBoxButton.YesNo", code);
        Assert.Contains("GetMethod(", code);
        Assert.Contains("\"ConfigureControlModelAsync\"", code);
        Assert.Contains("ConfigureWithExactRuntimeSignatureAsync(", code);
        Assert.Contains("if (!CanConfigureModel || SelectedModelChoice == null)", code);
        Assert.Contains("Commissioning · ctlModel (CF Write)", xaml);
        Assert.Contains("Apply ctlModel (one Write)", xaml);
        Assert.Contains("IsEnabled=\"{Binding CanConfigureModel}\"", xaml);
        Assert.DoesNotContain("Click=\"ConfigureControlModel_Click\" CommandParameter=\"Operate\"", xaml);
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
