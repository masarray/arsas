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
/// guessing. Exact source/event-log references are preferred. A normalized
/// logical-device/display wrapper is accepted only when it produces one unique
/// non-control signal with the required functional constraint.
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
            var exactReferences = IoTestLiveBindingService.ImportedReferences(point)
                .Select(IoTestLiveBindingService.NormalizeReference)
                .Where(value => value.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var candidates = device.Signals
                .Where(signal => IsEligible(signal, point))
                .Where(signal => exactReferences.Contains(
                    IoTestLiveBindingService.NormalizeReference(signal.ObjectReference)))
                .ToList();
            var usedNormalizedPrefix = false;

            if (candidates.Count == 0)
            {
                candidates = device.Signals
                    .Where(signal => IsEligible(signal, point))
                    .Where(signal => NormalizedTelegramMatches(signal, point, ied, device))
                    .ToList();
                usedNormalizedPrefix = true;
            }

            if (candidates.Count == 0)
            {
                missing.Add(point);
                continue;
            }

            if (candidates.Count != 1 || !usedSignals.Add(candidates[0]))
            {
                ambiguous.Add(point);
                continue;
            }

            matches.Add(new IoTestSignalMatch(point, candidates[0], usedNormalizedPrefix));
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

        return new IoTestSignalSelectionResult(
            matches,
            missing,
            ambiguous,
            $"Resolved {matches.Count} enabled IO-list signal(s) to unique discovered model points.");
    }

    private static bool IsEligible(SignalDefinition signal, IoTestPointPlan point)
    {
        if (signal.IsControlSignal || string.IsNullOrWhiteSpace(signal.ObjectReference))
            return false;

        if (string.IsNullOrWhiteSpace(point.FunctionalConstraint))
            return true;

        return !string.IsNullOrWhiteSpace(signal.FunctionalConstraint) &&
               signal.FunctionalConstraint.Equals(point.FunctionalConstraint, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NormalizedTelegramMatches(
        SignalDefinition signal,
        IoTestPointPlan point,
        IoTestIedPlan ied,
        Iec61850MonitorDevice device)
    {
        var expected = IoTestLiveBindingService.ImportedReferences(point)
            .Select(reference => IoTestLiveBindingService.NormalizeTelegram(reference, ied.IedName))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (expected.Count == 0)
            return false;

        var observed = IoTestLiveBindingService.NormalizeTelegram(signal.ObjectReference, device.Name);
        if (expected.Contains(observed))
            return true;

        if (string.IsNullOrWhiteSpace(device.SclIedName))
            return false;

        observed = IoTestLiveBindingService.NormalizeTelegram(signal.ObjectReference, device.SclIedName);
        return expected.Contains(observed);
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
}
