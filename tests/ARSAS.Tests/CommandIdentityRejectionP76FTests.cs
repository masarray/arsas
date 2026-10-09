using ArIED61850Tester.Models;
using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class CommandIdentityRejectionP76FTests
{
    [Fact]
    public void CommandDock_DeduplicatesDifferentDataSetsByExactControlObject()
    {
        var device = new Iec61850MonitorDevice();
        var first = Command("BCUGEF650/CSWI1.Pos", "BCUGEF650/LLN0.CSWI");
        var second = Command("BCUGEF650/CSWI1.Pos", "BCUGEF650/LLN0.MMXU_SOGI1");
        var third = Command("BCUGEF650/CSWI10.Pos", "BCUGEF650/LLN0.CSWI");
        device.Signals.Add(first);
        device.Signals.Add(second);
        device.Signals.Add(third);
        device.RefreshCommandSignalProjection();
        Assert.Equal(2, device.CommandSignals.Count);
        Assert.Contains(device.CommandSignals, s => s.ObjectReference == "BCUGEF650/CSWI1.Pos");
        Assert.Contains(device.CommandSignals, s => s.ObjectReference == "BCUGEF650/CSWI10.Pos");
        Assert.Equal(3, device.Signals.Count); // reporting membership unchanged
    }

    [Fact]
    public void CommandDock_DoesNotMergeDifferentLDsOrIEDs()
    {
        var device = new Iec61850MonitorDevice();
        device.Signals.Add(Command("BCUGEF650/CSWI1.Pos", "a"));
        device.Signals.Add(Command("BCUGEOTHER/CSWI1.Pos", "b"));
        device.RefreshCommandSignalProjection();
        Assert.Equal(2, device.CommandSignals.Count);
    }

    [Fact]
    public void DeniedMmsWithoutAddCause_DoesNotInventLocalInterlockOrAuthorization()
    {
        var result = new Iec61850ControlCommandResult
        {
            Message = "MMS Confirmed-Write rejected: item[0]=object-access-denied (3)."
        };
        var message = MainWindow.BuildControlRejectionDetail(result);
        Assert.Contains("object-access-denied", message);
        Assert.Contains("check Local/Remote", message);
        Assert.Contains("did not disclose the cause", message);
        Assert.DoesNotContain("AddCause=", message);
        Assert.DoesNotContain("ControlError=", message);
    }

    [Fact]
    public void RealAddCause_IsPreservedNotOverriddenBySpeculativeLocalDiagnosis()
    {
        var result = new Iec61850ControlCommandResult
        {
            Message = "Control rejected by IED",
            AddCause = "blocked-by-interlocking",
            ControlError = "1"
        };
        var message = MainWindow.BuildControlRejectionDetail(result);
        Assert.Contains("AddCause=blocked-by-interlocking", message);
        Assert.Contains("ControlError=1", message);
        Assert.DoesNotContain("check Local/Remote", message);
    }

    private static SignalDefinition Command(string reference, string dataset) => new()
    {
        ObjectReference = reference,
        DisplayReference = reference,
        DataSetReference = dataset,
        IsControlSignal = true,
        IsSelected = true,
        ControlModelResolved = true,
        ControlCdc = "DPC",
        ControlModelText = "Direct · Normal security"
    };
}
