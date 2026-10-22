using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// IEC 61850 command identity is the full DataObject reference within one
/// IED, not a DataSet membership, RCB or WPF SignalDefinition instance.
/// </summary>
public static class Iec61850ControlIdentity
{
    public static string Normalize(string? value)
        => (value ?? string.Empty).Trim().Replace('\\', '/').Replace('$', '.');

    public static int CountSelected(IEnumerable<SignalDefinition> signals)
        => signals.Where(s => s.IsSelected && s.IsValidControlObject)
            .Select(s => Normalize(s.ObjectReference))
            .Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    // Catalog membership and operational authorization are separate concerns.
    public static SignalDefinition[] DistinctSelected(IEnumerable<SignalDefinition> signals)
        => signals.Where(s => s.IsSelected && s.IsValidControlObject)
            .GroupBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(s => s.ControlCommandBusy)
                .ThenByDescending(s => s.ControlConfirmationPending)
                .ThenByDescending(s => s.ControlSupportsOperate)
                .ThenByDescending(s => s.ControlModelResolved).First())
            .ToArray();

    public static SignalDefinition[] DistinctOperable(IEnumerable<SignalDefinition> signals)
        => signals.Where(s => s.IsSelected && s.IsValidControlObject &&
                              s.ControlModelResolved && s.ControlSupportsOperate && !s.IsGenericControl)
            .GroupBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            // Prefer an in-flight owner in legacy cached duplicate models.
            .Select(group => group.OrderByDescending(s => s.ControlCommandBusy)
                .ThenByDescending(s => s.ControlConfirmationPending).First()).ToArray();

    // Reopening a view is not a request to invalidate verified live metadata.
    // Status-only objects legitimately have no operable current-value projection.
    public static bool NeedsInspection(SignalDefinition signal, bool force)
        => !signal.ControlInspectionBusy && !signal.ControlCommandBusy &&
           (force || !signal.ControlModelResolved ||
            (signal.ControlSupportsOperate && signal.ControlCurrentValue == "-"));

    /// <summary>
    /// A control is NOT a DataSet member merely because its feedback Pos.stVal
    /// is reported. Yet a verified live CSWI.Pos may exist in another LD and
    /// must remain discoverable without changing the operator's signal selection.
    /// Candidate admission is structural only; no control is enabled until
    /// the exact live ctlModel and Oper structure pass native inspection.
    /// </summary>
    public static bool IsIndependentCswiCandidate(SignalDefinition signal)
        => signal.IsValidControlObject && !signal.IsGenericControl &&
           string.Equals(SignalDefinition.DetectLogicalNodeClass(signal.LogicalNode),
               "CSWI", StringComparison.OrdinalIgnoreCase) &&
           Normalize(signal.ObjectReference).EndsWith(".Pos",
               StringComparison.OrdinalIgnoreCase);

    public static SignalDefinition[] IndependentCswiInspectionCandidates(
        IEnumerable<SignalDefinition> signals, int budget = 24)
    {
        if (budget <= 0) return [];
        return signals.Where(s => !s.IsSelected && IsIndependentCswiCandidate(s))
            .GroupBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.ControlModelResolved)
                .ThenByDescending(s => s.ControlSupportsOperate).First())
            .OrderBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Take(budget).ToArray();
    }

    public static SignalDefinition[] VerifiedIndependentCswiControls(
        IEnumerable<SignalDefinition> signals)
        => IndependentCswiInspectionCandidates(signals)
            .Where(s => s.ControlModelResolved && s.ControlSupportsOperate)
            .ToArray();

    public static SignalDefinition[] InspectionRepresentatives(IEnumerable<SignalDefinition> signals)
        => DistinctSelected(signals).Where(s => !s.ControlModelResolved).ToArray();
}
