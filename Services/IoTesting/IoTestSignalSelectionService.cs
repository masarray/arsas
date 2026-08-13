using System.Text.RegularExpressions;
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
/// Weak source rows are resolved only after stronger references have claimed their
/// signals, allowing deterministic sibling evidence without fuzzy text matching.
/// </summary>
public sealed class IoTestSignalSelectionService
{
    private static readonly Regex ProtectionCodeRegex = new(
        @"\((?<code>\d{2,3}[A-Z]{0,3})(?:\s*-\s*[^)]*)?\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        var unresolved = new List<CandidateSet>();

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
            unresolved.Add(new CandidateSet(point, bestScore, candidates));
        }

        // Resolve the strongest references first. This makes the result independent of
        // workbook row order and lets a weak legacy row use already-proven sibling
        // assignments as elimination evidence. No candidate is ever selected by text
        // similarity: it must still be the one unique best IEC object left.
        var madeProgress = true;
        while (madeProgress && unresolved.Count > 0)
        {
            madeProgress = false;
            foreach (var candidateSet in unresolved
                         .OrderByDescending(item => item.BestScore)
                         .ThenBy(item => item.Candidates.Count)
                         .ToArray())
            {
                var candidates = candidateSet.Candidates
                    .Where(signal => !usedSignals.Contains(signal))
                    .ToList();

                if (candidateSet.BestScore <= IoTestReferenceMatcher.PartialObjectScore && candidates.Count > 1)
                    candidates = NarrowByProtectionIdentity(candidateSet.Point, candidates);

                if (candidates.Count != 1)
                    continue;

                var signal = candidates[0];
                if (!usedSignals.Add(signal))
                    continue;

                matches.Add(new IoTestSignalMatch(
                    candidateSet.Point,
                    signal,
                    candidateSet.BestScore < IoTestReferenceMatcher.ExactScore));
                unresolved.Remove(candidateSet);
                madeProgress = true;
            }
        }

        ambiguous.AddRange(unresolved.Select(item => item.Point));

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

    private static List<SignalDefinition> NarrowByProtectionIdentity(
        IoTestPointPlan point,
        IReadOnlyCollection<SignalDefinition> candidates)
    {
        var match = ProtectionCodeRegex.Match(point.SignalName ?? string.Empty);
        if (!match.Success)
            return candidates.ToList();

        // P0 field regression: a few legacy 7SX80 rows retained only `.Op.general`
        // while the user-visible signal description still proved ANSI 27. Do not turn
        // this into a generic fuzzy-name matcher. For ANSI 27 only, accept the explicit
        // live IEC identity used by protection models (27Undervoltage / PTUV), and still
        // require one unique candidate after stronger sibling references are claimed.
        var code = match.Groups["code"].Value.ToUpperInvariant();
        if (!code.Equals("27", StringComparison.Ordinal))
            return candidates.ToList();

        var narrowed = candidates
            .Where(signal => IsProtection27Reference(signal.ObjectReference))
            .ToList();
        return narrowed.Count == 0 ? candidates.ToList() : narrowed;
    }

    private static bool IsProtection27Reference(string? reference)
    {
        var normalized = IoTestReferenceMatcher.NormalizeRaw(reference);
        if (normalized.Contains("27undervoltage", StringComparison.OrdinalIgnoreCase))
            return true;

        return Regex.IsMatch(
            normalized,
            @"(?:^|[/_.])ptuv\d*(?:[/.]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
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
    private sealed record CandidateSet(
        IoTestPointPlan Point,
        int BestScore,
        IReadOnlyList<SignalDefinition> Candidates);
}
