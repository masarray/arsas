using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Interprets MMS GOOSE payloads using their actual wire types and verified
/// FCDA metadata. Never assumes an arbitrary bit string is an IEC quality.
/// The raw value remains accessible separately for diagnostics.
/// </summary>
public static class GooseTypedValueInterpreter
{
    public static string Render(MmsDataValue value, string? cdc, string? bType, string? reference)
    {
        if (value.Kind != MmsDataKind.BitString)
            return MmsDataValueRenderer.ToCompactString(value, reference);
        var raw = value.RawValue.Span;
        if (raw.Length == 2 && raw[0] == 6 &&
            (string.Equals(bType, "Dbpos", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(cdc, "DPC", StringComparison.OrdinalIgnoreCase)))
            return ((raw[1] >> 6) & 3) switch
            {
                0 => "Intermediate",
                1 => "Off",
                2 => "On",
                _ => "Invalid"
            };
        if (IsQuality(reference, bType))
            return RenderQuality(value);
        return MmsDataValueRenderer.ToCompactString(value, reference);
    }

    public static bool IsQuality(string? reference, string? bType)
        => string.Equals(bType, "Quality", StringComparison.OrdinalIgnoreCase) ||
           (reference?.EndsWith(".q", StringComparison.OrdinalIgnoreCase) ?? false);

    public static string RenderQuality(MmsDataValue value)
    {
        if (value.Kind != MmsDataKind.BitString)
            return MmsDataValueRenderer.ToCompactString(value);
        var raw = value.RawValue.Span;
        // IEC 61850 Quality: exactly 13 significant bits, encoded MSB-first.
        if (raw.Length != 3 || raw[0] != 3)
            return MmsDataValueRenderer.ToCompactString(value);
        var flags = (raw[1] << 8) | raw[2];
        var validity = (flags >> 14) & 3;
        var pieces = new List<string>(12)
        {
            validity switch { 0 => "Good", 1 => "Invalid", 2 => "Reserved", _ => "Questionable" }
        };
        if ((flags & 0x2000) != 0) pieces.Add("Overflow");
        if ((flags & 0x1000) != 0) pieces.Add("Out of range");
        if ((flags & 0x0800) != 0) pieces.Add("Bad reference");
        if ((flags & 0x0400) != 0) pieces.Add("Oscillatory");
        if ((flags & 0x0200) != 0) pieces.Add("Failure");
        if ((flags & 0x0100) != 0) pieces.Add("Old data");
        if ((flags & 0x0080) != 0) pieces.Add("Inconsistent");
        if ((flags & 0x0040) != 0) pieces.Add("Inaccurate");
        if ((flags & 0x0020) != 0) pieces.Add("Substituted");
        if ((flags & 0x0010) != 0) pieces.Add("Test");
        if ((flags & 0x0008) != 0) pieces.Add("Operator blocked");
        return string.Join(" · ", pieces);
    }
}

/// <summary>Pairs only exact, independently identified q and primary FCDA references.</summary>
public static class GooseCanonicalLeafProjection
{
    public static IReadOnlyList<GooseLeafValueSnapshot> Project(IReadOnlyList<GooseLeafValueSnapshot> wireLeaves)
    {
        var quality = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var leaf in wireLeaves)
        {
            if (!string.IsNullOrWhiteSpace(leaf.SignalReference) &&
                leaf.SignalReference.EndsWith(".q", StringComparison.OrdinalIgnoreCase))
                quality[leaf.SignalReference[..^2]] = leaf.Value;
        }
        var result = new List<GooseLeafValueSnapshot>(wireLeaves.Count);
        foreach (var leaf in wireLeaves)
        {
            var reference = leaf.SignalReference;
            if (!string.IsNullOrWhiteSpace(reference) &&
                reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase) &&
                quality.ContainsKey(reference[..^2]) &&
                wireLeaves.Any(candidate => candidate.SignalReference.StartsWith(reference[..^2] + ".", StringComparison.OrdinalIgnoreCase)
                    && !candidate.SignalReference.EndsWith(".q", StringComparison.OrdinalIgnoreCase)))
                continue;
            var suffix = reference.LastIndexOf('.');
            var objectRef = suffix > 0 ? reference[..suffix] : string.Empty;
            result.Add(leaf with
            {
                Quality = objectRef.Length > 0 && quality.TryGetValue(objectRef, out var q) ? q : "—"
            });
        }
        return result;
    }
}
