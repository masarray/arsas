using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using Xunit;

namespace ARSAS.Tests;

public sealed class DynamicReportCommandRuntimeDiagnosticWitnessTests
{
    [Fact]
    public void RuntimeDiagnostic_ResolvesExactCommandSignal()
    {
        var device = new Iec61850MonitorDevice { Name = "AA1C1F08R4" };
        var signal = new SignalDefinition
        {
            IsControlSignal = true,
            ObjectReference = "AA1C1F08R4Q0/CSWI1.Pos",
            ControlStatusReference = "AA1C1F08R4Q0/XCBR1.Pos.stVal"
        };
        var entry = new DiagnosticEntry
        {
            Source = "AA1C1F08R4",
            Message = "Control execution requested: AA1C1F08R4Q0/CSWI1.Pos value=Open [01]; test=False; interlock=True; synchro=False; origin=StationControl/; IED acceptance is determined only from native MMS wire evidence."
        };

        var ok = DynamicReportCommandBoundStimulusWitnessServiceV3.TryBuildRuntimeIntent(
            entry,
            device,
            [signal],
            out var intent);

        Assert.True(ok);
        Assert.NotNull(intent);
        Assert.Same(signal, intent!.Signal);
        Assert.Equal("Open [01]", intent.RequestedValue);
        Assert.Equal(DynamicReportCommandBoundStimulusWitnessServiceV3.RuntimeDiagnosticSource, intent.Source);
    }

    [Fact]
    public void RuntimeDiagnostic_RejectsDifferentIed()
    {
        var device = new Iec61850MonitorDevice { Name = "AA1C1F08R4" };
        var signal = new SignalDefinition
        {
            IsControlSignal = true,
            ObjectReference = "AA1C1F08R4Q0/CSWI1.Pos",
            ControlStatusReference = "AA1C1F08R4Q0/XCBR1.Pos.stVal"
        };
        var entry = new DiagnosticEntry
        {
            Source = "OTHER_IED",
            Message = "Control execution requested: AA1C1F08R4Q0/CSWI1.Pos value=Closed [10]; test=False; interlock=True; synchro=False; origin=StationControl/."
        };

        Assert.False(DynamicReportCommandBoundStimulusWitnessServiceV3.TryBuildRuntimeIntent(
            entry,
            device,
            [signal],
            out _));
    }

    [Fact]
    public void RuntimeDiagnostic_RejectsNonControlDiagnostic()
    {
        var device = new Iec61850MonitorDevice { Name = "AA1C1F08R4" };
        var signal = new SignalDefinition
        {
            IsControlSignal = true,
            ObjectReference = "AA1C1F08R4Q0/CSWI1.Pos",
            ControlStatusReference = "AA1C1F08R4Q0/XCBR1.Pos.stVal"
        };
        var entry = new DiagnosticEntry
        {
            Source = "AA1C1F08R4",
            Message = "Control inspected: AA1C1F08R4Q0/CSWI1.Pos; model=SBO enhanced."
        };

        Assert.False(DynamicReportCommandBoundStimulusWitnessServiceV3.TryBuildRuntimeIntent(
            entry,
            device,
            [signal],
            out _));
    }

    [Fact]
    public void V3_ObservesExistingDiagnosticEvent_WithoutInstrumentingRuntimeControlSource()
    {
        var root = FindRepositoryRoot();
        var v3 = File.ReadAllText(Path.Combine(root, "Services", "DynamicReportCommandBoundStimulusWitnessServiceV3.cs"));
        var runtime = File.ReadAllText(Path.Combine(root, "Services", "Iec61850MonitorRuntime.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "MainWindow.xaml.cs"));
        var adapter = File.ReadAllText(Path.Combine(root, "MainWindow.A21Witness.cs"));
        var ui = File.ReadAllText(Path.Combine(root, "DynamicReportCommandBoundWitnessUiBehavior.cs"));

        Assert.Contains("runtime.Diagnostic += RuntimeDiagnosticHandler", v3, StringComparison.Ordinal);
        Assert.Contains("Control execution requested:", v3, StringComparison.Ordinal);
        Assert.Contains("A21WitnessRuntime", adapter, StringComparison.Ordinal);
        Assert.Contains("DynamicReportCommandBoundStimulusWitnessServiceV3", ui, StringComparison.Ordinal);
        Assert.Contains("window.A21WitnessRuntime", ui, StringComparison.Ordinal);

        Assert.DoesNotContain("DynamicReportCommandBoundStimulusWitnessServiceV3", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("DynamicReportCommandIntentObservation", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("A21Witness", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("A21Witness", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void V3_RemainsReportingReadOnlyAndProductionDynamicOff()
    {
        var root = FindRepositoryRoot();
        var v3 = File.ReadAllText(Path.Combine(root, "Services", "DynamicReportCommandBoundStimulusWitnessServiceV3.cs"));
        var runtime = File.ReadAllText(Path.Combine(root, "Services", "Iec61850MonitorRuntime.cs"));

        Assert.DoesNotContain("WriteReportControl", v3, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareDynamicRcbCommissioningFieldsAsync", v3, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitor", v3, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineDataSet", v3, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteDataSet", v3, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", v3, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible", v3, StringComparison.Ordinal);
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
