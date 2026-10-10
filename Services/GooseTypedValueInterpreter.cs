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
        var raw = value.RawValue;
        if (raw.Count == 2 && raw[0] == 6 &&
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

    /// <summary>Legacy previous values have only MMS renderer text, not the old typed node.</summary>
    public static string RenderPrevious(string? previous, string? cdc, string? bType)
    {
        if (string.IsNullOrWhiteSpace(previous))
            return string.Empty;
        if (string.Equals(bType, "Quality", StringComparison.OrdinalIgnoreCase) &&
            previous.StartsWith("bits(", StringComparison.OrdinalIgnoreCase) &&
            previous.Contains("unused=3", StringComparison.OrdinalIgnoreCase))
        {
            var data = previous.AsSpan(5);
            var comma = data.IndexOf(',');
            if (comma == 4 && ushort.TryParse(data[..comma],
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var bits))
                return RenderQuality(MmsDataValue.BitString(3, new byte[] { (byte)(bits >> 8), (byte)bits }));
        }
        if ((string.Equals(cdc, "DPC", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(bType, "Dbpos", StringComparison.OrdinalIgnoreCase)) &&
             previous.StartsWith("bits(", StringComparison.OrdinalIgnoreCase))
        {
            var content = previous.AsSpan(5);
            var comma = content.IndexOf(',');
            if (comma == 2 && byte.TryParse(content[..comma], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var bits) &&
                previous.Contains("unused=6", StringComparison.OrdinalIgnoreCase))
                return ((bits >> 6) & 3) switch
                {
                    0 => "Intermediate", 1 => "Off", 2 => "On", _ => "Invalid"
                };
        }
        return GooseEngineeringValueFormatter.Format(previous);
    }

    public static bool IsQuality(string? reference, string? bType)
        => string.Equals(bType, "Quality", StringComparison.OrdinalIgnoreCase) ||
           (reference?.EndsWith(".q", StringComparison.OrdinalIgnoreCase) ?? false);

    public static string RenderQuality(MmsDataValue value)
    {
        if (value.Kind != MmsDataKind.BitString)
            return MmsDataValueRenderer.ToCompactString(value);
        var raw = value.RawValue;
        // IEC 61850 Quality: exactly 13 significant bits, encoded MSB-first.
        if (raw.Count != 3 || raw[0] != 3)
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

/// <summary>
/// Exact reference-identity engineering view of GOOSE allData. Raw wire
/// entries remain separately available and untouched, including orphan q.
/// </summary>
public static class GooseCanonicalLeafProjection
{
    public static IReadOnlyList<GooseLeafValueSnapshot> Project(IReadOnlyList<GooseLeafValueSnapshot> wireLeaves)
    {
        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var quality = new Dictionary<string, List<GooseLeafValueSnapshot>>(StringComparer.OrdinalIgnoreCase);
        foreach (var leaf in wireLeaves)
        {
            var key = CanonicalOwner(leaf.SignalReference, out var kind);
            if (key is null) continue;
            if (kind == "value") owners.Add(key);
            else if (kind == "quality")
            {
                if (!quality.TryGetValue(key, out var found))
                    quality[key] = found = new List<GooseLeafValueSnapshot>(1);
                found.Add(leaf);
            }
        }

        var result = new List<GooseLeafValueSnapshot>(wireLeaves.Count);
        foreach (var leaf in wireLeaves)
        {
            var key = CanonicalOwner(leaf.SignalReference, out var kind);
            quality.TryGetValue(key ?? string.Empty, out var companions);
            var hasUniqueCompanion = key is not null && owners.Contains(key) &&
                                     companions is { Count: 1 };
            if (hasUniqueCompanion && kind == "quality") continue;
            if (hasUniqueCompanion && kind == "value")
                result.Add(leaf with
                {
                    SignalName = leaf.SignalName.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase)
                        ? leaf.SignalName[..^6] : leaf.SignalName,
                    Quality = companions![0].Value,
                    IsChanged = leaf.IsChanged || companions[0].IsChanged
                });
            else result.Add(leaf);
        }
        return result;
    }

    private static string? CanonicalOwner(string? source, out string kind)
    {
        kind = string.Empty;
        if (string.IsNullOrWhiteSpace(source)) return null;
        var key = source.Trim().Replace('$', '.');
        var slash = key.LastIndexOf('/');
        var dot = key.IndexOf('.', slash + 1);
        if (dot >= 0 && dot + 4 < key.Length &&
            key.AsSpan(dot + 1, 3).Equals("ST.".AsSpan(), StringComparison.OrdinalIgnoreCase))
            key = key.Remove(dot + 1, 3);
        var bracket = key.LastIndexOf('[');
        if (bracket >= 0 && key.EndsWith("]", StringComparison.Ordinal) &&
            key[(bracket + 1)..^1].Equals("ST", StringComparison.OrdinalIgnoreCase))
            key = key[..bracket];
        if (key.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase) ||
            key.EndsWith(".mag.f", StringComparison.OrdinalIgnoreCase))
        {
            kind = "value";
            return key[..^6];
        }
        if (key.EndsWith(".q", StringComparison.OrdinalIgnoreCase))
        {
            kind = "quality";
            return key[..^2];
        }
        return null;
    }
}
