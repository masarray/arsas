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
        Assert.Contains("session.ControlFlight.TryEnter()", code);
        Assert.Contains("session.ControlFlight.Retire()", code);
        Assert.Contains("CONTROL_OUTCOME_UNKNOWN_NATIVE_RESULT", code);
        Assert.Contains("DisposeDetachedSessionAfterDrainAsync(", code);
        Assert.Contains("if (!IsCurrentSession(session))", code);
        Assert.DoesNotContain("_activeControlTargets", code);
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

    [Fact]
    public async Task RetiredAssociationWaitsForActualNativeRelease()
    {
        var oldAssociation = new Iec61850ControlSessionLease();
        Assert.True(oldAssociation.TryEnter());
        var retirementToken = oldAssociation.RetirementToken;
        Assert.False(retirementToken.IsCancellationRequested);
        var drain = oldAssociation.Retire();
        Assert.True(retirementToken.IsCancellationRequested);
        Assert.False(drain.IsCompleted);
        Assert.True(oldAssociation.IsUnavailable);
        Assert.True(oldAssociation.IsActive);
        Assert.False(oldAssociation.TryEnter());

        // A new socket has an independent lease even for the same deviceId/CSWI.
        var newAssociation = new Iec61850ControlSessionLease();
        Assert.True(newAssociation.TryEnter());
        oldAssociation.Release();
        await drain.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(newAssociation.IsActive);
        Assert.False(oldAssociation.TryEnter());
        newAssociation.Release();
    }

    [Fact]
    public void RetirementIsAtomicAgainstConcurrentAdmission()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var oldAssociation = new Iec61850ControlSessionLease();
            var contenders = new Task[8];
            // Once Retire returns, no further physical admission is possible.
            var drain = oldAssociation.Retire();
            Assert.True(drain.IsCompleted);
            for (var i = 0; i < contenders.Length; i++)
                contenders[i] = Task.Run(() =>
                    Assert.False(oldAssociation.TryEnter()));
            Task.WaitAll(contenders);
        }
    }

    [Fact]
    public void LateCompletionNeverClearsUnknownWireQuarantine()
    {
        var lease = new Iec61850ControlSessionLease();
        Assert.True(lease.TryEnter());
        lease.Quarantine();
        lease.Release();
        Assert.True(lease.IsUnavailable);
        Assert.False(lease.TryEnter());
    }

    [Theory]
    [InlineData("WireStateUnknown")]
    [InlineData("TimedOut")]
    [InlineData("MmsTimeout")]
    public void NativeCompletedButUncertainOutcomeRequiresQuarantine(string state)
    {
        Assert.True(Iec61850ControlWireOutcomePolicy.RequiresQuarantine(state));
        Assert.False(Iec61850ControlWireOutcomePolicy.RequiresQuarantine("NotSent"));
        Assert.False(Iec61850ControlWireOutcomePolicy.RequiresQuarantine("Rejected"));
        Assert.False(Iec61850ControlWireOutcomePolicy.RequiresQuarantine("Completed"));
    }

    [Fact]
    public void BrowserDiscardsResultFromRetiredControlAssociation()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
               !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.xaml.cs"));
        Assert.Contains("GetControlAssociationId(device.DeviceId)", code);
        Assert.Contains("IsControlAssociationCurrent(device.DeviceId, dispatchedAssociation)", code);
        Assert.Contains("CONTROL_STALE_RESULT_IGNORED", code);
    }


    [Fact]
    public void NativeFlightSnapshotNeverSharesMutableGuiSignal()
    {
        var shared = new SignalDefinition
        {
            Name = "CSWI6 Position",
            ObjectReference = "BCU1CSWI6.Pos",
            IsControlSignal = true,
            ControlCdc = "DPC",
            ControlStatusReference = "BCU1CSWI6.Pos.stVal",
            ControlValueType = "Dbpos",
            ControlModelText = "Direct normal",
            ControlCurrentValue = "Open [01]",
            ControlSemanticVerified = true
        };
        shared.AcceptVerifiedLiveControlCdc("DPC");
        var original = new Iec61850ControlCommandRequest
        {
            Signal = shared,
            ValueText = "Closed [10]",
            InterlockCheck = false,
            SynchroCheck = true,
            TestMode = true,
            Originator = "ARSAS test",
            OriginCategory = "StationControl",
            FeedbackTimeoutMs = 9000,
            CommandTerminationTimeoutMs = 7000
        };
        var detached = Iec61850ControlRequestSnapshot.Create(original);
        Assert.NotSame(shared, detached.Signal);
        Assert.Equal(shared.ObjectReference, detached.Signal.ObjectReference);
        Assert.Equal(shared.ControlStatusReference, detached.Signal.ControlStatusReference);
        Assert.Equal("DPC", detached.Signal.VerifiedLiveControlCdc);
        Assert.Equal(original.ValueText, detached.ValueText);
        Assert.Equal(original.InterlockCheck, detached.InterlockCheck);
        Assert.Equal(original.SynchroCheck, detached.SynchroCheck);
        Assert.Equal(original.TestMode, detached.TestMode);
        Assert.Equal(original.Originator, detached.Originator);
        Assert.Equal(original.OriginCategory, detached.OriginCategory);
        Assert.Equal(original.FeedbackTimeoutMs, detached.FeedbackTimeoutMs);
        Assert.Equal(original.CommandTerminationTimeoutMs, detached.CommandTerminationTimeoutMs);

        // Simulate a late native reply after the caller retired old MMS state.
        detached.Signal.ControlCurrentValue = "Closed [10]";
        detached.Signal.ControlSemanticVerified = false;
        Assert.Equal("Open [01]", shared.ControlCurrentValue);
        Assert.True(shared.ControlSemanticVerified);
    }

}
