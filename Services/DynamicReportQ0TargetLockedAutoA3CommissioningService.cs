using System.Reflection;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Field-bounded G2.6-P1 coordinator for the already-proven AA1C1F08R4 Q0 CSWI1.Pos
/// control object. Ctrl+Shift+A is the explicit commissioning action; after every
/// identity/profile/control/report gate closes, this coordinator dispatches exactly one
/// OPEN through the existing Iec61850MonitorRuntime control path. It never retries,
/// toggles, sends CLOSE, or restores the breaker automatically.
///
/// The existing deterministic A3 service remains authoritative for the dchg-only report
/// transaction and exact DataSet-index correlation. This coordinator only removes the
/// operator timing race and target-selection ambiguity discovered during physical P1.
/// </summary>
internal sealed class DynamicReportQ0TargetLockedAutoA3CommissioningService
{
    internal const string ExpectedStableIdentity = "ied:AA1C1F08R4";
    internal const string ExpectedModelFingerprint = "sha256:50c691318c6d6a16b68b121ac48627c26e6e32b937836d559dca1b9eb559f0d9";
    internal const string TargetControlReference = "AA1C1F08R4Q0/CSWI1.Pos";
    internal const string TargetStatusReference = "AA1C1F08R4Q0/CSWI1.Pos.stVal";
    internal const string AutoStimulusValue = "Open";

    private const string AutoOriginator = "ARSAS-G2.6-P1-A3";
    private const string AutoOriginCategory = "StationControl";

    public async Task<DynamicReportCommandBoundA3CommissioningResult> RunAsync(
        Iec61850MonitorRuntime runtime,
        Iec61850MonitorDevice device,
        IReadOnlyList<SignalDefinition> fullModelSignals,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(fullModelSignals);

        var identity = DynamicReportQualificationIdentity.Build(device, fullModelSignals);
        if (!identity.StableIdentityKey.Equals(ExpectedStableIdentity, StringComparison.OrdinalIgnoreCase) ||
            !identity.ModelFingerprint.Equals(ExpectedModelFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Q0 target-locked A3 is field-bounded to {ExpectedStableIdentity} / {ExpectedModelFingerprint}. " +
                $"Connected identity is {identity.StableIdentityKey} / {identity.ModelFingerprint}. No control command was sent.");
        }

        var target = fullModelSignals.SingleOrDefault(signal =>
            SameUserReference(signal.ObjectReference, TargetControlReference));
        if (target is null)
            throw new InvalidOperationException($"Exact A3 target {TargetControlReference} is absent from the live model. No control command was sent.");
        if (!target.IsControlSignal)
            throw new InvalidOperationException($"Exact A3 target {TargetControlReference} is not a live ARSAS control signal. No control command was sent.");
        if (!SameUserReference(target.ControlStatusReference, TargetStatusReference))
        {
            throw new InvalidOperationException(
                $"Exact A3 target status mismatch. Expected {TargetStatusReference}; live ControlStatusReference={TextOrDash(target.ControlStatusReference)}. No control command was sent.");
        }
        if (target.ControlCommandBusy)
            throw new InvalidOperationException($"Exact A3 target {TargetControlReference} is already busy. No additional control command was sent.");

        progress?.Report($"G2.6-P1 Q0 AUTO: target locked to {TargetControlReference}; validating existing ARSAS control semantics before any A3 report mutation…");
        await RequireClosedOperationalTargetAsync(runtime, device, target, "initial preflight", cancellationToken).ConfigureAwait(false);

        // Existing field recovery is intentionally reused, but with a cloned model whose
        // command-focus surface exposes only Q0. Identity-significant signal properties are
        // unchanged, so the exact persisted profile remains identity-compatible. Originals
        // are never mutated and the normal ARSAS command panel/runtime keep their full model.
        var recoverySignals = CreateTargetScopedRecoveryModel(fullModelSignals);
        var scopedIdentity = DynamicReportQualificationIdentity.Build(device, recoverySignals);
        if (!scopedIdentity.StableIdentityKey.Equals(identity.StableIdentityKey, StringComparison.OrdinalIgnoreCase) ||
            !scopedIdentity.ModelFingerprint.Equals(identity.ModelFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Target-scoped recovery changed identity-significant model evidence. Recovery and control were blocked.");
        }

        var recovery = new DynamicReportCommandFocusRequalificationCommissioningService();
        progress?.Report($"G2.6-P1 Q0 AUTO: READ-ONLY exact-profile assessment for {TargetStatusReference}…");
        var assessment = await recovery.AssessAsync(device, recoverySignals, cancellationToken).ConfigureAwait(false);
        if (!assessment.IsSuccess)
            throw new InvalidOperationException(assessment.Summary + " No control command was sent.");

        if (assessment.RequiresRequalification)
        {
            progress?.Report("G2.6-P1 Q0 AUTO: Q0 is absent from the exact G2.4 envelope; running transactional staging recovery automatically. ZERO control commands are permitted during recovery…");
            var recoveryResult = await recovery.RunAsync(
                device,
                recoverySignals,
                progress,
                cancellationToken).ConfigureAwait(false);
            if (!recoveryResult.IsSuccess || !recoveryResult.LiveProfileReplaced || !recoveryResult.FreshCleanupClosureSucceeded)
            {
                throw new InvalidOperationException(
                    recoveryResult.Summary + " The previous live profile remains authoritative and no control command was sent.");
            }

            progress?.Report("G2.6-P1 Q0 AUTO: staged recovery PASS; independently re-checking the exact Q0 command-focus invariant…");
            var postRecovery = await recovery.AssessAsync(device, recoverySignals, cancellationToken).ConfigureAwait(false);
            if (!postRecovery.IsSuccess || postRecovery.RequiresRequalification)
            {
                throw new InvalidOperationException(
                    "Q0 recovery completed, but the independent post-recovery exact-target assessment did not close. No control command was sent. " +
                    postRecovery.Summary);
            }
        }

        // Re-read immediately before the mutating report transaction. OPEN is permitted
        // only from an exact Closed state; Open/intermediate/unknown never causes a toggle.
        await RequireClosedOperationalTargetAsync(runtime, device, target, "post-recovery pre-arm", cancellationToken).ConfigureAwait(false);

        using var a3Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<Iec61850ControlCommandResult>? autoCommandTask = null;
        Exception? readyGateFailure = null;
        var autoDispatchStarted = 0;

        var immediateProgress = new ImmediateProgress(text =>
        {
            if (!text.StartsWith(DynamicReportCommandBoundDataChangeCommissioningService.ReadyMarker, StringComparison.Ordinal))
            {
                progress?.Report(text);
                return;
            }

            if (Interlocked.CompareExchange(ref autoDispatchStarted, 1, 0) != 0)
                return;

            progress?.Report($"G2.6-P1 A3 AUTO READY — exact target {TargetControlReference}; re-validating Closed then dispatching ONE OPEN through the existing ARSAS control path. Do not press OPEN/CLOSE manually.");
            autoCommandTask = DispatchOneShotOpenAsync(
                runtime,
                device,
                target,
                progress,
                ex =>
                {
                    readyGateFailure = ex;
                    try
                    {
                        a3Cancellation.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // The A3 transaction already closed; no retry is ever attempted.
                    }
                },
                a3Cancellation.Token);
        });

        progress?.Report("G2.6-P1 Q0 AUTO: Q0 command-focus gate closed; arming the existing dchg-only A3 report transaction. The one-shot OPEN will be dispatched only after the final read-only baseline is ready…");
        var a3 = new DynamicReportCommandBoundDataChangeCommissioningService();
        DynamicReportCommandBoundA3CommissioningResult result;
        try
        {
            result = await a3.RunAsync(
                runtime,
                device,
                fullModelSignals,
                immediateProgress,
                a3Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (readyGateFailure is not null && !cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                "Q0 auto-stimulus READY gate failed before control dispatch. A3 was cancelled so it would not wait for a command that was deliberately blocked. No retry was attempted.",
                readyGateFailure);
        }

        if (autoCommandTask is not null)
        {
            try
            {
                var command = await autoCommandTask.ConfigureAwait(false);
                progress?.Report(
                    $"G2.6-P1 Q0 AUTO command completed: success={command.IsSuccess}; accepted={command.ServiceAccepted}; feedback={command.FeedbackConfirmed}; termination={command.CommandTerminationReceived}/{command.PositiveTermination}; stage={command.Stage}. No retry, CLOSE, toggle, or auto-restore will be issued.");
            }
            catch (OperationCanceledException) when (a3Cancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                progress?.Report("G2.6-P1 Q0 AUTO command task was cancelled by the fail-closed A3 coordinator. No retry was attempted.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
            {
                // Runtime wire evidence plus the A3 physical transition/report correlation
                // remain authoritative. Never retry an ambiguous physical command.
                progress?.Report($"G2.6-P1 Q0 AUTO command returned {ex.GetType().Name}: {ex.Message}. No retry was attempted; A3 evidence remains fail-closed.");
            }
        }
        else if (!result.IsBlocked)
        {
            progress?.Report("G2.6-P1 Q0 AUTO: A3 never reached its final READY handoff, therefore zero control commands were sent.");
        }

        return result;
    }

    private static async Task DispatchOneShotOpenAsync(
        Iec61850MonitorRuntime runtime,
        Iec61850MonitorDevice device,
        SignalDefinition target,
        IProgress<string>? progress,
        Action<Exception> failBeforeDispatch,
        CancellationToken cancellationToken)
    {
        // Re-inspect after the A3 final witness baseline. This closes the time-of-check /
        // time-of-use gap: the service never turns "current state" into an automatic toggle.
        Iec61850ControlCapabilities capabilities;
        try
        {
            capabilities = await runtime.InspectControlAsync(device.DeviceId, target, cancellationToken).ConfigureAwait(false);
            ValidateClosedOperationalTarget(capabilities, "A3 READY recheck");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException or TimeoutException)
        {
            failBeforeDispatch(ex);
            return;
        }

        if (target.ControlCommandBusy)
        {
            failBeforeDispatch(new InvalidOperationException($"Exact A3 target {TargetControlReference} became busy at READY. No control command was sent."));
            return;
        }

        var request = new Iec61850ControlCommandRequest
        {
            Signal = target,
            ValueText = AutoStimulusValue,
            InterlockCheck = true,
            SynchroCheck = false,
            TestMode = false,
            Originator = AutoOriginator,
            OriginCategory = AutoOriginCategory,
            FeedbackTimeoutMs = 12000,
            CommandTerminationTimeoutMs = 10000
        };

        progress?.Report($"G2.6-P1 Q0 AUTO DISPATCH: {TargetControlReference} -> {AutoStimulusValue}; interlock=true; synchro=false; test=false; one-shot=true; retry=false.");

        // IMPORTANT: call the existing runtime method directly. Its already-existing
        // "Control execution requested:" diagnostic is emitted synchronously before the
        // native ARIEC control await, so the armed A3 witness captures the exact request.
        // No separate SBO/SBOw/Operate implementation exists here.
        await runtime.ExecuteControlAsync(device.DeviceId, request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RequireClosedOperationalTargetAsync(
        Iec61850MonitorRuntime runtime,
        Iec61850MonitorDevice device,
        SignalDefinition target,
        string phase,
        CancellationToken cancellationToken)
    {
        var capabilities = await runtime.InspectControlAsync(device.DeviceId, target, cancellationToken).ConfigureAwait(false);
        ValidateClosedOperationalTarget(capabilities, phase);
    }

    private static void ValidateClosedOperationalTarget(Iec61850ControlCapabilities capabilities, string phase)
    {
        if (!SameUserReference(capabilities.ObjectReference, TargetControlReference))
        {
            throw new InvalidOperationException(
                $"{phase}: control inspection returned {TextOrDash(capabilities.ObjectReference)} instead of exact target {TargetControlReference}. No control command was sent.");
        }

        if (!capabilities.SupportsOperate || !capabilities.IsOperationallyReady)
        {
            throw new InvalidOperationException(
                $"{phase}: exact target is not operationally ready for the existing ARSAS control service; model={capabilities.ControlModelText}. No control command was sent.");
        }

        if (!capabilities.CurrentState.Equals("Closed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{phase}: exact target must be Closed before the one-shot OPEN stimulus. CurrentState={TextOrDash(capabilities.CurrentState)}, CurrentValue={TextOrDash(capabilities.CurrentValue)}. No CLOSE/toggle/restore command is allowed.");
        }
    }

    private static SignalDefinition[] CreateTargetScopedRecoveryModel(IReadOnlyList<SignalDefinition> fullModelSignals)
    {
        var cloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)
                          ?? throw new InvalidOperationException("MemberwiseClone is unavailable; target-scoped recovery cannot be isolated safely.");
        var statusProperty = typeof(SignalDefinition).GetProperty(
            nameof(SignalDefinition.ControlStatusReference),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SignalDefinition.ControlStatusReference is unavailable; target-scoped recovery cannot be isolated safely.");
        var statusSetter = statusProperty.GetSetMethod(nonPublic: true);
        var backingField = typeof(SignalDefinition).GetField(
            $"<{nameof(SignalDefinition.ControlStatusReference)}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (statusSetter is null && backingField is null)
            throw new InvalidOperationException("ControlStatusReference cannot be changed on a private clone; target-scoped recovery was blocked.");

        var clones = new SignalDefinition[fullModelSignals.Count];
        for (var index = 0; index < fullModelSignals.Count; index++)
        {
            var clone = (SignalDefinition)(cloneMethod.Invoke(fullModelSignals[index], null)
                        ?? throw new InvalidOperationException("Signal clone failed; target-scoped recovery was blocked."));

            if (clone.IsControlSignal &&
                !SameUserReference(clone.ObjectReference, TargetControlReference) &&
                !string.IsNullOrWhiteSpace(clone.ControlStatusReference))
            {
                if (statusSetter is not null)
                    statusSetter.Invoke(clone, [string.Empty]);
                else
                    backingField!.SetValue(clone, string.Empty);
            }

            clones[index] = clone;
        }

        var scopedCommands = clones
            .Where(signal => signal.IsControlSignal && !string.IsNullOrWhiteSpace(signal.ControlStatusReference))
            .ToArray();
        if (scopedCommands.Length != 1 || !SameUserReference(scopedCommands[0].ObjectReference, TargetControlReference))
        {
            throw new InvalidOperationException(
                $"Target-scoped recovery model must expose exactly one control focus ({TargetControlReference}); resolved={string.Join(", ", scopedCommands.Select(signal => signal.ObjectReference))}. No recovery/control mutation was attempted.");
        }

        return clones;
    }

    private static bool SameUserReference(string? left, string? right)
        => NormalizeUserReference(left).Equals(NormalizeUserReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeUserReference(string? value)
        => (value ?? string.Empty).Trim().Replace('$', '.');

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private sealed class ImmediateProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value ?? string.Empty);
    }
}