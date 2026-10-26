using System.Threading;
using ArIED61850Tester.Models;
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
    public async Task NativeDeadlineQuarantinesButNeverFakesTaskCompletion()
    {
        var session = new SessionFence();
        Assert.True(Iec61850ControlFlightAdmission.TryEnter(ref session.Active, ref session.Quarantine));
        var native = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var nativeCancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            Iec61850ControlFlightAdmission.WaitForNativeAsync(
                native.Task, TimeSpan.FromMilliseconds(40), CancellationToken.None,
                () => Iec61850ControlFlightAdmission.Quarantine(ref session.Quarantine),
                nativeCancellation.Cancel));

        Assert.True(nativeCancellation.IsCancellationRequested);
        Assert.False(native.Task.IsCompleted);
        Assert.True(Iec61850ControlFlightAdmission.IsUnavailable(ref session.Active, ref session.Quarantine));
        Assert.False(Iec61850ControlFlightAdmission.TryEnter(ref session.Active, ref session.Quarantine));

        native.SetResult(42);
        Assert.Equal(42, await native.Task);
        Iec61850ControlFlightAdmission.Release(ref session.Active);
        // The late native result is observed, not promoted to fresh authority.
        Assert.Equal(1, session.Quarantine);
        Assert.False(Iec61850ControlFlightAdmission.TryEnter(ref session.Active, ref session.Quarantine));
    }

    [Fact]
    public async Task CallerCancellationQuarantinesAnUnresolvedNativeWrite()
    {
        var session = new SessionFence();
        Assert.True(Iec61850ControlFlightAdmission.TryEnter(ref session.Active, ref session.Quarantine));
        var native = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var waiterCancellation = new CancellationTokenSource();
        using var nativeCancellation = new CancellationTokenSource();
        var waiter = Iec61850ControlFlightAdmission.WaitForNativeAsync(
            native.Task, TimeSpan.FromSeconds(10), waiterCancellation.Token,
            () => Iec61850ControlFlightAdmission.Quarantine(ref session.Quarantine),
            nativeCancellation.Cancel);

        waiterCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.True(nativeCancellation.IsCancellationRequested);
        Assert.False(native.Task.IsCompleted);
        Assert.True(Iec61850ControlFlightAdmission.IsUnavailable(ref session.Active, ref session.Quarantine));
    }

    [Fact]
    public async Task AlreadyCanceledNativeTaskNeverProvesWriteWasNotSent()
    {
        var session = new SessionFence();
        Assert.True(Iec61850ControlFlightAdmission.TryEnter(ref session.Active, ref session.Quarantine));
        using var nativeCancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Iec61850ControlFlightAdmission.WaitForNativeAsync(
                Task.FromCanceled<int>(new CancellationToken(canceled: true)),
                TimeSpan.FromSeconds(1), CancellationToken.None,
                () => Iec61850ControlFlightAdmission.Quarantine(ref session.Quarantine),
                nativeCancellation.Cancel));
        Assert.True(nativeCancellation.IsCancellationRequested);
        Iec61850ControlFlightAdmission.Release(ref session.Active);
        Assert.Equal(1, session.Quarantine);
        Assert.False(Iec61850ControlFlightAdmission.TryEnter(ref session.Active, ref session.Quarantine));
    }

    [Fact]
    public async Task CompletedNativeResponseKeepsAssociationAvailableAfterRelease()
    {
        var session = new SessionFence();
        Assert.True(Iec61850ControlFlightAdmission.TryEnter(ref session.Active, ref session.Quarantine));
        var canceled = false;
        var result = await Iec61850ControlFlightAdmission.WaitForNativeAsync(
            Task.FromResult(17), TimeSpan.FromSeconds(1), CancellationToken.None,
            () => Iec61850ControlFlightAdmission.Quarantine(ref session.Quarantine),
            () => canceled = true);
        Assert.Equal(17, result);
        Assert.False(canceled);
        Iec61850ControlFlightAdmission.Release(ref session.Active);
        Assert.False(Iec61850ControlFlightAdmission.IsUnavailable(ref session.Active, ref session.Quarantine));
    }

    [Fact]
    public void RuntimeControlRecoveryClosesAlternativeMmsLanesAndFencesDisconnect()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
            !File.Exists(Path.Combine(root.FullName, "Services", "Iec61850MonitorRuntime.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code = File.ReadAllText(Path.Combine(root!.FullName, "Services", "Iec61850MonitorRuntime.cs"));
        Assert.Contains("CTL_MODEL_OUTCOME_UNKNOWN_TIMEOUT", code);
        Assert.Contains("CONTROL_MONITOR_QUARANTINE", code);
        Assert.Contains("CONTROL_STOP_DEFERRED_NATIVE_RCB_CLEANUP", code);
        Assert.Contains("ControlLifetimeCancellation.Cancel()", code);
        Assert.Contains("session.ControlLifetimeCancellation.Token", code);
        Assert.Contains("Control association was replaced before result publication", code);
        Assert.Contains("Monitoring cannot start during an active or uncertain MMS control flight", code);

        var nativeCode = File.ReadAllText(Path.Combine(root.FullName, "Services", "NativeIec61850Client.cs"));
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", nativeCode);
        Assert.Contains("if (!cancellationToken.IsCancellationRequested)", nativeCode);
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
        Assert.Contains("Iec61850ControlFlightAdmission.WaitForNativeAsync(", code);
        Assert.Contains("nativeFlight is { IsCompleted: false }", code);
        Assert.Contains("ObserveLateControlFlightAsync(", code);
        Assert.Contains("CONTROL_OUTCOME_UNKNOWN_TIMEOUT", code);
        Assert.Contains("CONTROL_IED_SINGLE_FLIGHT", code);
        Assert.Contains("Iec61850ControlFlightAdmission.TryEnter(", code);
    }

    [Fact]
    public void ControlTargetClaimsAreScopedToSessionNotOnlyDeviceId()
    {
        // An unresolved old native flight must never block or remove a claim
        // belonging to the newly established association for the same IED.
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
            !File.Exists(Path.Combine(root.FullName, "Services",
                "Iec61850MonitorRuntime.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code = File.ReadAllText(Path.Combine(root!.FullName,
            "Services", "Iec61850MonitorRuntime.cs"));
        Assert.Contains("ControlSessionIdentity { get; } = Guid.NewGuid()", code);
        Assert.Contains("session.ControlSessionIdentity.ToString(\"N\") + \"|\" + Iec61850ControlIdentity.Normalize(signal.ObjectReference)", code);
        Assert.Contains("session.ControlSessionIdentity.ToString(\"N\") + \"|\" + Iec61850ControlIdentity.Normalize(request.Signal.ObjectReference)", code);
        Assert.DoesNotContain("var targetKey = deviceId +", code);
    }

    [Fact]
    public void DisconnectCleanupUsesBoundedExistingClientDisposal()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
            !File.Exists(Path.Combine(root.FullName, "Services",
                "Iec61850MonitorRuntime.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code = File.ReadAllText(Path.Combine(root!.FullName,
            "Services", "Iec61850MonitorRuntime.cs"));
        var stop = code.IndexOf("public async Task StopDeviceAsync(", StringComparison.Ordinal);
        var next = code.IndexOf("private async Task StartReportPlansAsync(", stop, StringComparison.Ordinal);
        Assert.True(stop >= 0 && next > stop);
        var stopBody = code[stop..next];
        Assert.Contains("DisposeClientForReconnectAsync(", stopBody);
        Assert.Contains("SmartReconnectPolicy.ClientCleanupBudget", stopBody);
        Assert.DoesNotContain("await session.Client.DisposeAsync()", stopBody);
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
    public async Task DetachedLateFlightCannotMutateGuiOwnedSignalAfterCancellation()
    {
        var gui = new ArIED61850Tester.Models.SignalDefinition
        {
            Name = "CSWI6 Pos", ObjectReference = "LD/CSWI6.Pos",
            IsControlSignal = true, ControlCdc = "DPC",
            ControlStatusReference = "LD/CSWI6.Pos.stVal",
            ControlSemanticVerified = true,
            ControlCurrentValue = "Open [01]"
        };
        gui.AcceptVerifiedLiveControlCdc("DPC");
        // SignalDefinition normalizes operator labels when live CDC is
        // accepted ("Open [01]" can become "Open"). Assert immutability
        // against the GUI's actual pre-flight state, not a display spelling.
        var originalGuiValue = gui.ControlCurrentValue;
        var request = new Iec61850ControlCommandRequest
        {
            Signal = gui, ValueText = "Closed [10]", TestMode = true,
            FeedbackTimeoutMs = 12000
        };
        var detached = Iec61850ControlRequestSnapshot.Create(request);
        Assert.NotSame(gui, detached.Signal);
        Assert.Equal("DPC", detached.Signal.VerifiedLiveControlCdc);

        var nativeFinished = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var userCancelled = new CancellationTokenSource();
        var quarantine = 0;
        var cancelledNative = 0;
        var waiter = Iec61850ControlFlightAdmission.WaitForNativeAsync(
            nativeFinished.Task, TimeSpan.FromMinutes(1),
            userCancelled.Token,
            () => Interlocked.Exchange(ref quarantine, 1),
            () => Interlocked.Exchange(ref cancelledNative, 1));
        userCancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiter);
        Assert.Equal(1, Volatile.Read(ref quarantine));
        Assert.Equal(1, Volatile.Read(ref cancelledNative));

        // The real native task did not finish when its caller stopped waiting.
        // Late completion may mutate only its private detached signal copy.
        detached.Signal.ControlCurrentValue = "Closed [10]";
        detached.Signal.ControlSemanticVerified = false;
        nativeFinished.TrySetResult(1);
        Assert.Equal(originalGuiValue, gui.ControlCurrentValue);
        Assert.True(gui.ControlSemanticVerified);
        Assert.Equal(1, Volatile.Read(ref quarantine));
    }

    [Fact]
    public void OperatorControlPublicationUsesNonreusableAssociationFence()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
               !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var ui = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.xaml.cs"));
        var runtime = File.ReadAllText(Path.Combine(root.FullName,
            "Services/Iec61850MonitorRuntime.cs"));
        var facade = File.ReadAllText(Path.Combine(root.FullName,
            "Services/UiResponsiveIec61850MonitorRuntimeFacade.cs"));
        Assert.Contains("Guid dispatchedAssociation = Guid.Empty;", ui);
        Assert.Contains("IsControlAssociationCurrent(device.DeviceId, dispatchedAssociation)", ui);
        Assert.Contains("CONTROL_STALE_RESULT_IGNORED", ui);
        Assert.Contains("_applicationCancellation.Token, dispatchedAssociation", ui);
        Assert.Contains("session.ControlSessionIdentity != expectedAssociation", runtime);
        Assert.Contains("Iec61850ControlRequestSnapshot.Create(request)", runtime);
        Assert.Contains("detachedRequest, nativeCancellation.Token", runtime);
        Assert.Contains("_inner.IsControlAssociationCurrent(deviceId, associationId)", facade);
    }

    [Fact]
    public void DetachedReadOnlyInspectionAndAssociationPublicationCannotReplayOldProof()
    {
        var gui = new SignalDefinition
        {
            Name = "CSWI6 Pos", ObjectReference = "LD/CSWI6.Pos",
            IsControlSignal = true, FunctionalConstraint = "CO",
            ControlCdc = "DPC", ControlModelText = "Auto-detect",
            ControlCurrentValue = "Open"
        };
        var detached = Iec61850ControlRequestSnapshot.CreateSignal(gui);
        Assert.NotSame(gui, detached);
        detached.ControlCurrentValue = "Closed";
        detached.ControlModelText = "Direct Operate (DO) • Normal security";
        detached.AcceptVerifiedLiveControlCdc("DPC");
        detached.ControlSemanticVerified = true;
        Assert.NotEqual(detached.ControlCurrentValue, gui.ControlCurrentValue);
        Assert.False(gui.ControlSemanticVerified);
        Assert.Empty(gui.VerifiedLiveControlCdc);

        var old = Guid.NewGuid();
        var current = Guid.NewGuid();
        var ready = new Iec61850ControlCapabilities
        {
            ObjectReference = gui.ObjectReference,
            StatusReference = "LD/CSWI6.Pos.stVal",
            ControlModelReference = "LD/CSWI6.Pos.ctlModel",
            ControlModel = Iec61850ControlModelKind.DirectNormal,
            ControlModelText = "Direct Operate (DO) • Normal security",
            ControlCdc = "DPC", ControlValueType = "Dbpos",
            CurrentValue = "Closed", IsOperationallyReady = true,
            EngineControlServiceAvailable = true,
            EngineControlServiceStatus = "Live typed descriptor verified"
        };
        Assert.False(Iec61850ControlInspectionPublication.TryApply(
            gui, ready, old, id => id == current));
        Assert.Equal("Open", gui.ControlCurrentValue);
        Assert.False(gui.ControlSemanticVerified);
        var foreignObject = new Iec61850ControlCapabilities
        {
            ObjectReference = "LD/CSWI7.Pos",
            ControlModel = Iec61850ControlModelKind.DirectNormal,
            ControlCdc = "DPC", IsOperationallyReady = true,
            EngineControlServiceAvailable = true
        };
        Assert.False(Iec61850ControlInspectionPublication.TryApply(
            gui, foreignObject, current, _ => true));
        Assert.True(Iec61850ControlInspectionPublication.TryApply(
            gui, ready, current, id => id == current));
        Assert.Equal("Closed", gui.ControlCurrentValue);
        Assert.Equal("DPC", gui.VerifiedLiveControlCdc);
        Assert.True(gui.ControlSemanticVerified);
        Assert.True(gui.ControlSupportsOperate);

        var statusOnly = new Iec61850ControlCapabilities
        {
            ObjectReference = gui.ObjectReference,
            ControlModel = Iec61850ControlModelKind.StatusOnly,
            ControlModelText = "Status only",
            ControlCdc = "DPC", CurrentValue = "-",
            EngineControlServiceAvailable = true,
            IsOperationallyReady = false,
            EngineControlServiceStatus = "Status-only MMS read"
        };
        Assert.True(Iec61850ControlInspectionPublication.TryApply(
            gui, statusOnly, current, id => id == current));
        Assert.False(gui.ControlSemanticVerified);
        Assert.Empty(gui.VerifiedLiveControlCdc);
        Assert.False(gui.ControlSupportsOperate);
        Assert.Equal("Status only", gui.ControlModelText);
        Assert.Contains("Status only", gui.ControlLastResult);
        Assert.Contains("read-only", gui.ControlLastResult, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void ControlInspectionNativeUsesOnlyDetachedSignalAndUiPublicationFence()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
               !File.Exists(Path.Combine(root.FullName,
                   "Services/Iec61850MonitorRuntime.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var runtime = File.ReadAllText(Path.Combine(root!.FullName,
            "Services/Iec61850MonitorRuntime.cs"));
        var facade = File.ReadAllText(Path.Combine(root.FullName,
            "Services/UiResponsiveIec61850MonitorRuntimeFacade.cs"));
        Assert.Contains(
            "var detachedSignal = Iec61850ControlRequestSnapshot.CreateSignal(signal);",
            runtime);
        Assert.Contains("detachedSignal, inspectionCancellation.Token", runtime);
        Assert.Contains("var detached = Iec61850ControlRequestSnapshot.CreateSignal(signal);",
            facade);
        Assert.Contains("Iec61850ControlInspectionPublication.TryApply(", facade);
        Assert.Contains("_inner.IsControlAssociationCurrent(deviceId, expected)", facade);
        Assert.Contains("signal.ControlCommandBusy", facade);
        Assert.Contains("signal.ClearLiveControlAuthority();", facade);
    }

    [Fact]
    public void PostWriteInspectionRejectsRetiredMmsAndLiveModelEpochs()
    {
        var oldMms = Guid.NewGuid();
        var currentMms = Guid.NewGuid();
        const long writeModelEpoch = 54;
        bool Current(Guid association) => association == currentMms;
        Assert.False(Iec61850ControlInspectionPublication.IsCurrentPostWriteTarget(
            Guid.Empty, Current, writeModelEpoch, writeModelEpoch, true, true));
        Assert.False(Iec61850ControlInspectionPublication.IsCurrentPostWriteTarget(
            oldMms, Current, writeModelEpoch, writeModelEpoch, true, true));
        Assert.False(Iec61850ControlInspectionPublication.IsCurrentPostWriteTarget(
            currentMms, Current, writeModelEpoch, writeModelEpoch + 1, true, true));
        Assert.False(Iec61850ControlInspectionPublication.IsCurrentPostWriteTarget(
            currentMms, Current, writeModelEpoch, writeModelEpoch, false, true));
        Assert.False(Iec61850ControlInspectionPublication.IsCurrentPostWriteTarget(
            currentMms, Current, writeModelEpoch, writeModelEpoch, true, false));
        Assert.True(Iec61850ControlInspectionPublication.IsCurrentPostWriteTarget(
            currentMms, Current, writeModelEpoch, writeModelEpoch, true, true));
    }

    [Fact]
    public void PostWriteLateCancellationAndFailureNeverRevokeCurrentControlProof()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(
                   Path.Combine(root.FullName, "MainWindow.xaml.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.xaml.cs"));
        var begin = code.IndexOf("private async Task ReinspectConfiguredControlAsync(", StringComparison.Ordinal);
        var end = code.IndexOf("private void RemoveControlFeedbackIndex(", begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin);
        var postWrite = code[begin..end];
        Assert.Contains("expectedMmsAssociation = _runtime.GetControlAssociationId(device.DeviceId)", postWrite);
        Assert.Contains("Iec61850ControlInspectionPublication.IsCurrentPostWriteTarget(", postWrite);
        Assert.Contains("writeAssociation, device.LiveValues.Association", postWrite);
        Assert.Contains("CTL_MODEL_POSTWRITE_STALE_IGNORED", postWrite);
        Assert.Contains("IsCurrentPostWriteInspection);", postWrite);
        Assert.Contains("predicate is evaluated by the facade", postWrite,
            StringComparison.OrdinalIgnoreCase);
        var guardCount = postWrite.Split("if (!IsCurrentPostWriteInspection())").Length - 1;
        Assert.Equal(3, guardCount); // success, cancellation, fault
        var cancelled = postWrite.IndexOf("catch (OperationCanceledException)", StringComparison.Ordinal);
        var failed = postWrite.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(cancelled > 0 && failed > cancelled);
        Assert.True(postWrite.IndexOf("if (!IsCurrentPostWriteInspection())", cancelled,
            StringComparison.Ordinal) < postWrite.IndexOf("signal.ClearLiveControlAuthority();", cancelled,
            StringComparison.Ordinal));
        Assert.True(postWrite.IndexOf("if (!IsCurrentPostWriteInspection())", failed,
            StringComparison.Ordinal) < postWrite.IndexOf("signal.ClearLiveControlAuthority();", failed,
            StringComparison.Ordinal));
        Assert.Contains("if (IsCurrentPostWriteInspection())", postWrite);
    }

    [Fact]
    public void FacadeChecksModelEpochBeforePublishingAndBeforeFailureRevocation()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
               !File.Exists(Path.Combine(root.FullName,
                   "Services/UiResponsiveIec61850MonitorRuntimeFacade.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code = File.ReadAllText(Path.Combine(root!.FullName,
            "Services/UiResponsiveIec61850MonitorRuntimeFacade.cs"));
        var begin = code.IndexOf(
            "public async Task<Iec61850ControlCapabilities> InspectControlAsync(",
            StringComparison.Ordinal);
        var end = code.IndexOf("public Task<AR.Iec61850.Control.Iec61850ControlModelConfigurationResult>",
            begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin);
        var inspect = code[begin..end];
        Assert.Contains("Func<bool>? stillCurrentPublicationTarget = null", inspect);
        Assert.Contains("Control inspection NOT SENT: the selected live model changed", inspect);
        Assert.Contains("!Iec61850ControlInspectionPublication.TryApply(", inspect);
        var apply = inspect.IndexOf("!Iec61850ControlInspectionPublication.TryApply(",
            StringComparison.Ordinal);
        Assert.True(inspect.LastIndexOf("!stillCurrentPublicationTarget()", apply,
            StringComparison.Ordinal) >= 0);
        var catchBegin = inspect.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        var revoke = inspect.IndexOf("signal.ClearLiveControlAuthority();",
            catchBegin, StringComparison.Ordinal);
        Assert.True(catchBegin >= 0 && revoke > catchBegin);
        Assert.True(inspect.IndexOf("stillCurrentPublicationTarget()", catchBegin,
            StringComparison.Ordinal) < revoke);
    }

}
