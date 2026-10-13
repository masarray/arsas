using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P109MControlDockPresentationTests
{
    private static SignalDefinition Control(string obj, string cdc)
        => new()
        {
            Name = obj[(obj.LastIndexOf('.') + 1)..],
            ObjectReference = obj,
            IsControlSignal = true,
            IsSelected = true,
            FunctionalConstraint = "CO",
            ControlCdc = cdc,
            ControlModelText = "Status only",
        };

    [Theory]
    [InlineData("IED/CSWI1.Pos", "DPC", true, false)]
    [InlineData("IED/CSWI2.Pos", "DPC", true, false)]
    [InlineData("IED/CSWI3.Pos", "DPC", true, false)]
    [InlineData("IED/CSWI4.Pos", "DPC", true, false)]
    [InlineData("IED/ATCC1.TapChg", "BSC", false, true)]
    public void StatusOnly_DoesNotEraseDeclaredButtons_AndCommandsRemainBlocked(
        string reference, string cdc, bool expectedPosition, bool expectedTap)
    {
        var signal = Control(reference, cdc);
        Assert.True(signal.ControlModelResolved);
        Assert.Equal(expectedPosition, signal.IsPositionControl);
        Assert.Equal(expectedTap, signal.IsRaiseLowerControl);
        Assert.False(signal.IsGenericControl);
        Assert.False(signal.ControlSupportsOperate);
        Assert.False(signal.TryStageControlConfirmation("Closed [10]", "Close", out _));
        Assert.False(signal.TryBeginDirectControlCommand("Raise", "Raise", out _, out _));
        signal.ControlModelText = "Auto-detect";
        Assert.Equal(expectedPosition, signal.IsPositionControl);
        Assert.Equal(expectedTap, signal.IsRaiseLowerControl);
        Assert.False(signal.ControlSupportsOperate);
    }

    [Fact]
    public void MismatchedCdc_RequiresExactStepStatus_NotTapChgNameHeuristics()
    {
        var control = Control("IED/ATCC1.TapChg", "SPC");
        var onlyName = new List<SignalDefinition> { control };
        Assert.Equal(0, Iec61850ControlStatusEvidence.ApplyExactStepStatus(onlyName));
        Assert.True(control.IsBooleanControl);

        var exact = new SignalDefinition
        {
            ObjectReference = "IED/ATCC1.TapChg.valWTr.posVal",
            FunctionalConstraint = "ST"
        };
        onlyName.Add(exact);
        Assert.Equal(1, Iec61850ControlStatusEvidence.ApplyExactStepStatus(onlyName));
        Assert.Equal("BSC", control.ControlCdc);
        Assert.Equal(exact.ObjectReference, control.ControlStatusReference);
        Assert.True(control.IsRaiseLowerControl);
        Assert.False(control.ControlSupportsOperate);
        Assert.Equal(0, Iec61850ControlStatusEvidence.ApplyExactStepStatus(onlyName));
    }

    [Fact]
    public void SimilarOrWrongFunctionalConstraintStatus_CannotReclassifyCdc()
    {
        var control = Control("IED/ATCC1.TapChg", "SPC");
        var neighbors = new List<SignalDefinition> {
            control,
            new() { ObjectReference = "IED/ATCC2.TapChg.valWTr.posVal", FunctionalConstraint = "ST" },
            new() { ObjectReference = "IED/ATCC1.TapChg.valWTr.posVal", FunctionalConstraint = "MX" }
        };
        Assert.Equal(0, Iec61850ControlStatusEvidence.ApplyExactStepStatus(neighbors));
        Assert.Equal("SPC", control.ControlCdc);
    }

    [Fact]
    public void DiagnosticsAndXamlKeepDisabledControlsVisibleAndExplainWhy()
    {
        var root = FindRepo();
        var xaml = File.ReadAllText(Path.Combine(root, "MainWindow.xaml"));
        var diagnostics = File.ReadAllText(Path.Combine(root, "Services", "DiagnosticReportBuilder.cs"));
        Assert.Contains("Binding IsPositionControl", xaml);
        Assert.Contains("Binding IsRaiseLowerControl", xaml);
        Assert.Contains("IsEnabled=\"{Binding ControlSupportsOperate}\"", xaml);
        // Detailed diagnostics belong to the model/evidence column and
        // Diagnostic Report, never to a large hover popover over buttons.
        Assert.Contains("<Setter Property=\"ToolTip\" Value=\"{Binding ControlInspectionEvidence}\"/>", xaml);
        var controlStart = xaml.IndexOf("<DataGridTemplateColumn Header=\"Control\"", StringComparison.Ordinal);
        var controlEnd = xaml.IndexOf("</DataGridTemplateColumn>", controlStart, StringComparison.Ordinal);
        Assert.True(controlStart >= 0 && controlEnd > controlStart);
        Assert.DoesNotContain("ToolTip=\"{Binding ControlInspectionEvidence}\"",
            xaml[controlStart..controlEnd]);
        Assert.Contains("No typed action", xaml);
        Assert.Contains("Control catalog  : selected=", diagnostics);
        Assert.Contains("ControlInspectionEvidence", diagnostics);
    }

    private static string FindRepo()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MainWindow.xaml"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("ARSAS source root not found");
    }
}
