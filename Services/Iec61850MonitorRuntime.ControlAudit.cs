using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed partial class Iec61850MonitorRuntime
{
    /// <summary>
    /// WPF's pre-dispatch local rejection must be visible as NOT SENT too.
    /// This is observational only; no extra control is sent, and only the
    /// currently owned session is allowed to produce the record.
    /// </summary>
    public void RecordUnsentOperatorControl(
        string deviceId, SignalDefinition signal, string requestedValue,
        string reason, bool interlockCheck, bool synchroCheck, bool testMode)
    {
        if (!_sessions.TryGetValue(deviceId, out var session) ||
            session.ControlLifetimeCancellation.IsCancellationRequested)
            return;
        PublishControlAudit(session, new Iec61850ControlCommandRequest
        {
            Signal = signal,
            ValueText = requestedValue,
            InterlockCheck = interlockCheck,
            SynchroCheck = synchroCheck,
            TestMode = testMode
        }, "Control not sent", null, "Local operator admission: " + reason);
    }

    /// <summary>
    /// One terminal operator audit per physical control flight, emitted into
    /// the existing EventRaised source and sequenced with SOE. It does not
    /// manufacture a process state, IED timestamp, quality or report identity.
    /// All interlock/synchro/Test flags are REQUESTED checks, not evidence that
    /// the remote IED evaluated or accepted those conditions.
    /// </summary>
    private void PublishControlAudit(
        DeviceSession session, Iec61850ControlCommandRequest request,
        string outcome, Iec61850ControlCommandResult? result, string detail)
    {
        var command = request.Signal;
        EventRaised?.Invoke(new Iec61850EventEntry
        {
            Sequence = Interlocked.Increment(ref _eventSequence),
            EventKind = "Control",
            ClientEventUtc = DateTimeOffset.UtcNow,
            DeviceId = session.Device.DeviceId,
            DeviceName = session.Device.Name,
            IpAddress = session.Device.IpAddress,
            PointKey = session.Device.DeviceId + "|CONTROL|" + command.ObjectReference,
            IecReference = command.ObjectReference,
            SignalName = command.Name,
            IecDataType = command.ControlCdc,
            DeviceTimestamp = "-",
            OldValue = "-",
            NewValue = request.ValueText,
            Quality = string.Empty, // no IEC process quality exists for an operator command
            SourceMode = "Operator control",
            Reason = detail,
            ControlOutcome = outcome,
            ControlEvidence = result is not null
                ? Iec61850ControlAuditPolicy.Evidence(result)
                : detail,
            ControlChecks = $"InterlockCheck requested={request.InterlockCheck}; " +
                            $"SynchroCheck requested={request.SynchroCheck}; " +
                            $"Test requested={request.TestMode}",
            ControlOrigin = request.OriginCategory + "/" + request.Originator,
            ControlError = result?.ControlError ?? string.Empty,
            ControlAddCause = result?.AddCause ?? string.Empty,
            ControlCompletionState = result?.CompletionState ?? string.Empty,
            ControlFlightSequence = result?.ControlNumber ?? string.Empty,
            ControlFeedback = result is { FeedbackConfirmed: true }
                ? result.FeedbackValue : string.Empty
        });
    }
}
