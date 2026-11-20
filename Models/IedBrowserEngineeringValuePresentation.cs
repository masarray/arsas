using System.Globalization;

namespace ArIED61850Tester.Models;

/// <summary>
/// Display-only units sourced from engine-enriched SignalDefinition.Unit.
/// Exact IEC reference + FC precedes nearest owning reference; no guessed
/// physical dimension from DO names and no modifications to received values.
/// </summary>
public enum IedBrowserEngineeringRole
{
    Magnitude,
    AngleDegrees,
    Quality,
    UtcTime,
    StatusEnum,
    SettingMagnitude,
    Configuration,
    TextOrContainer
}

/// <summary>
/// IEC semantic dimension is derived from the exact canonical DA path and FC,
/// before any ancestor-unit lookup. Vector.ang and Vector.ang.f are the same
/// angle dimension; neither may inherit cVal/phase magnitude SIUnit.
/// </summary>
public static class IedBrowserEngineeringValuePresentation
{
    public static IedBrowserEngineeringRole Classify(string reference, string fc)
    {
        var path = IedBrowserLiveValues.CanonicalReference(reference);
        var constraint = (fc ?? "").Trim().ToUpperInvariant();
        if (path.EndsWith(".q", StringComparison.Ordinal))
            return IedBrowserEngineeringRole.Quality;
        if (path.EndsWith(".t", StringComparison.Ordinal))
            return IedBrowserEngineeringRole.UtcTime;
        if (constraint == "MX" && IsVectorAngle(path))
            return IedBrowserEngineeringRole.AngleDegrees;
        if (constraint is "SG" or "SP" &&
            (path.EndsWith(".setMag", StringComparison.Ordinal) ||
             path.EndsWith(".setMag.f", StringComparison.Ordinal)))
            return IedBrowserEngineeringRole.SettingMagnitude;
        if (constraint == "MX" &&
            (path.EndsWith(".mag.f", StringComparison.Ordinal) ||
             path.EndsWith(".instMag.f", StringComparison.Ordinal) ||
             path.EndsWith(".cVal", StringComparison.Ordinal) ||
             path.EndsWith(".instCVal", StringComparison.Ordinal) ||
             path.EndsWith(".mag", StringComparison.Ordinal)))
            return IedBrowserEngineeringRole.Magnitude;
        if (path.EndsWith(".stVal", StringComparison.Ordinal) ||
            path.EndsWith(".setVal", StringComparison.Ordinal) ||
            path.EndsWith(".setCharact", StringComparison.Ordinal) ||
            path.EndsWith(".ctlModel", StringComparison.Ordinal))
            return IedBrowserEngineeringRole.StatusEnum;
        if (constraint is "CF" or "DC")
            return IedBrowserEngineeringRole.Configuration;
        return IedBrowserEngineeringRole.TextOrContainer;
    }

    private static bool IsVectorAngle(string path)
        => path.EndsWith(".cVal.ang", StringComparison.Ordinal) ||
           path.EndsWith(".cVal.ang.f", StringComparison.Ordinal) ||
           path.EndsWith(".instCVal.ang", StringComparison.Ordinal) ||
           path.EndsWith(".instCVal.ang.f", StringComparison.Ordinal);

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
        var role = Classify(canonical, normalizedFc);
        if (role == IedBrowserEngineeringRole.AngleDegrees)
            return ResolveVectorAngleUnit(index, canonical, normalizedFc);
        if (role is IedBrowserEngineeringRole.Quality or IedBrowserEngineeringRole.UtcTime
            or IedBrowserEngineeringRole.StatusEnum or IedBrowserEngineeringRole.Configuration)
            return "";
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
        if (normalizedFc != "MX" || !IsVectorAngle(canonical)) return "";
        // An explicit angular unit, if declared, is the ONLY metadata owner
        // allowed to override the IEC Vector.ang degree convention.
        var angleOwner = canonical.EndsWith(".ang.f", StringComparison.Ordinal)
            ? canonical[..^2] : canonical;
        if (index.TryGetValue(canonical + "|" + normalizedFc, out var exact))
            return AngleSymbol(exact);
        if (index.TryGetValue(angleOwner + "|" + normalizedFc, out var declared))
            return AngleSymbol(declared);
        // IEC 61850-7-3 Vector.ang is degrees. Never walk to phase/cVal.
        return "°";
    }

    private static string AngleSymbol(string? unit)
    {
        // A null indexed unit is an explicit conflicting declaration: fail
        // closed. An unrelated non-angle symbol is leakage, not a legitimate
        // override of the IEC Vector.ang degree convention.
        if (unit is null) return "";
        return string.Equals(unit, "rad", StringComparison.Ordinal) ? "rad" : "°";
    }

    /// <summary>
    /// One pure semantic unit decision for all Browser pages. The container
    /// and its observed leaf must yield the same engineering unit. Exact
    /// typed integer ST is allowed ONLY by live paired owner CF evidence;
    /// status enums, q/t, and unverified counter metadata remain unitless.
    /// </summary>
    public static string ResolveObservedUnit(
        IReadOnlyDictionary<string, string?> units,
        IedBrowserCfEvidencePresentation cfEvidence,
        string observedReference, string engineeringReference,
        string fc, bool isEngineeringValue)
    {
        var observed = IedBrowserLiveValues.CanonicalReference(observedReference);
        var normalizedFc = (fc ?? "").Trim().ToUpperInvariant();
        // IEC XCBR typed numeric status/setting: do NOT promote an
        // unverified SignalDefinition unit. Only live paired CF of the exact
        // owning DO may qualify an integer actVal or SP setMag.
        var numericCandidate = normalizedFc == "ST" &&
            (observed.EndsWith(".stVal", StringComparison.Ordinal) ||
             observed.EndsWith(".actVal", StringComparison.Ordinal)) ||
            normalizedFc == "SP" &&
            (observed.EndsWith(".setMag", StringComparison.Ordinal) ||
             observed.EndsWith(".setMag.f", StringComparison.Ordinal));
        if (numericCandidate)
            return cfEvidence.TryFormatVerifiedNumericStatusUnit(
                observed, normalizedFc, out var proven, out _) ? proven : "";
        var unit = isEngineeringValue ? ResolveUnit(units, engineeringReference, fc) : "";
        if (unit.Length == 0 && string.Equals(fc, "MX", StringComparison.OrdinalIgnoreCase) &&
            observed.EndsWith(".ang.f", StringComparison.Ordinal))
            unit = ResolveVectorAngleUnit(units, observed, fc);
        return unit;
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
