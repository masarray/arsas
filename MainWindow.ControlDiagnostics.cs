using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.RegularExpressions;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private static readonly TimeSpan CswiPositionDebounce = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan ControlFeedbackStateLifetime = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ControlRelatedReportWindow = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AmbiguousReportNoticeWindow = TimeSpan.FromSeconds(30);

    private static readonly Regex ControlRequestedPattern = new(
        @"Control requested:\s*(?<reference>.+?)\s+value=(?<value>[^;]+);",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private sealed class PendingCswiPositionSnapshot
    {
        public required Iec61850PointSnapshot Latest { get; set; }
        public required string Value { get; set; }
        public CancellationTokenSource Cancellation { get; } = new();
        public object Sync { get; } = new();
    }

    private sealed record StablePositionEvidence(string Value, DateTimeOffset ObservedUtc);

    private sealed class ActivePositionCommand
    {
        public required string Key { get; init; }
        public required SignalDefinition Signal { get; init; }
        public required string BeforeValue { get; init; }
        public required string RequestedValue { get; init; }
        public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
        public bool StableConfirmed { get; set; }
        public bool Restoring { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
    }

    private readonly ConcurrentDictionary<string, PendingCswiPositionSnapshot> _pendingCswiPositionSnapshots =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, StablePositionEvidence> _stablePositionReferences =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _recentControlDiagnosticActivity =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAmbiguousReportNotice =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ActivePositionCommand> _activePositionCommands =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _controlDiagnosticNormalizerInstalled;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_controlDiagnosticNormalizerInstalled)
            return;

        _controlDiagnosticNormalizerInstalled = true;

        // Filter short CSWI command-object echoes before they enter the 100 ms UI batch.
        // Both Open and Closed are debounced because some relays briefly publish the
        // requested value, fall back to the old state, then publish the settled state.
        _runtime.PointUpdated -= Runtime_PointUpdated;
        _runtime.PointUpdated += Runtime_PointUpdatedWithStablePositionFilter;

        // Normalize expected control/report diagnostics before they reach the journal.
        _runtime.Diagnostic -= Runtime_Diagnostic;
        _runtime.Diagnostic += Runtime_DiagnosticWithControlModelClassification;

        InstallCommandPanelUx();
    }

    private void Runtime_PointUpdatedWithStablePositionFilter(Iec61850PointSnapshot snapshot)
    {
        var reference = snapshot.Point.IecReference;
        if (!IsPositionStatusReference(reference))
        {
            Runtime_PointUpdated(snapshot);
            return;
        }

        var key = NormalizeReference(reference);
        var value = NormalizeControlState(snapshot.Value);
        var isBinaryPosition = value.Equals("Open", StringComparison.OrdinalIgnoreCase) ||
                               value.Equals("Closed", StringComparison.OrdinalIgnoreCase);

        if (!isBinaryPosition)
        {
            _stablePositionReferences.TryRemove(key, out _);
            CancelPendingCswiPosition(key);
            Runtime_PointUpdated(snapshot);
            return;
        }

        // XCBR/XSWI are equipment-status objects and remain immediate. CSWI.Pos is the
        // command-facing status object that can expose a short requested-state echo.
        if (!IsCswiPositionStatusReference(reference))
        {
            _stablePositionReferences[key] = new StablePositionEvidence(value, DateTimeOffset.UtcNow);
            Runtime_PointUpdated(snapshot);
            return;
        }

        while (true)
        {
            if (_pendingCswiPositionSnapshots.TryGetValue(key, out var existing))
            {
                lock (existing.Sync)
                {
                    if (existing.Value.Equals(value, StringComparison.OrdinalIgnoreCase))
                    {
                        existing.Latest = snapshot;
                        return;
                    }
                }

                // Opposite value arrived inside the guard window. The first sample was a
                // transient; discard it and begin a fresh stability window for the new one.
                CancelPendingCswiPosition(key);
                continue;
            }

            var pending = new PendingCswiPositionSnapshot
            {
                Latest = snapshot,
                Value = value
            };

            if (!_pendingCswiPositionSnapshots.TryAdd(key, pending))
                continue;

            _ = ReleaseStableCswiPositionAsync(key, pending);
            return;
        }
    }

    private async Task ReleaseStableCswiPositionAsync(string key, PendingCswiPositionSnapshot pending)
    {
        try
        {
            await Task.Delay(CswiPositionDebounce, pending.Cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var removed = ((ICollection<KeyValuePair<string, PendingCswiPositionSnapshot>>)_pendingCswiPositionSnapshots)
            .Remove(new KeyValuePair<string, PendingCswiPositionSnapshot>(key, pending));
        if (!removed)
            return;

        Iec61850PointSnapshot stableSnapshot;
        string stableValue;
        lock (pending.Sync)
        {
            stableSnapshot = pending.Latest;
            stableValue = pending.Value;
        }

        _stablePositionReferences[key] = new StablePositionEvidence(stableValue, DateTimeOffset.UtcNow);
        Runtime_PointUpdated(stableSnapshot);
        pending.Cancellation.Dispose();
    }

    private void CancelPendingCswiPosition(string key)
    {
        if (!_pendingCswiPositionSnapshots.TryRemove(key, out var pending))
            return;

        pending.Cancellation.Cancel();
        pending.Cancellation.Dispose();
    }

    private void Runtime_DiagnosticWithControlModelClassification(DiagnosticEntry entry)
    {
        var message = entry.Message ?? string.Empty;
        ObserveControlRequest(message, entry.Source);

        // A user-requested command is an audit event, not a warning. Repeated WARN rows
        // previously made every normal Open/Close operation look like a fault.
        if (IsControlRequestDiagnostic(message))
        {
            ForwardDiagnostic(entry, "INFO");
            return;
        }

        if (IsControlCompletionDiagnostic(message))
            RememberControlDiagnosticActivity(entry.Source);

        // A verified smart fallback is a successful routing decision. Keep it visible as
        // INFO, but do not raise a warning badge when all selected points are covered.
        if (IsSafeRcbFallbackDiagnostic(message))
        {
            ForwardDiagnostic(entry, "INFO");
            return;
        }

        // Some relays emit a control-related InformationReport without a uniquely usable
        // RptID/DataSet identity around Operate/CommandTermination. The engine correctly
        // refuses unsafe projection. During a known control window this is expected control
        // traffic, so emit one compact INFO notice and coalesce repeats. Outside that window
        // it remains WARN because an unrelated process report may have been discarded.
        if (IsAmbiguousInformationReportDiagnostic(message))
        {
            if (HasRecentControlDiagnosticActivity(entry.Source))
            {
                if (ShouldEmitAmbiguousReportNotice(entry.Source))
                {
                    Runtime_Diagnostic(new DiagnosticEntry
                    {
                        Time = entry.Time,
                        Level = "INFO",
                        Source = entry.Source,
                        Message = "A control-related InformationReport had no unique RptID/DataSet identity and was safely ignored; selected process points remain on their verified report/MMS acquisition paths."
                    });
                }
                return;
            }
        }

        if (IsStatusOnlyControlInspection(message))
        {
            Runtime_Diagnostic(new DiagnosticEntry
            {
                Time = entry.Time,
                Level = "INFO",
                Source = entry.Source,
                Message = $"{ExtractControlReference(message)}: ctlModel=StatusOnly; this is a read-only status object and command actions are disabled."
            });
            return;
        }

        if (IsUnknownControlModelInspection(message))
        {
            Runtime_Diagnostic(new DiagnosticEntry
            {
                Time = entry.Time,
                Level = "WARN",
                Source = entry.Source,
                Message = $"{ExtractControlReference(message)}: ctlModel could not be resolved; command actions remain disabled until the live model is known."
            });
            return;
        }

        Runtime_Diagnostic(entry);
    }

    private void ForwardDiagnostic(DiagnosticEntry entry, string level)
    {
        Runtime_Diagnostic(new DiagnosticEntry
        {
            Time = entry.Time,
            Level = level,
            Source = entry.Source,
            Message = entry.Message
        });
    }

    private void RememberControlDiagnosticActivity(string? source)
    {
        var key = string.IsNullOrWhiteSpace(source) ? "IEC61850" : source.Trim();
        _recentControlDiagnosticActivity[key] = DateTimeOffset.UtcNow;
    }

    private bool HasRecentControlDiagnosticActivity(string? source)
    {
        var key = string.IsNullOrWhiteSpace(source) ? "IEC61850" : source.Trim();
        return _recentControlDiagnosticActivity.TryGetValue(key, out var observedUtc) &&
               DateTimeOffset.UtcNow - observedUtc <= ControlRelatedReportWindow;
    }

    private bool ShouldEmitAmbiguousReportNotice(string? source)
    {
        var key = string.IsNullOrWhiteSpace(source) ? "IEC61850" : source.Trim();
        var now = DateTimeOffset.UtcNow;
        if (_lastAmbiguousReportNotice.TryGetValue(key, out var previous) &&
            now - previous < AmbiguousReportNoticeWindow)
        {
            return false;
        }

        _lastAmbiguousReportNotice[key] = now;
        return true;
    }

    private static bool IsControlRequestDiagnostic(string message)
        => message.Contains("Control requested:", StringComparison.OrdinalIgnoreCase);

    private static bool IsControlCompletionDiagnostic(string message)
        => message.Contains("Control Feedback confirmed:", StringComparison.OrdinalIgnoreCase) ||
           message.Contains("Control rejected:", StringComparison.OrdinalIgnoreCase) ||
           message.Contains("Control Control rejected:", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafeRcbFallbackDiagnostic(string message)
        => message.Contains("Preferred RCB", StringComparison.OrdinalIgnoreCase) &&
           message.Contains("smart fallback selected", StringComparison.OrdinalIgnoreCase) &&
           message.Contains("avoid an unsafe/busy RCB", StringComparison.OrdinalIgnoreCase);

    private static bool IsAmbiguousInformationReportDiagnostic(string message)
        => message.Contains("InformationReport frame(s) were not routed", StringComparison.OrdinalIgnoreCase) &&
           message.Contains("RptID/DataSet identity was ambiguous", StringComparison.OrdinalIgnoreCase) &&
           message.Contains("refused unsafe DataSet projection", StringComparison.OrdinalIgnoreCase);

    private void ObserveControlRequest(string? message, string? source)
    {
        if (string.IsNullOrWhiteSpace(message) || Dispatcher.HasShutdownStarted)
            return;

        var match = ControlRequestedPattern.Match(message);
        if (!match.Success)
            return;

        RememberControlDiagnosticActivity(source);

        var requestedValue = NormalizeControlState(match.Groups["value"].Value);
        if (!requestedValue.Equals("Open", StringComparison.OrdinalIgnoreCase) &&
            !requestedValue.Equals("Closed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() => BeginPositionCommand(
            match.Groups["reference"].Value,
            requestedValue));
    }

    private void BeginPositionCommand(string reference, string requestedValue)
    {
        var key = NormalizeReference(reference);
        var signal = FindCommandSignal(reference);
        if (string.IsNullOrWhiteSpace(key) || signal == null || !IsPositionCommand(signal))
            return;

        RemovePositionCommand(key);

        var state = new ActivePositionCommand
        {
            Key = key,
            Signal = signal,
            BeforeValue = NormalizeControlState(signal.ControlCurrentValue),
            RequestedValue = requestedValue
        };

        PropertyChangedEventHandler handler = (_, args) => HandlePositionCommandPropertyChanged(state, args.PropertyName);
        state.PropertyChangedHandler = handler;
        signal.PropertyChanged += handler;
        _activePositionCommands[key] = state;

        // Evidence from before this command must never confirm the new operation.
        var feedbackKey = ResolveControlFeedbackKey(signal);
        if (!string.IsNullOrWhiteSpace(feedbackKey))
            _stablePositionReferences.TryRemove(feedbackKey, out _);

        _ = ExpirePositionCommandAsync(state);
    }

    private void HandlePositionCommandPropertyChanged(ActivePositionCommand state, string? propertyName)
    {
        if (state.Restoring || !_activePositionCommands.TryGetValue(state.Key, out var active) ||
            !ReferenceEquals(active, state))
        {
            return;
        }

        if (propertyName == nameof(SignalDefinition.ControlCurrentValue))
        {
            var current = NormalizeControlState(state.Signal.ControlCurrentValue);
            if (!current.Equals(state.RequestedValue, StringComparison.OrdinalIgnoreCase))
                return;

            if (HasFreshStableEvidence(state))
            {
                state.StableConfirmed = true;
                SetStableFeedbackResult(state);
                if (!state.Signal.ControlCommandBusy)
                    RemovePositionCommand(state.Key);
                return;
            }

            RestorePreCommandValue(state);
            return;
        }

        if (propertyName == nameof(SignalDefinition.ControlLastResult))
        {
            var result = state.Signal.ControlLastResult ?? string.Empty;
            if (IsControlFailureResult(result))
            {
                RemovePositionCommand(state.Key);
                return;
            }

            if (state.StableConfirmed)
            {
                SetStableFeedbackResult(state);
                return;
            }

            if (!HasFreshStableEvidence(state) &&
                result.Contains("Feedback confirmed", StringComparison.OrdinalIgnoreCase) &&
                result.Contains(state.RequestedValue, StringComparison.OrdinalIgnoreCase))
            {
                SetWaitingForStableFeedbackResult(state);
            }

            return;
        }

        if (propertyName == nameof(SignalDefinition.ControlCommandBusy) &&
            !state.Signal.ControlCommandBusy && state.StableConfirmed)
        {
            SetStableFeedbackResult(state);
            RemovePositionCommand(state.Key);
        }
    }

    private void RestorePreCommandValue(ActivePositionCommand state)
    {
        state.Restoring = true;
        try
        {
            state.Signal.ControlCurrentValue = state.BeforeValue;
            state.Signal.ControlLastResult =
                $"Command accepted — waiting for stable {state.RequestedValue} process feedback…";
        }
        finally
        {
            state.Restoring = false;
        }
    }

    private void SetWaitingForStableFeedbackResult(ActivePositionCommand state)
    {
        state.Restoring = true;
        try
        {
            state.Signal.ControlLastResult =
                $"Command accepted — waiting for stable {state.RequestedValue} process feedback…";
        }
        finally
        {
            state.Restoring = false;
        }
    }

    private void SetStableFeedbackResult(ActivePositionCommand state)
    {
        state.Restoring = true;
        try
        {
            state.Signal.ControlLastResult =
                $"Stable process feedback confirmed: {state.RequestedValue}.";
        }
        finally
        {
            state.Restoring = false;
        }
    }

    private bool HasFreshStableEvidence(ActivePositionCommand state)
    {
        var feedbackKey = ResolveControlFeedbackKey(state.Signal);
        return !string.IsNullOrWhiteSpace(feedbackKey) &&
               _stablePositionReferences.TryGetValue(feedbackKey, out var evidence) &&
               evidence.ObservedUtc >= state.StartedUtc &&
               evidence.Value.Equals(state.RequestedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveControlFeedbackKey(SignalDefinition signal)
    {
        // P4E fail-closed contract: stable command confirmation is allowed only when live
        // control discovery supplied an explicit status reference. Never infer .stVal from
        // ObjectReference, SignalName, row order, or any runtime identifier.
        if (string.IsNullOrWhiteSpace(signal.ControlStatusReference))
            return string.Empty;

        return NormalizeReference(signal.ControlStatusReference);
    }

    private async Task ExpirePositionCommandAsync(ActivePositionCommand state)
    {
        await Task.Delay(ControlFeedbackStateLifetime).ConfigureAwait(false);
        if (Dispatcher.HasShutdownStarted)
            return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (!_activePositionCommands.TryGetValue(state.Key, out var active) || !ReferenceEquals(active, state))
                return;

            RemovePositionCommand(state.Key);
            if (!state.StableConfirmed)
            {
                state.Signal.ControlLastResult =
                    $"Command accepted, but stable {state.RequestedValue} process feedback was not confirmed within {ControlFeedbackStateLifetime.TotalSeconds:0} s.";
            }
        });
    }

    private SignalDefinition? FindCommandSignal(string reference)
    {
        var normalized = NormalizeReference(reference);
        return Devices
            .SelectMany(device => device.CommandSignals)
            .FirstOrDefault(signal => NormalizeReference(signal.ObjectReference)
                .Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private void RemovePositionCommand(string key)
    {
        if (!_activePositionCommands.Remove(key, out var state))
            return;

        if (state.PropertyChangedHandler != null)
            state.Signal.PropertyChanged -= state.PropertyChangedHandler;
    }

    private static bool IsPositionCommand(SignalDefinition signal)
    {
        var reference = (signal.ObjectReference ?? string.Empty).Trim().Replace('$', '.').TrimEnd('.');
        return reference.EndsWith(".Pos", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPositionStatusReference(string? reference)
    {
        var normalized = (reference ?? string.Empty).Trim().Replace('$', '.').TrimEnd('.');
        return normalized.EndsWith(".Pos.stVal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCswiPositionStatusReference(string? reference)
    {
        var normalized = (reference ?? string.Empty).Trim().Replace('$', '.');
        if (!normalized.EndsWith(".Pos.stVal", StringComparison.OrdinalIgnoreCase))
            return false;

        var slash = normalized.LastIndexOf('/');
        var afterSlash = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        var dot = afterSlash.IndexOf('.');
        var logicalNode = dot > 0 ? afterSlash[..dot] : afterSlash;
        return logicalNode.Contains("CSWI", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsControlFailureResult(string result)
        => result.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
           result.Contains("rejected", StringComparison.OrdinalIgnoreCase) ||
           result.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
           result.Contains("unsupported", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeControlState(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        if (text.Contains("closed", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("close", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("10", StringComparison.OrdinalIgnoreCase))
        {
            return "Closed";
        }

        if (text.Contains("open", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("01", StringComparison.OrdinalIgnoreCase))
        {
            return "Open";
        }

        if (text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            return "True";
        }

        if (text.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            return "False";
        }

        var bracket = text.IndexOf('[');
        return bracket > 0 ? text[..bracket].Trim() : text;
    }

    private static bool IsStatusOnlyControlInspection(string? message)
        => !string.IsNullOrWhiteSpace(message) &&
           message.Contains("Control inspection failed", StringComparison.OrdinalIgnoreCase) &&
           (message.Contains("ctlModel=StatusOnly", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ctlModel=Status only", StringComparison.OrdinalIgnoreCase));

    private static bool IsUnknownControlModelInspection(string? message)
        => !string.IsNullOrWhiteSpace(message) &&
           message.Contains("Control inspection failed", StringComparison.OrdinalIgnoreCase) &&
           message.Contains("ctlModel=Unknown", StringComparison.OrdinalIgnoreCase);

    private static string ExtractControlReference(string message)
    {
        const string marker = "Control inspection failed for ";
        var start = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return "IEC 61850 control object";

        start += marker.Length;
        var end = message.IndexOf(':', start);
        var reference = end > start ? message[start..end] : message[start..];
        return string.IsNullOrWhiteSpace(reference) ? "IEC 61850 control object" : reference.Trim();
    }
}
