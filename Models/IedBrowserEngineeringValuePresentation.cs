using System.Globalization;

namespace ArIED61850Tester.Models;

/// <summary>
/// Display-only units sourced from engine-enriched SignalDefinition.Unit.
/// Exact IEC reference + FC precedes nearest owning reference; no guessed
/// physical dimension from DO names and no modifications to received values.
/// </summary>
public static class IedBrowserEngineeringValuePresentation
{
    public static IReadOnlyDictionary<string, string?> BuildUnitIndex(
        IEnumerable<SignalDefinition> signals)
    {
        var index = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var signal in signals)
        {
            var unit = (signal.Unit ?? "").Trim();
            var reference = IedBrowserLiveValues.CanonicalReference(signal.ObjectReference);
            var fc = (signal.FunctionalConstraint ?? "").Trim().ToUpperInvariant();
            if (unit.Length == 0 || unit is "-" or "Unknown" ||
                reference.Length == 0 || fc.Length == 0)
                continue;
            var key = reference + "|" + fc;
            if (!index.TryGetValue(key, out var existing))
                index.Add(key, unit);
            else if (!string.Equals(existing, unit, StringComparison.Ordinal))
                index[key] = null; // conflicting metadata: fail closed
        }
        return index;
    }

    public static string ResolveUnit(
        IReadOnlyDictionary<string, string?> index, string reference, string fc)
    {
        var canonical = IedBrowserLiveValues.CanonicalReference(reference);
        var normalizedFc = (fc ?? "").Trim().ToUpperInvariant();
        if (canonical.Length == 0 || normalizedFc.Length == 0) return "";
        var slash = canonical.IndexOf('/');
        // Nearest exact owner, never another DO, LD, or a shared LN.
        var cursor = canonical;
        while (cursor.Length > slash + 1)
        {
            if (index.TryGetValue(cursor + "|" + normalizedFc, out var unit))
                return unit ?? "";
            var dot = cursor.LastIndexOf('.');
            if (dot <= slash + 1) break;
            cursor = cursor[..dot];
        }
        return "";
    }

    /// <summary>
    /// Vector angle may have an explicit unit on ang or ang.f only.
    /// Inheriting a phase magnitude unit (for example kV) across cVal
    /// would be dimensionally wrong, even though that ancestor is an
    /// otherwise valid source for mag.f.
    /// </summary>
    public static string ResolveVectorAngleUnit(
        IReadOnlyDictionary<string, string?> index, string reference, string fc)
    {
        var canonical = IedBrowserLiveValues.CanonicalReference(reference);
        var normalizedFc = (fc ?? "").Trim().ToUpperInvariant();
        if (!canonical.EndsWith(".ang.f", StringComparison.Ordinal) ||
            normalizedFc != "MX") return "";
        if (index.TryGetValue(canonical + "|" + normalizedFc, out var exact))
            return exact ?? "";
        var angleOwner = canonical[..^2];
        if (index.TryGetValue(angleOwner + "|" + normalizedFc, out var declared))
            return declared ?? "";
        // IEC 61850-7-3 Vector.ang: Unit.SIUnit='deg', no multiplier.
        // Exact cVal/instCVal paths only, never inferred for scalar MV.
        return canonical.EndsWith(".cVal.ang.f", StringComparison.Ordinal) ||
               canonical.EndsWith(".instCVal.ang.f", StringComparison.Ordinal)
            ? "°" : "";
    }

    public static string Format(string raw, string unit)
    {
        if (string.IsNullOrWhiteSpace(unit) || string.IsNullOrWhiteSpace(raw))
            return raw;
        var value = raw.Trim();
        if (value.EndsWith(" " + unit, StringComparison.Ordinal) ||
            value.EndsWith("°", StringComparison.Ordinal) && unit == "°")
            return value;
        // Only numerical engineering scalars accept an SI unit. Control
        // enumerations, q, t, structured vectors, and status text stay raw.
        return double.TryParse(value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out _)
            ? value + (unit == "°" ? "" : " ") + unit : raw;
    }
}
