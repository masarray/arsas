using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using Xunit;

namespace ARSAS.Tests;

public sealed class DynamicReportCommandIntentObservationTests
{
    [Fact]
    public void Bus_IsObserverOnly_AndContainsSubscriberFailures()
    {
        var device = new Iec61850MonitorDevice();
        var signal = new SignalDefinition { IsControlSignal = true, ObjectReference = "AA1Q0/CSWI1.Pos" };
        var delivered = 0;

        using var failing = DynamicReportCommandIntentObservation.Subscribe(_ => throw new InvalidOperationException("observer failure"));
        using var healthy = DynamicReportCommandIntentObservation.Subscribe(_ => delivered++);

        DynamicReportCommandIntentObservation.Publish(new DynamicReportObservedCommandIntent(
            device,
            signal,
            "Open [01]",
            "test",
            DateTimeOffset.UtcNow));

        Assert.Equal(1, delivered);
    }

    [Fact]
    public void Ui_ObservesDedicatedControlWindowBeforeExistingHandler_AndUsesV2()
    {
        var root = FindRepositoryRoot();
        var ui = File.ReadAllText(Path.Combine(root, "DynamicReportCommandBoundWitnessUiBehavior.cs"));

        Assert.Contains("typeof(Button)", ui, StringComparison.Ordinal);
        Assert.Contains("Button.ClickEvent", ui, StringComparison.Ordinal);
        Assert.Contains("ControlCommandWindow", ui, StringComparison.Ordinal);
        Assert.Contains("Send Command", ui, StringComparison.Ordinal);
        Assert.Contains("Send Test", ui, StringComparison.Ordinal);
        Assert.Contains("commandWindow.CanSend", ui, StringComparison.Ordinal);
        Assert.Contains("DynamicReportCommandIntentObservation.Publish", ui, StringComparison.Ordinal);
        Assert.Contains("DynamicReportCommandBoundStimulusWitnessServiceV2", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void V2_ListensToBothControlUiPaths_AndRemainsReadOnly()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Services", "DynamicReportCommandBoundStimulusWitnessServiceV2.cs"));

        Assert.Contains("ControlCommandBusy", source, StringComparison.Ordinal);
        Assert.Contains("DynamicReportCommandIntentObservation.Subscribe", source, StringComparison.Ordinal);
        Assert.Contains("FastCommandPanel.ControlCommandBusy", source, StringComparison.Ordinal);
        Assert.Contains("ReadSingleVariableAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteControlAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteReportControl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareDynamicRcbCommissioningFieldsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineDataSet", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteDataSet", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OriginalControlTransactionSources_AreNotInstrumented()
    {
        var root = FindRepositoryRoot();
        var dialog = File.ReadAllText(Path.Combine(root, "ControlCommandWindow.xaml.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"));
        var signal = File.ReadAllText(Path.Combine(root, "Models", "SignalDefinition.cs"));
        var adapter = File.ReadAllText(Path.Combine(root, "ControlCommandWindow.A21Witness.cs"));

        Assert.DoesNotContain("DynamicReportCommandIntentObservation", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("A21Witness", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("DynamicReportCommandIntentObservation", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("A21Witness", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("DynamicReportCommandIntentObservation", signal, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteControlAsync", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SendCommand_Click", adapter, StringComparison.Ordinal);
        Assert.Contains("A21WitnessSignal => _signal", adapter, StringComparison.Ordinal);
        Assert.Contains("A21WitnessDevice => _device", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionDynamicReporting_RemainsOff()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(root, "Services", "Iec61850MonitorRuntime.cs"));
        Assert.DoesNotContain("AllowDynamicBrcb = true", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicUrcb = true", runtime, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArIED61850Tester.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("ARSAS repository root was not found from test base directory.");
    }
}
