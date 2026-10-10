namespace ArIED61850Tester.Services;

public sealed record GooseTimelineSignature(string Payload, string State, string Diagnostics, string Sequence);
public readonly record struct GooseTimelineDecision(bool Include, bool IsNew, bool PayloadChanged, bool StateChanged, bool DiagnosticsChanged, bool NewSequenceAnomaly);

/// <summary>Presentation-only filter. All packets still enter the read-only wire capture.</summary>
public static class GooseTimelineEventPolicy
{
    // Coalesce per-frame quantitative diagnostics into stable semantic
    // categories FOR UI ONLY. Raw wire evidence is preserved in the inspector.
    public static string DiagnosticCategory(IEnumerable<string> diagnostics)
    {
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in diagnostics)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            if (item.Contains("test flag", StringComparison.OrdinalIgnoreCase))
                categories.Add("test-flag");
            else if (item.Contains("supervision expired", StringComparison.OrdinalIgnoreCase))
                categories.Add("supervision-expired");
            else if (item.Contains("sequence gap", StringComparison.OrdinalIgnoreCase))
                categories.Add("sequence-gap");
            else
                categories.Add(item.Trim());
        }
        return string.Join("|", categories.OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
    }

    private static string SequenceFamily(string value)
    {
        if (value.Contains("retransmission", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("normal", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("new", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("first", StringComparison.OrdinalIgnoreCase))
            return "normal";
        return value.Trim().ToLowerInvariant();
    }

    public static GooseTimelineDecision Evaluate(GooseTimelineSignature? previous, GooseTimelineSignature current, bool showRetransmissions)
    {
        ArgumentNullException.ThrowIfNull(current);
        var first = previous is null;
        var valueChanged = !first && previous!.Payload != current.Payload;
        var stateChanged = !first && previous!.State != current.State;
        var diagnosticChanged = !first && previous!.Diagnostics != current.Diagnostics;
        var normal = current.Sequence.Contains("Retransmission", StringComparison.OrdinalIgnoreCase) ||
                     current.Sequence.Contains("Normal", StringComparison.OrdinalIgnoreCase);
        var newAnomaly = !first && SequenceFamily(previous!.Sequence) != SequenceFamily(current.Sequence) &&
                         !normal && !string.IsNullOrWhiteSpace(current.Sequence);
        return new GooseTimelineDecision(first || valueChanged || stateChanged || diagnosticChanged || newAnomaly || showRetransmissions,
            first, valueChanged, stateChanged, diagnosticChanged, newAnomaly);
    }
}
