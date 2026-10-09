using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class F650CanonicalCommandFailureTests
{
    private static SignalDefinition Pos(string dataset, string reference)
    {
        var signal = new SignalDefinition
        {
            Name = "Pos", ObjectReference = reference, DisplayReference = reference,
            FunctionalConstraint = "CO", DataType = "DPC", ControlCdc = "DPC",
            DataSetReference = dataset, IsControlSignal = true, IsSelected = true
        };
        signal.ControlModelText = "Direct Operate (DO) • Normal security";
        return signal;
    }

    [Fact]
    public void F650_SameCswiInThreeDataSets_IsOneOperableCommand()
    {
        var signals = new[]
        {
            Pos("BCUGEF650/LLN0.CSWI", "BCUGEF650/CSWI1.Pos"),
            Pos("BCUGEF650/LLN0.MMXU_SOGI1", "BCUGEF650/CSWI1.Pos"),
            Pos("BCUGEF650/LLN0.REPORT1", "BCUGEF650/CSWI1.Pos"),
            Pos("BCUGEF650/LLN0.CSWI", "BCUGEF650/CSWI10.Pos")
        };
        Assert.Equal(2, Iec61850ControlIdentity.CountSelected(signals));
        Assert.Equal(2, Iec61850ControlIdentity.DistinctOperable(signals).Length);
    }

    [Fact]
    public void Identity_IsReferenceAwareNotDataSetNorObjectNameAlone()
    {
        Assert.Equal("LD1/CSWI1.Pos", Iec61850ControlIdentity.Normalize("LD1\\CSWI1$Pos"));
        var signals = new[]
        {
            Pos("DS", "LD1/CSWI1.Pos"),
            Pos("DS", "LD2/CSWI1.Pos"),
            Pos("DS", "LD1/CSWI2.Pos")
        };
        Assert.Equal(3, Iec61850ControlIdentity.DistinctOperable(signals).Length);
    }

    [Theory]
    [InlineData("blocked-by-interlocking", "interlocking")]
    [InlineData("blocked-by-synchrocheck", "synchrocheck")]
    [InlineData("blocked-by-mode", "control mode")]
    [InlineData("no-access-authority", "access authority")]
    public void ExplicitIEDAddCause_IsReportedAsConfirmed(string cause, string expected)
    {
        var result = new Iec61850ControlCommandResult { AddCause = cause, Message = "IED rejected" };
        var explanation = Iec61850ControlFailureReason.Explain(result);
        Assert.Equal("IEDExplicitAddCause", explanation.Confidence);
        Assert.Contains(expected, explanation.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void F650_MmsDeniedWhileLocal_DoesNotInventProvenInterlockOrMode()
    {
        var result = new Iec61850ControlCommandResult
        {
            Message = "MMS Confirmed-Write rejected: item[0]=object-access-denied (3)",
            CompletionState = "Rejected"
        };
        var explanation = Iec61850ControlFailureReason.Explain(result);
        Assert.Equal("MmsServiceOnly", explanation.Confidence);
        Assert.Contains("Local/Remote", explanation.Checks, StringComparison.Ordinal);
        Assert.Contains("specific cause not provided", explanation.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalClientFailure_ReportsOperateNotSent()
    {
        var result = new Iec61850ControlCommandResult { CompletionState = "NotSent" };
        Assert.Equal("ConfirmedClient", Iec61850ControlFailureReason.Explain(result).Confidence);
    }

    [Fact]
    public void FailedCommand_HasVisibleOperatorWarning_OnlyOnFailure()
    {
        var main = Read("MainWindow.xaml.cs");
        Assert.Contains("if (!result.IsSuccess)", main, StringComparison.Ordinal);
        Assert.Contains("IED Command Failed —", main, StringComparison.Ordinal);
        Assert.Contains("MessageBoxImage.Warning", main, StringComparison.Ordinal);
        Assert.Contains("explanation.Confidence", main, StringComparison.Ordinal);
        Assert.Contains("The exact MMS response and AddCause", main, StringComparison.Ordinal);
    }

    [Fact]
    public void ProducersAndDispatcher_EnforceCanonicalIdentity()
    {
        var projection = Read("Services/Iec61850StaticControlStatusProjectionService.cs");
        Assert.Contains("Full DataObject is command identity", projection, StringComparison.Ordinal);
        var model = Read("Models/MonitorModels.cs");
        Assert.Contains("DistinctOperable(Signals)", model, StringComparison.Ordinal);
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("_activeControlTargets.TryAdd(targetKey, 0)", runtime, StringComparison.Ordinal);
        Assert.Contains("_activeControlTargets.TryRemove(targetKey, out _)", runtime, StringComparison.Ordinal);
        Assert.Contains("CONTROL_FAILURE_CLASSIFIED", runtime, StringComparison.Ordinal);
    }

    private static string Read(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            var name = Path.Combine(dir.FullName, path);
            if (File.Exists(name)) return File.ReadAllText(name);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
