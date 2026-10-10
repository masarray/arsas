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
/// Engineering readout only: preserves every original ordered GOOSE FCDA in
/// GooseStreamRow.Leaves. Pairs q only where its owner is unambiguous.
/// </summary>
public static class GooseCanonicalLeafProjection
{
    public static IReadOnlyList<GooseLeafValueSnapshot> Project(IReadOnlyList<GooseLeafValueSnapshot> wireLeaves)
    {
        var values = wireLeaves.Where(leaf =>
            Owner(leaf.SignalReference, out var kind) is not null && kind == "value").ToArray();
        var qualities = wireLeaves.Where(leaf =>
            Owner(leaf.SignalReference, out var kind) is not null && kind == "quality").ToArray();

        var pairs = new Dictionary<int, GooseLeafValueSnapshot>();
        var pairedQualities = new HashSet<int>();
        foreach (var value in wireLeaves)
        {
            if (!IsValue(value) &&
                !(IsModelBound(value) && value.SignalName.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase)))
                continue;
            var valueRef = Owner(value.SignalReference, out _);
            var exact = qualities.Where(q =>
                string.Equals(valueRef, Owner(q.SignalReference, out _), StringComparison.OrdinalIgnoreCase)).ToArray();
            // Exact qualified-reference pairing has priority. Never accept
            // duplicate status/quality owners as a unique match.
            var exactValueCount = values.Count(other =>
                string.Equals(valueRef, Owner(other.SignalReference, out _), StringComparison.OrdinalIgnoreCase));
            GooseLeafValueSnapshot? candidate = exact.Length == 1 && exactValueCount == 1
                ? exact[0] : null;

            if (candidate is null && exact.Length == 0 && IsModelBound(value))
            {
                // Some SCL publishers expose mixed abbreviated DA references.
                // The ordered wire DataSet itself provides a stronger fallback:
                // adjacent typed q following stVal, same FC, exact LN.DO name,
                // unique in this stream. Never match unrelated LD-qualified refs.
                var ownerName = Owner(value.SignalName, out var valueKind);
                var adjacent = wireLeaves.Where(q => q.DataSetIndex == value.DataSetIndex + 1 &&
                    IsModelBound(q) && IsQuality(q) &&
                    q.FunctionalConstraint.Equals(value.FunctionalConstraint, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ownerName, Owner(q.SignalName, out var qKind), StringComparison.OrdinalIgnoreCase) &&
                    valueKind == "value" && qKind == "quality" &&
                    (CompatibleOwners(valueRef, Owner(q.SignalReference, out _)) ||
                     CompatibleOwners(DoScopedReference(value.SignalReference),
                                      DoScopedReference(q.SignalReference)))).ToArray();
                if (adjacent.Length == 1)
                {
                    var occurrences = wireLeaves.Count(row => IsValue(row) &&
                        string.Equals(ownerName, Owner(row.SignalName, out _), StringComparison.OrdinalIgnoreCase));
                    if (occurrences == 1) candidate = adjacent[0];
                }
            }
            if (candidate is null && IsModelBound(value) &&
                value.SignalName.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase))
            {
                // Some GE F650 SCL models publish typed q in the next FCDA but
                // use different naming conventions for MMS reference vs display.
                // Only two *unique*, adjacent, model-bound wire members with
                // identical IEC LN.DO and a decoded quality status may be joined.
                var shortOwner = Owner(value.SignalName, out var valueKind);
                var neighbors = wireLeaves.Where(q =>
                    q.DataSetIndex == value.DataSetIndex + 1 &&
                    IsModelBound(q) &&
                    q.SignalName.EndsWith(".q", StringComparison.OrdinalIgnoreCase) &&
                    Owner(q.SignalName, out var qKind) == shortOwner &&
                    qKind == "quality" && valueKind == "value" &&
                    IsReadableQuality(q.Value) &&
                    (string.IsNullOrWhiteSpace(q.FunctionalConstraint) ||
                     string.IsNullOrWhiteSpace(value.FunctionalConstraint) ||
                     q.FunctionalConstraint.Equals(value.FunctionalConstraint, StringComparison.OrdinalIgnoreCase)) &&
                    (CompatibleOwners(valueRef, Owner(q.SignalReference, out _)) ||
                     CompatibleOwners(DoScopedReference(value.SignalReference),
                                      DoScopedReference(q.SignalReference)))).ToArray();
                if (neighbors.Length == 1 &&
                    wireLeaves.Count(row => row.SignalName.Equals(value.SignalName, StringComparison.OrdinalIgnoreCase)) == 1)
                    candidate = neighbors[0];
            }
            if (candidate is null || !pairedQualities.Add(candidate.DataSetIndex))
                continue;
            pairs[value.DataSetIndex] = candidate;
        }

        var result = new List<GooseLeafValueSnapshot>(wireLeaves.Count);
        foreach (var leaf in wireLeaves)
        {
            if (pairedQualities.Contains(leaf.DataSetIndex)) continue;
            if (pairs.TryGetValue(leaf.DataSetIndex, out var quality))
                result.Add(leaf with
                {
                    SignalName = leaf.SignalName.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase)
                        ? leaf.SignalName[..^6] : leaf.SignalName,
                    Quality = quality.Value,
                    IsChanged = leaf.IsChanged || quality.IsChanged
                });
            else result.Add(leaf);
        }
        return result;
    }

    private static string? DoScopedReference(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var key = input.Trim().Replace('$', '.');
        var slash = key.LastIndexOf('/');
        var dot = key.IndexOf('.', slash + 1);
        if (dot >= 0 && dot + 4 < key.Length &&
            key.AsSpan(dot + 1, 3).Equals("ST.".AsSpan(), StringComparison.OrdinalIgnoreCase))
            key = key.Remove(dot + 1, 3);
        foreach (var suffix in new[] { ".stVal", ".q" })
            if (key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                key = key[..^suffix.Length];
        return key;
    }

    private static bool IsReadableQuality(string value) =>
        value.Equals("Good", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("Good ·", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Invalid", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("Invalid ·", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Questionable", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("Questionable ·", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Reserved", StringComparison.OrdinalIgnoreCase);

    private static bool IsModelBound(GooseLeafValueSnapshot leaf)
        => !leaf.BindingSource.Equals("Unbound", StringComparison.OrdinalIgnoreCase) &&
           !string.IsNullOrWhiteSpace(leaf.SignalReference);

    private static bool IsValue(GooseLeafValueSnapshot leaf)
        => Owner(leaf.SignalReference, out var kind) is not null && kind == "value";

    private static bool IsQuality(GooseLeafValueSnapshot leaf)
        => Owner(leaf.SignalReference, out var kind) is not null && kind == "quality" &&
           (GooseTypedValueInterpreter.IsQuality(leaf.SignalReference, leaf.BType) ||
            leaf.SignalName.EndsWith(".q", StringComparison.OrdinalIgnoreCase));

    private static bool CompatibleOwners(string? primary, string? quality)
    {
        if (primary is null || quality is null) return false;
        if (primary.Equals(quality, StringComparison.OrdinalIgnoreCase)) return true;
        // An abbreviated SCL reference may omit its LD prefix. Two explicit
        // different LD prefixes are NOT equivalent, regardless of matching LN.
        var a = primary.LastIndexOf('/');
        var b = quality.LastIndexOf('/');
        if (a >= 0 && b >= 0) return false;
        return (a >= 0 ? primary[(a+1)..] : primary)
            .Equals(b >= 0 ? quality[(b+1)..] : quality,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? Owner(string? source, out string kind)
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
