using System.IO;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ARSAS.Tests;

public sealed class P0SiebcuCommandAdmissionAndPhaseNamingTests
{
    [Theory]
    [InlineData("A", "SIEBCUMEAS/RPRE_MMXU1.A.phsA.cVal.mag.f", "A PhsA")]
    [InlineData("PPV", "SIEBCUMEAS/RPRE_MMXU1.PPV.phsBC.cVal.mag.f", "PPV PhsBC")]
    [InlineData("A", "IED1MEAS/MMXU1.A.phsB.cVal.mag.f", "A PhsB")]
    [InlineData("A", "IED1MEAS/MMXU1.A.phsC.cVal.mag.f", "A PhsC")]
    [InlineData("ThdPPV", "IED1MEAS/MMXU1.ThdPPV.phsAB.instMag.f", "ThdPPV PhsAB")]
    public void Explorer_UsesRealSemanticPhaseOfVerifiedIecReference(
        string rawName, string reference, string expected)
    {
        var row = new Iec61850MonitorPoint
        {
            SignalName = rawName,
            IecReference = reference,
            DeviceName = "SIEBCU"
        };
        Assert.True(IoSignalDisplayName.HasPhaseContext(reference));
        Assert.Equal(expected, row.ExplorerSignalName);
        Assert.Equal(rawName, row.SignalName);
        Assert.Equal(reference, row.IecReference);
    }

    [Theory]
    [InlineData("Pos", "SIEBCUCTRL/CSWI1.Pos.stVal", "CSWI1 · Pos")]
    [InlineData("Pos", "SIEBCUCTRL/XCBR1.Pos.stVal", "XCBR1 · Pos")]
    [InlineData("Dig01", "IEDUD1/GGIO1.Dig01.stVal", "GGIO1 · Dig01")]
    public void Explorer_NonPhaseLegacyInstancePresentationRemainsStable(
        string name, string reference, string expected)
    {
        var row = new Iec61850MonitorPoint { SignalName = name, IecReference = reference };
        Assert.False(IoSignalDisplayName.HasPhaseContext(reference));
        Assert.Equal(expected, row.ExplorerSignalName);
    }

    [Fact]
    public void PhaseMatchRejectsUnrelatedFreeTextAndDoesNotDuplicateSuffix()
    {
        Assert.False(IoSignalDisplayName.HasPhaseContext("IED/LLN0.Health.stVal"));
        Assert.False(IoSignalDisplayName.HasPhaseContext("IED/MMXU1.A.phsABCD.cVal.mag.f"));
        Assert.Equal("A PhsA",
            IoSignalDisplayName.Format("A PhsA", "IED/MMXU1.A.phsA.cVal.mag.f"));
    }

    [Fact]
    public void ControlAdmissionNeverWaitsInTheUiLifecycleFifo()
    {
        var source = Read("Services/UiResponsiveIec61850MonitorRuntimeFacade.cs");
        var start = source.IndexOf("public Task<Iec61850ControlCommandResult> ExecuteControlAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("public HybridReportPhysicalValidationSnapshot", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var command = source[start..end];
        Assert.Contains("rejectWhenBusy: true", command);
        Assert.Contains("ClearActiveCommandExpectations(expectationKeys)", command);
        Assert.Contains("_inner.ExecuteControlAsync", command);

        Assert.Contains("OperationGate.WaitAsync(0, cancellationToken)", source);
        Assert.Contains("StopGate.WaitAsync(0, cancellationToken)", source);
        Assert.Contains("IEC command NOT SENT:", source);
        Assert.Contains("slot.OperationGate.Release();", source);
        Assert.Contains("slot.StopGate.Release();", source);
        Assert.Contains("RunPreemptiveStopAsync", source);
        Assert.DoesNotContain("slot.ActiveOperationCancellation?.Cancel();", command);
    }

    [Fact]
    public void NativeSingleFlightAndSboOperateSafetyRemainStrict()
    {
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        var native = Read("Services/NativeIec61850Client.cs");
        Assert.Contains("Iec61850ControlFlightAdmission.TryEnter(", runtime);
        Assert.Contains("CONTROL_IED_SINGLE_FLIGHT:", runtime);
        Assert.Contains("CONTROL_OUTCOME_UNKNOWN_TIMEOUT:", runtime);
        Assert.Contains("Quarantine(ref session.ControlFlightQuarantined)", runtime);
        Assert.Contains("_controlCommandGate.WaitAsync(cancellationToken)", native);
        Assert.Contains("ExecuteControlCoreAsync(request, cancellationToken)", native);
    }

    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path))
                return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
