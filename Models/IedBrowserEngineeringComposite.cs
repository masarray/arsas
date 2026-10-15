using System.Globalization;
using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Two *model-declared* MX leaves composing one IEC CMV polar value.
/// The source and exact FC are immutable; observations remain in LiveValues.
/// </summary>
public sealed record IedBrowserPolarPlan(
    string MagnitudeReference, string AngleReference, string FunctionalConstraint,
    string Phase = "");

public sealed record IedBrowserCompositeObservation(
    string Text, string Source, string Provenance, bool IsStale);

/// <summary>
/// Read-only IEC phasor presentation. No derived values are stored or published,
/// no phase is inferred from list position, and no physical unit is guessed.
/// Plans are created only during lazy model projection (never at Report ingress).
/// </summary>
public static class IedBrowserEngineeringComposite
{
    // IEC WYE/A/PhV and DEL/PPV are different model-verified phase groups.
    // One object exposing both group shapes is ambiguous, never first-match-wins.
    private static readonly string[][] PhaseFamilies =
    [
        ["phsA", "phsB", "phsC"],
        ["phsAB", "phsBC", "phsCA"]
    ];

    public static IedBrowserPolarPlan? BuildPolar(
        IEnumerable<LiveIedDataAttributeModel> attributes, string owner,
        bool requireAngle = true)
    {
        var declared = attributes.Where(a =>
            string.Equals(a.FunctionalConstraint, "MX", StringComparison.OrdinalIgnoreCase))
            .Select(a => IedBrowserLiveValues.CanonicalReference(a.MmsReference))
            .Where(reference => reference.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        // Only normal IEC magnitude/angle pairs; instantaneous magnitudes are
        // never used as an arbitrary substitute for steady cVal/mag.
        foreach (var (magSuffix, angleSuffix) in new[]
            { (".cVal.mag.f", ".cVal.ang.f"), (".mag.f", ".ang.f") })
        {
            var magnitude = owner + magSuffix;
            if (!declared.Contains(magnitude)) continue;
            var angle = owner + angleSuffix;
            if (requireAngle && !declared.Contains(angle))
                continue; // a scalar MV mag.f must not become a phasor
            return new IedBrowserPolarPlan(magnitude,
                declared.Contains(angle) ? angle : "", "MX");
        }
        return null;
    }

    // CDC authority is an engine-derived data object type, never a name
    // heuristic (TotW/Hz) or a guessed unit. A typed scalar MV must remain
    // scalar even when a malformed/vendor schema advertises an ang sibling.
    public static bool IsScalarCdc(string? cdc)
        => string.Equals((cdc ?? "").Trim(), "MV",
            StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<IedBrowserPolarPlan> BuildThreePhases(
        IEnumerable<LiveIedDataAttributeModel> attributes, string owner)
    {
        var all = attributes.ToArray();
        IedBrowserPolarPlan[]? matched = null;
        foreach (var names in PhaseFamilies)
        {
            var phases = new IedBrowserPolarPlan[names.Length];
            var complete = true;
            for (var i = 0; i < names.Length; i++)
            {
                // A proven three-phase family may still present magnitude
                // if an angle leaf is absent. Scalar DOs have no phase family.
                var result = BuildPolar(all, owner + "." + names[i],
                    requireAngle: false);
                if (result is null) { complete = false; break; }
                phases[i] = result with { Phase = names[i] };
            }
            if (!complete) continue;
            if (matched is not null) return Array.Empty<IedBrowserPolarPlan>();
            matched = phases;
        }
        return matched ?? Array.Empty<IedBrowserPolarPlan>();
    }

    public static bool TryCompose(
        IedBrowserDetailRow row, IedBrowserLiveValues ledger,
        IReadOnlyDictionary<string, string?> unitIndex, bool monitoring,
        out IedBrowserCompositeObservation observed)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(ledger);
        observed = null!;
        // One entry point for every schema-qualified vector, including
        // neutral/net/residual extensions not covered by three-phase plans.
        // The independent adapter shares CanPair: never bypass timestamp
        // or source checks through a secondary renderer in MainWindow.
        // A top-level DO shows the canonical three-phase engineering preview
        // without dumping every neutral/net/res component into one grid cell.
        // Those extra exact components remain accessible on expansion.
        // Independent inspection rows still use the full-vector adapter.
        var compactDo = row.IsDataObject && row.PhasePlans.Count == 3 &&
            row.VectorComponents.Count > 3;
        if (row.VectorComponents.Count > 3 && !compactDo)
            return TryIndependent(row, ledger, unitIndex, monitoring, out observed);
        IReadOnlyList<IedBrowserPolarPlan> plans = row.PhasePlans.Count > 0
            ? row.PhasePlans : row.PolarPlan is { } polar
                ? new[] { polar } : Array.Empty<IedBrowserPolarPlan>();
        if (plans.Count == 0)
            return TryIndependent(row, ledger, unitIndex, monitoring, out observed);

        var components = new List<string>(plans.Count);
        var authorities = new HashSet<string>(StringComparer.Ordinal);
        var references = new List<string>(plans.Count * 2);
        long? generation = null;
        bool stale = false;
        var missingMagnitudes = false;
        foreach (var plan in plans)
        {
            if (!ledger.TryGet(plan.MagnitudeReference, plan.FunctionalConstraint,
                    out var magnitude, preferReport: monitoring) ||
                !double.TryParse(magnitude.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var magnitudeNumber) ||
                !double.IsFinite(magnitudeNumber))
            {
                // A partially received vector must not masquerade as a full
                // phasor, but its already observed phases must not disappear.
                missingMagnitudes = true;
                components.Add(plan.Phase + ": not observed");
                continue;
            }
            if (generation.HasValue && generation.Value != magnitude.Association)
                return false;

            generation = magnitude.Association;
            stale |= magnitude.IsStale;
            var unit = IedBrowserEngineeringValuePresentation.ResolveUnit(
                unitIndex, plan.MagnitudeReference, plan.FunctionalConstraint);
            var magText = IedBrowserEngineeringValuePresentation.Format(magnitude.Value, unit);
            var text = magText;
            authorities.Add(magnitude.Source);
            references.Add(plan.MagnitudeReference + "[" + plan.FunctionalConstraint + "]");
            var anglePaired = false;
            if (plan.AngleReference.Length > 0 &&
                ledger.TryGet(plan.AngleReference, plan.FunctionalConstraint,
                    out var angle, preferReport: monitoring) &&
                double.TryParse(angle.Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var angleNumber) &&
                double.IsFinite(angleNumber) &&
                angle.IsStale == magnitude.IsStale &&
                CanPair(magnitude, angle))
            {
                var angleUnit = IedBrowserEngineeringValuePresentation.ResolveVectorAngleUnit(
                    unitIndex, plan.AngleReference, plan.FunctionalConstraint);
                var suffix = angleUnit switch
                {
                    "°" => "°",
                    "deg" or "degree" or "degrees" => "°",
                    "" => " (unit unverified)",
                    _ => " " + angleUnit
                };
                text += " ∠ " + angle.Value + suffix;
                authorities.Add(angle.Source);
                references.Add(plan.AngleReference + "[" + plan.FunctionalConstraint + "]");
                anglePaired = true;
            }
            if (!anglePaired && !compactDo)
                text += " (angle not verified)";
            components.Add(plan.Phase.Length == 0 ? text : plan.Phase + ": " + text);
        }
        if (!generation.HasValue) return false; // no evidence at all
        var prefix = plans.Count > 1 ? string.Join("; ", components) : components[0];
        if (missingMagnitudes) prefix = "Partial · " + prefix;
        var sources = string.Join("+", authorities.OrderBy(x => x, StringComparer.Ordinal));
        observed = new IedBrowserCompositeObservation(prefix, sources,
            "Derived from exact " + string.Join(", ", references) +
            " · association " + generation + " · sources " + sources +
            (compactDo
                ? " · three-phase summary only; other model-proven components on expansion"
                : " · independently observed components; not a synchronous phasor sample"),
            stale);
        return true;
    }

    private static bool TryIndependent(IedBrowserDetailRow row,
        IedBrowserLiveValues ledger, IReadOnlyDictionary<string, string?> unitIndex,
        bool monitoring, out IedBrowserCompositeObservation result)
    {
        result = null!;
        if (!IedBrowserObservedVectorPresentation.TryFormat(
                ledger, row.VectorComponents, unitIndex, monitoring,
                out var independent))
            return false;
        result = new IedBrowserCompositeObservation(
            independent.Value, "Independent " + independent.Source,
            independent.Provenance, independent.IsStale);
        return true;
    }

    /// <summary>
    /// Shared compatibility gate for both the standard three-phase composer and
    /// additional schema-declared neutral/residual vector rows. An independent
    /// angle remains observable but is never presented as a paired phasor.
    /// </summary>
    internal static bool CanPair(IedBrowserObservedValue magnitude,
        IedBrowserObservedValue angle)
    {
        if (magnitude.Association != angle.Association ||
            magnitude.IsStale != angle.IsStale ||
            !string.Equals(magnitude.Source, angle.Source, StringComparison.Ordinal))
            return false;

        static string ActualTimestamp(string? value)
        {
            var stamp = (value ?? "").Trim();
            return stamp is "-" or "Unknown" or "Not read" ? "" : stamp;
        }
        var a = ActualTimestamp(magnitude.DeviceTimestamp);
        var b = ActualTimestamp(angle.DeviceTimestamp);
        if (a.Length > 0 || b.Length > 0)
            return a.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);
        // Without an IED timestamp, pair only temporally adjacent observations.
        // Sequential explicit reads are labeled as such, never as atomic samples.
        return Math.Abs((magnitude.ObservedUtc - angle.ObservedUtc).TotalSeconds) <= 15;
    }
}
