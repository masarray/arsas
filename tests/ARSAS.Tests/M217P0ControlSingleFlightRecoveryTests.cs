using System.Threading;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class M217P0ControlSingleFlightRecoveryTests
{
    private sealed class SessionFence
    {
        public int Active;
        public int Quarantine;
    }

    [Fact]
    public void DifferentCswiObjectsCannotRunConcurrentCommandsOnSameAssociation()
    {
        var session = new SessionFence();
        Assert.True(Iec61850ControlFlightAdmission.TryEnter(
            ref session.Active, ref session.Quarantine));
        // Same physical MMS session, different CSWI object: reject locally.
        Assert.False(Iec61850ControlFlightAdmission.TryEnter(
            ref session.Active, ref session.Quarantine));
        Assert.True(Iec61850ControlFlightAdmission.IsUnavailable(
            ref session.Active, ref session.Quarantine));
        Iec61850ControlFlightAdmission.Release(ref session.Active);
        Assert.True(Iec61850ControlFlightAdmission.TryEnter(
            ref session.Active, ref session.Quarantine));
        Iec61850ControlFlightAdmission.Release(ref session.Active);
    }

    [Fact]
    public void CompetingConcurrentAdmissionsHaveExactlyOneWinner()
    {
        var session = new SessionFence();
        var winners = 0;
        Parallel.For(0, 40, _ =>
        {
            if (Iec61850ControlFlightAdmission.TryEnter(
                    ref session.Active, ref session.Quarantine))
                Interlocked.Increment(ref winners);
        });
        Assert.Equal(1, winners);
        Assert.Equal(1, session.Active);
    }

    [Fact]
    public void TimeoutQuarantineCannotBeClearedByLateNativeCompletion()
    {
        var session = new SessionFence();
        Assert.True(Iec61850ControlFlightAdmission.TryEnter(
            ref session.Active, ref session.Quarantine));
        Iec61850ControlFlightAdmission.Quarantine(ref session.Quarantine);
        Assert.False(Iec61850ControlFlightAdmission.TryEnter(
            ref session.Active, ref session.Quarantine));
        // Late native completion may release busy ownership, NOT clear the
        // uncertain wire outcome on this association.
        Iec61850ControlFlightAdmission.Release(ref session.Active);
        Assert.Equal(0, session.Active);
        Assert.Equal(1, session.Quarantine);
        Assert.False(Iec61850ControlFlightAdmission.TryEnter(
            ref session.Active, ref session.Quarantine));
        // A newly created DeviceSession has new per-association fence fields.
        var fresh = new SessionFence();
        Assert.True(Iec61850ControlFlightAdmission.TryEnter(
            ref fresh.Active, ref fresh.Quarantine));
    }

    [Fact]
    public void RuntimeDoesNotConfuseWaitTimeoutWithNativeOperationCompletion()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
            !File.Exists(Path.Combine(root.FullName, "Services",
                "Iec61850MonitorRuntime.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code = File.ReadAllText(Path.Combine(root!.FullName,
            "Services", "Iec61850MonitorRuntime.cs"));
        Assert.Contains("ControlFlightDeadline = TimeSpan.FromSeconds(45)", code);
        Assert.Contains("nativeFlight.WaitAsync(ControlFlightDeadline", code);
        Assert.Contains("nativeFlight is { IsCompleted: false }", code);
        Assert.Contains("ObserveLateControlFlightAsync(", code);
        Assert.Contains("CONTROL_OUTCOME_UNKNOWN_TIMEOUT", code);
        Assert.Contains("CONTROL_IED_SINGLE_FLIGHT", code);
        Assert.Contains("Iec61850ControlFlightAdmission.TryEnter(", code);
    }

    [Fact]
    public void BrowserMustRejectNewConfirmationBeforeClaimingPhysicalCommand()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
            !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code = File.ReadAllText(Path.Combine(root!.FullName,
            "MainWindow.xaml.cs"));
        Assert.Contains("RejectCommandWhenIedFlightUnavailable(device, signal)", code);
        Assert.Contains("CONTROL_UI_SINGLE_FLIGHT", code);
        Assert.Contains("CONTROL_UI_TIMEOUT", code);
        Assert.Contains("Control outcome UNKNOWN", code);
    }
}
