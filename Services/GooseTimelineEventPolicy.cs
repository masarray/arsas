namespace ArIED61850Tester.Services;

public sealed record GooseTimelineSignature(string Payload, string State, string Diagnostics, string Sequence);
public readonly record struct GooseTimelineDecision(bool Include, bool IsNew, bool PayloadChanged, bool StateChanged, bool DiagnosticsChanged, bool NewSequenceAnomaly);

/// <summary>Presentation-only filter. All packets still enter the read-only wire capture.</summary>
public static class GooseTimelineEventPolicy
{
    public static GooseTimelineDecision Evaluate(GooseTimelineSignature? previous, GooseTimelineSignature current, bool showRetransmissions)
    {
        ArgumentNullException.ThrowIfNull(current);
        var first = previous is null;
        var valueChanged = !first && previous!.Payload != current.Payload;
        var stateChanged = !first && previous!.State != current.State;
        var diagnosticChanged = !first && previous!.Diagnostics != current.Diagnostics;
        var normal = current.Sequence.Contains("Retransmission", StringComparison.OrdinalIgnoreCase) ||
                     current.Sequence.Contains("Normal", StringComparison.OrdinalIgnoreCase);
        var newAnomaly = !first && previous!.Sequence != current.Sequence && !normal;
        return new GooseTimelineDecision(first || valueChanged || stateChanged || diagnosticChanged || newAnomaly || showRetransmissions,
            first, valueChanged, stateChanged, diagnosticChanged, newAnomaly);
    }
}
