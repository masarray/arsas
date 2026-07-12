using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private const double ImmediatePositionFeedbackEchoThresholdMs = 150d;
    private static readonly TimeSpan StableFeedbackGuardWindow = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan StableFeedbackStateLifetime = TimeSpan.FromSeconds(15);

    private static readonly Regex ControlRequestedPattern = new(
        @"Control requested:\s*(?<reference>.+?)\s+value=(?<value>[^;]+);",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ControlFeedbackPattern = new(
        @"Control Feedback confirmed:\s*(?<reference>[^;]+);.*?requested=(?<requested>[^;]+);\s*feedback=(?<feedbackValue>[^;]+);.*?\bfeedback=(?<feedbackMs>\d+(?:[\.,]\d+)?)\s*ms(?:;|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private sealed class ControlFeedbackStabilityState
    {
        public required string Key { get; init; }
        public required string Reference { get; init; }
        public required SignalDefinition Signal { get; init; }
        public required string BeforeValue { get; init; }
        public required string RequestedValue { get; set; }
        public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? EchoSuppressedAtUtc { get; set; }
        public bool SuppressImmediateTarget { get; set; }
        public bool SawNonTargetAfterEcho { get; set; }
        public bool Restoring { get; set; }
        public PropertyChangedEventHandler? PropertyChangedHandler { get; set; }
    }

    private readonly Dictionary<string, ControlFeedbackStabilityState> _controlFeedbackStability =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _controlDiagnosticNormalizerInstalled;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_controlDiagnosticNormalizerInstalled)
            return;

        _controlDiagnosticNormalizerInstalled = true;

        // The native Smart Control service deliberately rejects ctlModel=StatusOnly
        // when asked to open an executable command session. For the explorer this is
        // valid live-model information, not a communication failure. Replace the
        // original diagnostic subscriber after the window is ready so status-only
        // objects do not raise a red application error.
        _runtime.Diagnostic -= Runtime_Diagnostic;
        _runtime.Diagnostic += Runtime_DiagnosticWithControlModelClassification;
    }

    private void Runtime_DiagnosticWithControlModelClassification(DiagnosticEntry entry)
    {
        ObserveControlFeedbackDiagnostic(entry);

        if (IsStatusOnlyControlInspection(entry.Message))
        {
            Runtime_Diagnostic(new DiagnosticEntry
            {
                Time = entry.Time,
                Level = "INFO",
                Source = entry.Source,
                Message = $"{ExtractControlReference(entry.Message)}: ctlModel=StatusOnly; this is a read-only status object and command actions are disabled."
            });
            return;
        }

        if (IsUnknownControlModelInspection(entry.Message))
        {
            Runtime_Diagnostic(new DiagnosticEntry
            {
                Time = entry.Time,
                Level = "WARN",
                Source = entry.Source,
                Message = $"{ExtractControlReference(entry.Message)}: ctlModel could not be resolved; command actions remain disabled until the live model is known."
            });
            return;
        }

        Runtime_Diagnostic(entry);
    }

    private void ObserveControlFeedbackDiagnostic(DiagnosticEntry entry)
    {
        var message = entry.Message ?? string.Empty;
        if (message.Length == 0 || Dispatcher.HasShutdownStarted)
            return;

        var requested = ControlRequestedPattern.Match(message);
        if (requested.Success)
        {
            _ = Dispatcher.InvokeAsync(() => BeginControlFeedbackStability(
                requested.Groups["reference"].Value,
                requested.Groups["value"].Value));
            return;
        }

        var feedback = ControlFeedbackPattern.Match(message);
        if (!feedback.Success)
            return;

        _ = Dispatcher.InvokeAsync(() => ClassifyImmediateControlFeedback(
            feedback.Groups["reference"].Value,
            feedback.Groups["requested"].Value,
            feedback.Groups["feedbackValue"].Value,
            feedback.Groups["feedbackMs"].Value));
    }

    private void BeginControlFeedbackStability(string reference, string requestedValue)
    {
        var key = NormalizeReference(reference);
        var signal = FindCommandSignal(reference);
        if (string.IsNullOrWhiteSpace(key) || signal == null)
            return;

        RemoveControlFeedbackStability(key);

        var state = new ControlFeedbackStabilityState
        {
            Key = key,
            Reference = reference.Trim(),
            Signal = signal,
            BeforeValue = NormalizeControlState(signal.ControlCurrentValue),
            RequestedValue = NormalizeControlState(requestedValue)
        };

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(SignalDefinition.ControlCurrentValue))
                HandleControlCurrentValueChanged(state);
        };
        state.PropertyChangedHandler = handler;
        signal.PropertyChanged += handler;
        _controlFeedbackStability[key] = state;
        _ = ExpireControlFeedbackStabilityAsync(state);
    }

    private void ClassifyImmediateControlFeedback(
        string reference,
        string requestedValue,
        string feedbackValue,
        string feedbackMilliseconds)
    {
        var key = NormalizeReference(reference);
        if (!_controlFeedbackStability.TryGetValue(key, out var state))
        {
            BeginControlFeedbackStability(reference, requestedValue);
            _controlFeedbackStability.TryGetValue(key, out state);
        }

        if (state == null)
            return;

        state.RequestedValue = NormalizeControlState(requestedValue);
        var observedValue = NormalizeControlState(feedbackValue);
        var millisecondsText = feedbackMilliseconds.Replace(',', '.');
        if (!double.TryParse(millisecondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var elapsedMs))
            return;

        // A position Close that is reported within only a few milliseconds is normally
        // the CSWI command-object echo, not settled primary-equipment feedback. The live
        // report/poll stream remains authoritative for the stable final state. Open is
        // intentionally not delayed because this relay reports genuine Open feedback fast.
        state.SuppressImmediateTarget =
            IsPositionCommand(state.Signal) &&
            state.RequestedValue.Equals("Closed", StringComparison.OrdinalIgnoreCase) &&
            observedValue.Equals(state.RequestedValue, StringComparison.OrdinalIgnoreCase) &&
            !state.BeforeValue.Equals(state.RequestedValue, StringComparison.OrdinalIgnoreCase) &&
            elapsedMs <= ImmediatePositionFeedbackEchoThresholdMs;
    }

    private void HandleControlCurrentValueChanged(ControlFeedbackStabilityState state)
    {
        if (state.Restoring || !_controlFeedbackStability.TryGetValue(state.Key, out var currentState) ||
            !ReferenceEquals(state, currentState))
        {
            return;
        }

        var currentValue = NormalizeControlState(state.Signal.ControlCurrentValue);
        var isRequestedTarget = currentValue.Equals(state.RequestedValue, StringComparison.OrdinalIgnoreCase);

        if (!isRequestedTarget)
        {
            if (state.EchoSuppressedAtUtc.HasValue)
                state.SawNonTargetAfterEcho = true;
            return;
        }

        if (state.SuppressImmediateTarget)
        {
            state.SuppressImmediateTarget = false;
            SuppressCommandObjectEcho(state);
            return;
        }

        if (state.EchoSuppressedAtUtc.HasValue &&
            !state.SawNonTargetAfterEcho &&
            DateTimeOffset.UtcNow - state.EchoSuppressedAtUtc.Value < StableFeedbackGuardWindow)
        {
            SuppressCommandObjectEcho(state);
            return;
        }

        CompleteStableControlFeedback(state, currentValue);
    }

    private void SuppressCommandObjectEcho(ControlFeedbackStabilityState state)
    {
        state.EchoSuppressedAtUtc ??= DateTimeOffset.UtcNow;
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

    private void CompleteStableControlFeedback(ControlFeedbackStabilityState state, string currentValue)
    {
        var hadSuppressedEcho = state.EchoSuppressedAtUtc.HasValue;
        RemoveControlFeedbackStability(state.Key);
        if (hadSuppressedEcho)
            state.Signal.ControlLastResult = $"Stable process feedback confirmed: {currentValue}.";
    }

    private async Task ExpireControlFeedbackStabilityAsync(ControlFeedbackStabilityState state)
    {
        await Task.Delay(StableFeedbackStateLifetime).ConfigureAwait(false);
        if (Dispatcher.HasShutdownStarted)
            return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (!_controlFeedbackStability.TryGetValue(state.Key, out var active) || !ReferenceEquals(active, state))
                return;

            var hadSuppressedEcho = state.EchoSuppressedAtUtc.HasValue;
            RemoveControlFeedbackStability(state.Key);
            if (hadSuppressedEcho)
            {
                state.Signal.ControlLastResult =
                    $"Command accepted, but stable {state.RequestedValue} process feedback was not confirmed within {StableFeedbackStateLifetime.TotalSeconds:0} s.";
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

    private void RemoveControlFeedbackStability(string key)
    {
        if (!_controlFeedbackStability.Remove(key, out var state))
            return;

        if (state.PropertyChangedHandler != null)
            state.Signal.PropertyChanged -= state.PropertyChangedHandler;
    }

    private static bool IsPositionCommand(SignalDefinition signal)
    {
        var reference = (signal.ObjectReference ?? string.Empty).Trim().Replace('$', '.').TrimEnd('.');
        return reference.EndsWith(".Pos", StringComparison.OrdinalIgnoreCase);
    }

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
