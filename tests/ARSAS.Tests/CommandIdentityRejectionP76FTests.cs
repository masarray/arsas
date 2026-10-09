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

    [Theory]
    [InlineData("blocked-by-interlocking", "Command blocked by interlock")]
    [InlineData("blocked-by-synchrocheck", "Command blocked by synchrocheck")]
    [InlineData("no-access-authority", "Command authorization denied")]
    [InlineData("blocked-by-mode", "Command blocked by operating mode")]
    public void Toast_UsesRealIedAddCauseWhenAvailable(string addCause, string expected)
    {
        var result = new Iec61850ControlCommandResult
        {
            Message = "Control service rejected",
            AddCause = addCause
        };
        var toast = MainWindow.BuildControlShout(result);
        Assert.Equal(expected, toast.Title);
    }

    [Fact]
    public void Toast_GenericMmsAccessDeniedDoesNotClaimLocalIsProven()
    {
        var result = new Iec61850ControlCommandResult
        {
            Message = "MMS Confirmed-Write rejected: object-access-denied (3)"
        };
        var toast = MainWindow.BuildControlShout(result);
        Assert.Equal("Command rejected by IED", toast.Title);
        Assert.Contains("Check BCU Local/Remote", toast.Detail);
        Assert.DoesNotContain("confirmed local", toast.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Toast_XamlIsNonModalAndTimerAutoDismisses()
    {
        var xaml = Read("MainWindow.xaml");
        var code = Read("MainWindow.xaml.cs");
        Assert.Contains("x:Name=\"ControlShoutCard\"", xaml);
        Assert.Contains("Panel.ZIndex=\"90\"", xaml);
        Assert.Contains("Interval = TimeSpan.FromSeconds(5)", code);
        Assert.Contains("ControlShoutCard.Visibility = Visibility.Collapsed", code);
    }

    private static string Read(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var file = Path.Combine(directory.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(path);
    }

    private static SignalDefinition Command(string reference, string dataset) => new()
    {
        ObjectReference = reference,
        DisplayReference = reference,
        DataSetReference = dataset,
        IsControlSignal = true,
        IsSelected = true,
        ControlCdc = "DPC",
        ControlModelText = "Direct · Normal security"
    };
}
