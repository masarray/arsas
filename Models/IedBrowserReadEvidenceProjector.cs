using System.Text.RegularExpressions;

namespace ArIED61850Tester.Models;

/// <summary>
/// One read-only projection of the authoritative association-scoped ledger and
/// absence evidence. No second value cache, no inferred wire request, no
/// fabricated process scalar and no FC/case-insensitive alias fallback.
/// </summary>
public static class IedBrowserReadEvidenceProjector
{
    private static readonly Regex AccessResultCode = new(
        @"\bAccessResult\.failure code ([0-9]+)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IedBrowserReadEvidenceSnapshot Project(
        IedBrowserReadTarget target,
        IedBrowserLiveValues ledger,
        IedBrowserReadFailureSample? absence = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(ledger);
        var canonical = IedBrowserLiveValues.CanonicalReference(target.Reference);
        if (ledger.TryGet(target.Reference, target.FunctionalConstraint,
                out var observation) &&
            !observation.IsStale && observation.Association == ledger.Association)
            return new IedBrowserReadEvidenceSnapshot(
                "OBSERVED", "NONE", observation, null);

        // Absence can only describe the exact model-declared target/FC.
        // A mislabeled callback is not a data source.
        if (absence is null ||
            !string.Equals(IedBrowserLiveValues.CanonicalReference(absence.Reference),
                canonical, StringComparison.Ordinal) ||
            !string.Equals(absence.FunctionalConstraint,
                target.FunctionalConstraint, StringComparison.OrdinalIgnoreCase))
            return new IedBrowserReadEvidenceSnapshot(
                "NOT_OBSERVED_NO_ATTEMPT_EVIDENCE", "NO_WIRE_EVIDENCE", null, null);

        var state = absence.Kind switch
        {
            IedBrowserReadAbsenceKind.Deferred => "DEFERRED",
            IedBrowserReadAbsenceKind.Interrupted => "INTERRUPTED",
            _ => "READ_FAILED"
        };
        var code = absence.Kind == IedBrowserReadAbsenceKind.Failed
            ? FailureReasonCode(absence.Reason)
            : "NOT_TERMINAL_ACCESS_RESULT";
        return new IedBrowserReadEvidenceSnapshot(state, code, null, absence);
    }

    public static string FailureReasonCode(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "UNRESOLVED";
        // Text is not type evidence. Only an actual numeric MMS accessResult
        // identifier admits a protocol-specific reason classification.
        var match = AccessResultCode.Match(reason);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var code))
            return "UNRESOLVED";
        return code switch
        {
            2 => "TEMPORARILY_UNAVAILABLE",
            3 => "ACCESS_DENIED",
            4 or 5 or 10 => "OBJECT_MISSING",
            6 or 9 => "NOT_SUPPORTED",
            7 or 8 => "TYPE_MISMATCH",
            _ => "REMOTE_ACCESS_FAILURE"
        };
    }
}

public sealed record IedBrowserReadEvidenceSnapshot(
    string State,
    string FailureReasonCode,
    IedBrowserObservedValue? Observation,
    IedBrowserReadFailureSample? Failure);
