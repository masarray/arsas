using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using AR.Iec61850.Mms;
using Xunit;

namespace ARSAS.Tests;

public sealed class DynamicReportCommandBoundStimulusWitnessServiceTests
{
    [Fact]
    public void Contract_IsBoundedAndHighSpeed()
    {
        Assert.Equal(6, DynamicReportCommandBoundStimulusWitnessService.MaximumFocusCandidates);
        Assert.Equal(128, DynamicReportCommandBoundStimulusWitnessService.MaximumPreCommandBaselinePoints);
        Assert.Equal("G2.5-A2.1 READY — ISSUE ONE ARSAS COMMAND", DynamicReportCommandBoundStimulusWitnessService.ReadyMarker);
        Assert.True(DynamicReportCommandBoundStimulusWitnessService.FocusObservationWindow <= TimeSpan.FromSeconds(5));
        Assert.True(DynamicReportCommandBoundStimulusWitnessService.InterCycleDelay <= TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void ResolveCommandStatusPoints_UsesExactControlStatusReference()
    {
        var status = Point("AA1Q0", "XCBR1", "ST", "Pos.stVal", "XCBR1$ST$Pos$stVal");
        var unrelated = Point("AA1Q8", "XCBR1", "ST", "Pos.stVal", "XCBR1$ST$Pos$stVal");
        var directory = new MmsIedModelDirectory([status, unrelated]);
        var signal = new SignalDefinition
        {
            IsControlSignal = true,
            ObjectReference = "AA1Q0/CSWI1.Pos",
            ControlStatusReference = "AA1Q0/XCBR1.Pos.stVal"
        };

        var resolved = DynamicReportCommandBoundStimulusWitnessService.ResolveCommandStatusPoints(directory, [signal]);

        Assert.True(resolved.TryGetValue(signal, out var point));
        Assert.Equal("AA1Q0/XCBR1$ST$Pos$stVal", point!.MmsReference);
    }

    [Fact]
    public void BuildFocusChain_StartsWithExactStatus_AndStaysBounded()
    {
        var exact = Point("AA1Q0", "XCBR1", "ST", "Pos.stVal", "XCBR1$ST$Pos$stVal");
        var cswi = Point("AA1Q0", "CSWI1", "ST", "Pos.stVal", "CSWI1$ST$Pos$stVal");
        var xswi = Point("AA1Q0", "XSWI1", "ST", "Pos.stVal", "XSWI1$ST$Pos$stVal");
        var openPulse = Point("AA1ADD", "GGIO2", "ST", "CBOpnCmdRecv.stVal", "GGIO2$ST$CBOpnCmdRecv$stVal");
        var closePulse = Point("AA1ADD", "GGIO2", "ST", "CBClsCmdRecv.stVal", "GGIO2$ST$CBClsCmdRecv$stVal");
        var localOpen = Point("AA1ADD", "GGIO1", "ST", "LocOpnCMDsta.stVal", "GGIO1$ST$LocOpnCMDsta$stVal");
        var localClose = Point("AA1ADD", "GGIO1", "ST", "LocClsCMDsta.stVal", "GGIO1$ST$LocClsCMDsta$stVal");
        var directory = new MmsIedModelDirectory([exact, cswi, xswi, openPulse, closePulse, localOpen, localClose]);

        var focus = DynamicReportCommandBoundStimulusWitnessService.BuildFocusChain(directory, exact);

        Assert.NotEmpty(focus);
        Assert.Equal(exact.MmsReference, focus[0].MmsReference);
        Assert.True(focus.Count <= DynamicReportCommandBoundStimulusWitnessService.MaximumFocusCandidates);
        Assert.Contains(focus, point => point.MmsReference == cswi.MmsReference);
        Assert.Contains(focus, point => point.MmsReference.Contains("CmdRecv", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Classify_PersistentTransition_IsLatched()
    {
        var transitions = new[]
        {
            Transition("bits(80, unused=6)", "bits(40, unused=6)", 0)
        };

        var kind = DynamicReportCommandBoundStimulusWitnessService.Classify(
            "bits(80, unused=6)",
            "bits(40, unused=6)",
            transitions);

        Assert.Equal(DynamicReportStimulusEligibilityKind.PersistentOrLatched, kind);
    }

    [Fact]
    public void Classify_ReturnToBaseline_IsPulse()
    {
        var transitions = new[]
        {
            Transition("false", "true", 0),
            Transition("true", "false", 50)
        };

        var kind = DynamicReportCommandBoundStimulusWitnessService.Classify("false", "false", transitions);

        Assert.Equal(DynamicReportStimulusEligibilityKind.MomentaryOrPulse, kind);
    }

    [Fact]
    public void Source_IsReadOnlyAndDetectsExistingControlBusySignal()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Services", "DynamicReportCommandBoundStimulusWitnessService.cs"));

        Assert.Contains("PropertyChanged", source, StringComparison.Ordinal);
        Assert.Contains("ControlCommandBusy", source, StringComparison.Ordinal);
        Assert.Contains("commandCapture.TrySetResult(signal)", source, StringComparison.Ordinal);
        Assert.Contains("ReadSingleVariableAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteReportControl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareDynamicRcbCommissioningFieldsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineDataSet", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteDataSet", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Ui_IsExplicitShortcut_AndDoesNotTouchExistingG25UiHandler()
    {
        var root = FindRepositoryRoot();
        var ui = File.ReadAllText(Path.Combine(root, "DynamicReportCommandBoundWitnessUiBehavior.cs"));
        var app = File.ReadAllText(Path.Combine(root, "App.xaml.cs"));
        var existing = File.ReadAllText(Path.Combine(root, "DynamicReportQualificationUiBehavior.cs"));

        Assert.Contains("Key.F", ui, StringComparison.Ordinal);
        Assert.Contains("READY — ISSUE ONE ARSAS COMMAND", ui, StringComparison.Ordinal);
        Assert.Contains("normal ARSAS Command Panel", ui, StringComparison.Ordinal);
        Assert.Contains("DynamicReportCommandBoundWitnessUiBehavior.Install();", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Key.F", existing, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionDynamicReporting_RemainsOff()
    {
        var root = FindRepositoryRoot();
        var runtime = File.ReadAllText(Path.Combine(root, "Services", "Iec61850MonitorRuntime.cs"));
        Assert.DoesNotContain("AllowDynamicBrcb = true", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicUrcb = true", runtime, StringComparison.Ordinal);
    }

    private static MmsFcResolvedPoint Point(string domain, string logicalNode, string fc, string path, string item)
        => new()
        {
            Domain = domain,
            LogicalNode = logicalNode,
            FunctionalConstraint = fc,
            DataObjectPath = path,
            MmsItemName = item,
            Confidence = 100
        };

    private static DynamicReportCommandBoundTransition Transition(string before, string after, int milliseconds)
        => new()
        {
            Reference = "AA1/XCBR1.Pos.stVal",
            MmsReference = "AA1/XCBR1$ST$Pos$stVal",
            BeforeValue = before,
            AfterValue = after,
            ObservedAtUtc = DateTimeOffset.UnixEpoch.AddMilliseconds(milliseconds)
        };

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
