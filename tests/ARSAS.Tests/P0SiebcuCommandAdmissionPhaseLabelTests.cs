using System.IO;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0SiebcuCommandAdmissionPhaseLabelTests
{
    private static string Source(string relative)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            var p = Path.Combine(d.FullName, relative);
            if (File.Exists(p)) return File.ReadAllText(p);
            d = d.Parent;
        }
        throw new FileNotFoundException(relative);
    }

    [Theory]
    [InlineData("SIEBCUMEAS/RPRE_MMXU1.A.phsA.cVal.mag.f", "A", "A PhsA")]
    [InlineData("SIEBCUMEAS/RPRE_MMXU1.PPV.phsBC.cVal.mag.f", "PPV", "PPV PhsBC")]
    [InlineData("SIEBCUMEAS/RPRE_MMXU1.A.phsC.cVal.mag.f", "A PhsC", "A PhsC")]
    [InlineData("SIEBCUMEAS/PPRE_MMXU1.TotW.mag.f", "TotW", "TotW")]
    public void ExplorerLabel_RespectsObservedIecPhase_WithoutChangingWireReference(
        string reference, string signal, string expected)
    {
        var point = new Iec61850MonitorPoint
        {
            DeviceId = "siebcu",
            DeviceName = "SIEBCU",
            IecReference = reference,
            SignalName = signal
        };
        Assert.Equal(expected, point.ExplorerSignalName);
        Assert.Equal(reference, point.IecReference);
        Assert.Equal(signal, point.SignalName);
    }

    [Fact]
    public void CommandDoesNotWaitBehindBulkMmsLifecycleOperation()
    {
        var code = Source("Services/UiResponsiveIec61850MonitorRuntimeFacade.cs");
        var start = code.IndexOf("public Task<Iec61850ControlCommandResult> ExecuteControlAsync(",
            StringComparison.Ordinal);
        var end = code.IndexOf("public HybridReportPhysicalValidationSnapshot", start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        Assert.Contains("rejectIfBusy: true", code[start..end]);
        Assert.Contains("OperationGate.WaitAsync(0, cancellationToken)", code);
        Assert.Contains("command NOT SENT", code);
        Assert.Contains("slot.OperationGate.Release();", code);
        Assert.Contains("slot.StopGate.WaitAsync", code);
        var runtime = Source("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("Iec61850ControlFlightAdmission.TryEnter", runtime);
        Assert.Contains("session.Client.ExecuteControlAsync", runtime);
    }
}
