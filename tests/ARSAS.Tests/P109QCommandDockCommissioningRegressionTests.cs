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
    public void RaiseAndLowerUseSharedSemanticPalette_AndConfirmTracksPendingAction()
    {
        var xaml = Read("MainWindow.xaml");
        var app = Read("App.xaml");
        var start = xaml.IndexOf("<DataGridTemplateColumn Header=\"Control\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("</DataGridTemplateColumn>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var cell = xaml[start..end];

        // Both the combined BSC and separate TapOpR/TapOpL paths must
        // have equally prominent, distinct colored command surfaces.
        Assert.Equal(2, cell.Split("Style=\"{StaticResource CommandRaiseButton}\"",
            StringSplitOptions.None).Length - 1);
        Assert.Equal(2, cell.Split("Style=\"{StaticResource CommandLowerButton}\"",
            StringSplitOptions.None).Length - 1);
        Assert.Contains("Style=\"{StaticResource CommandConfirmButton}\"", cell);
        Assert.Contains("Style=\"{StaticResource CommandDockSoftButton}\"", cell); // Cancel

        foreach (var resource in new[]
        {
            "CommandRaiseFill", "CommandRaiseStroke",
            "CommandLowerFill", "CommandLowerStroke", "CommandLowerInk"
        })
            Assert.Contains($"x:Key=\"{resource}\"", app);

        var confirmStart = app.IndexOf("<Style x:Key=\"CommandConfirmButton\"", StringComparison.Ordinal);
        var confirmEnd = app.IndexOf("</Style>", confirmStart, StringComparison.Ordinal);
        Assert.True(confirmStart >= 0 && confirmEnd > confirmStart);
        var confirm = app[confirmStart..confirmEnd];
        foreach (var (action, resource) in new[]
        {
            ("Raise", "CommandRaiseFill"), ("Lower", "CommandLowerFill"),
            ("Close", "CommandCloseGradient")
        })
        {
            Assert.Contains($"Binding ControlPendingAction}}\" Value=\"{action}\"", confirm);
            Assert.Contains($"StaticResource {resource}", confirm);
        }
        // Open remains default success green; Close remains red.
        Assert.Contains("StaticResource CommandOpenGradient", confirm);
        Assert.Contains("StaticResource CommandCloseGradient", confirm);
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
        Assert.Contains("RowHeight=\"43\" MinRowHeight=\"42\"", xaml);
        Assert.Contains("Height=\"29\" MinHeight=\"29\"", xaml);
        var styles = Read("App.xaml");
        Assert.Contains("x:Key=\"CommandDockPrimaryButton\"", styles);
        Assert.Contains("x:Key=\"CommandDockSoftButton\"", styles);
        var start = styles.IndexOf("x:Key=\"CommandActionButtonBase\"", StringComparison.Ordinal);
        var end = styles.IndexOf("x:Key=\"CommandOpenButton\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.DoesNotContain("DropShadowEffect", styles[start..end]);
        Assert.Contains("<StackPanel Margin=\"0,0\">", xaml);
        Assert.DoesNotContain("<StackPanel Margin=\"0,0\" ToolTip=\"{Binding ControlInspectionEvidence}\"", xaml);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("Grid.Column=\"1\" Content=\"Model\"", xaml);
        Assert.Contains("Click=\"ControlDetails_Click\"", xaml);
    }

    [Fact]
    public void PositionButtonsRequestTypedPreflightInsteadOfBeingPermanentlyDisabled()
    {
        var xaml = Read("MainWindow.xaml");
        var code = Read("MainWindow.xaml.cs");
        Assert.Contains("IsEnabled=\"{Binding ControlCanInspectPosition}\"", xaml);
        Assert.Contains("private async void ControlStageAction_Click(", code);
        Assert.Contains("if (signal.IsPositionControl && !signal.ControlSemanticVerified)", code);
        Assert.Contains("var capabilities = await _runtime.InspectControlAsync(", code);
        Assert.Contains("!capabilities.IsOperationallyReady || !signal.ControlSupportsOperate", code);
        Assert.Contains("signal.TryStageControlConfirmation(", code);
        var start = xaml.IndexOf("<DataGridTemplateColumn Header=\"Control\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("</DataGridTemplateColumn>", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var control = xaml[start..end];
        Assert.DoesNotContain("ToolTip=\"{Binding ControlInspectionEvidence}\"", control);
        Assert.DoesNotContain("ToolTipService.ShowOnDisabled", control);
        Assert.DoesNotContain("ToolTip=\"Read or write ctlModel", control);
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
        Assert.DoesNotContain("GetMethod(", handler);
        Assert.Contains("Iec61850ControlModelWriteDispatcher.ConfigureAsync(", handler);
        var gateway = Read("Services/Iec61850ControlModelWriteDispatcher.cs");
        Assert.Contains("runtime.ConfigureControlModelAsync(deviceId, signal, expected, requested, cancellationToken)", gateway);
        Assert.DoesNotContain("GetMethod(", gateway);
        Assert.Contains("global::ArIED61850Tester.Iec61850MonitorRuntime runtime", gateway);
        var facade = Read("Services/UiResponsiveIec61850MonitorRuntimeFacade.cs");
        Assert.Contains("public Task<AR.Iec61850.Control.Iec61850ControlModelConfigurationResult> ConfigureControlModelAsync(", facade);
        Assert.Contains("_inner.ConfigureControlModelAsync(deviceId, signal, expected, requested, token)", facade);
        Assert.Contains("RunDeviceOperationAsync(", facade);
        Assert.Contains("Background=\"#F4F7FB\"", ui);
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
        // A single atomic IED-wide flight now protects ctlModel Write and
        // ordinary Operate. Increment-and-reject was not a safe admission gate.
        Assert.Contains("Iec61850ControlFlightAdmission.TryEnter(", runtime);
        Assert.Contains("CONTROL_OUTCOME_UNKNOWN_TIMEOUT", runtime);
    }
}
