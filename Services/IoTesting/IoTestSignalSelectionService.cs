using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record IoTestSignalMatch(
    IoTestPointPlan TestPoint,
    SignalDefinition Signal,
    bool UsedNormalizedIedPrefix);

public sealed record IoTestSignalSelectionResult(
    IReadOnlyList<IoTestSignalMatch> Matches,
    IReadOnlyList<IoTestPointPlan> MissingPoints,
    IReadOnlyList<IoTestPointPlan> AmbiguousPoints,
    string Message)
{
    public bool Succeeded => MissingPoints.Count == 0 && AmbiguousPoints.Count == 0;
    public bool CanRetryWithFreshDiscovery => MissingPoints.Count > 0 && AmbiguousPoints.Count == 0;
}

/// <summary>
/// Resolves the enabled IO-list scope against one discovered IED model without
/// guessing. Exact references remain highest priority. Canonical IEC 61850 forms
/// accept vendor-safe spelling differences only when the best candidate is unique.
/// </summary>
public sealed class IoTestSignalSelectionService
{
    public IoTestSignalSelectionResult Resolve(
        IoTestIedPlan ied,
        Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(ied);
        ArgumentNullException.ThrowIfNull(device);

        var requested = ied.TestPoints
            .Where(point => point.TestEnabled && point.ImportReady)
            .ToList();
        var matches = new List<IoTestSignalMatch>(requested.Count);
        var missing = new List<IoTestPointPlan>();
        var ambiguous = new List<IoTestPointPlan>();
        var usedSignals = new HashSet<SignalDefinition>();

        foreach (var point in requested)
        {
            var importedReferences = IoTestLiveBindingService.ImportedReferences(point);
            var scored = device.Signals
                .Where(signal => IsEligible(signal, point))
                .Select(signal => new ScoredSignal(
                    signal,
                    importedReferences.Count == 0
                        ? 0
                        : importedReferences.Max(reference => IoTestReferenceMatcher.Score(
                            reference,
                            signal.ObjectReference,
                            ied.IedName,
                            device.Name,
                            device.SclIedName,
                            point.LogicalNode))))
                .Where(item => item.Score > 0)
                .ToList();

            if (scored.Count == 0)
            {
                missing.Add(point);
                continue;
            }

            var bestScore = scored.Max(item => item.Score);
            var candidates = scored
                .Where(item => item.Score == bestScore)
                .Select(item => item.Signal)
                .ToList();

            if (candidates.Count != 1 || !usedSignals.Add(candidates[0]))
            {
                ambiguous.Add(point);
                continue;
            }

            matches.Add(new IoTestSignalMatch(
                point,
                candidates[0],
                bestScore < IoTestReferenceMatcher.ExactScore));
        }

        if (missing.Count > 0 || ambiguous.Count > 0)
        {
            var details = new List<string>();
            if (missing.Count > 0)
                details.Add($"{missing.Count} signal(s) were not found: {Describe(missing)}");
            if (ambiguous.Count > 0)
                details.Add($"{ambiguous.Count} signal(s) did not resolve uniquely: {Describe(ambiguous)}");
            return new IoTestSignalSelectionResult(
                matches,
                missing,
                ambiguous,
                string.Join(" ", details));
        }

        var smartCount = matches.Count(match => match.UsedNormalizedIedPrefix);
        var smartText = smartCount == 0
            ? string.Empty
            : $" {smartCount} used unique canonical IEC 61850 matching.";
        return new IoTestSignalSelectionResult(
            matches,
            missing,
            ambiguous,
            $"Resolved {matches.Count} enabled IO-list signal(s) to unique discovered model points.{smartText}");
    }

    private static bool IsEligible(SignalDefinition signal, IoTestPointPlan point)
    {
        if (signal.IsControlSignal || string.IsNullOrWhiteSpace(signal.ObjectReference))
            return false;

        return string.IsNullOrWhiteSpace(point.FunctionalConstraint) ||
               string.IsNullOrWhiteSpace(signal.FunctionalConstraint) ||
               signal.FunctionalConstraint.Equals(point.FunctionalConstraint, StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(IReadOnlyCollection<IoTestPointPlan> points)
    {
        var values = points
            .Take(4)
            .Select(point => $"{point.TestPointId} ({point.ReportIecReference})")
            .ToList();
        if (points.Count > values.Count)
            values.Add($"…and {points.Count - values.Count} more");
        return string.Join(", ", values);
    }

    private sealed record ScoredSignal(SignalDefinition Signal, int Score);
}
