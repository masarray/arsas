namespace ArIED61850Tester.Models;

/// <summary>
/// One vector descriptor from an exact engine schema. This is never an
/// observation: all numeric values, timestamps and sources must come from
/// canonical association-fenced Report / explicit Read evidence.
/// </summary>
public sealed record IedBrowserVectorComponent(
    string Label, string MagnitudeReference, string AngleReference, string Fc);

public sealed record IedBrowserVectorDisplay(
    string Value, string Source, string Provenance, bool IsStale);

/// <summary>
/// Presentation-only composition of independently observed IEC Vector leaves.
/// Does not interpolate unavailable phases, calculate phasors, infer SI units,
/// or merge q/t or SOE state. Work is bounded to the visible row (at most
/// three phases) with constant-time ledger lookups.
/// </summary>
public static class IedBrowserObservedVectorPresentation
{
    public static bool TryFormat(
        IedBrowserLiveValues values,
        IReadOnlyList<IedBrowserVectorComponent> components,
        IReadOnlyDictionary<string, string?> units,
        bool monitoring, out IedBrowserVectorDisplay display)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(units);
        display = null!;
        if (components.Count == 0 || components.Count > 3)
            return false;

        var displayed = new List<string>(components.Count);
        var provenance = new List<string>(components.Count * 2);
        bool anyStale = false, anyReport = false, anyRead = false;
        foreach (var part in components)
        {
            var prefix = part.Label.Length == 0 ? "" : part.Label + ": ";
            var hasMag = values.TryGet(part.MagnitudeReference, part.Fc,
                out var magnitude, preferReport: monitoring);
            var hasAng = values.TryGet(part.AngleReference, part.Fc,
                out var angle, preferReport: monitoring);
            if (!hasMag && !hasAng) continue;
            // Components are independent observations, not proof that
            // magnitude and angle were captured atomically in one report.
            string text = "";
            if (hasMag)
            {
                var unit = IedBrowserEngineeringValuePresentation.ResolveUnit(
                    units, part.MagnitudeReference, part.Fc);
                text = IedBrowserEngineeringValuePresentation.Format(magnitude.Value, unit);
                Register(part.MagnitudeReference, magnitude);
            }
            if (hasAng)
            {
                var unit = IedBrowserEngineeringValuePresentation.ResolveVectorAngleUnit(
                    units, part.AngleReference, part.Fc);
                var rendered = unit == "°" ? angle.Value + "°" :
                    IedBrowserEngineeringValuePresentation.Format(angle.Value, unit);
                text += (text.Length == 0 ? "" : " ") + "∠ " + rendered;
                Register(part.AngleReference, angle);
            }
            displayed.Add(prefix + text);
        }

        if (displayed.Count == 0) return false;
        // A mixed source or partial phase set is never advertised as a single
        // coherent phasor image. The exact observed references remain visible.
        var source = anyReport && anyRead ? "Report + MMS Read" :
            anyReport ? "Report" : "MMS Read";
        if (anyStale) source = "Stale " + source;
        display = new IedBrowserVectorDisplay(
            string.Join("; ", displayed), source,
            string.Join(" | ", provenance) +
            " · independently observed components (no inferred missing phases)",
            anyStale);
        return true;

        void Register(string reference, IedBrowserObservedValue value)
        {
            anyStale |= value.IsStale;
            anyReport |= value.Source.StartsWith("Report", StringComparison.Ordinal);
            anyRead |= !value.Source.StartsWith("Report", StringComparison.Ordinal);
            provenance.Add(
                reference + " [" + value.Source + "] " +
                value.ObservedUtc.ToString("yyyy-MM-dd HH:mm:ss") +
                " UTC, association " + value.Association);
        }
    }
}
