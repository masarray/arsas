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
/// Operator projection of ordered wire allData. Only a unique model-bound
/// IEC DO owner may absorb its q into the stVal engineering quality column.
/// The separate raw Leaves collection remains untouched for diagnostics.
/// </summary>
public static class GooseCanonicalLeafProjection
{
    public static IReadOnlyList<GooseLeafValueSnapshot> Project(IReadOnlyList<GooseLeafValueSnapshot> wire)
    {
        if (wire.Count == 0) return Array.Empty<GooseLeafValueSnapshot>();
        var owners = new Dictionary<string, List<GooseLeafValueSnapshot>>(StringComparer.OrdinalIgnoreCase);
        var statuses = new List<GooseLeafValueSnapshot>();
        foreach (var leaf in wire)
        {
            if (!ModelBound(leaf) || !TryOwner(leaf, out var owner, out var isQuality)) continue;
            if (isQuality) continue;
            statuses.Add(leaf);
            if (!owners.TryGetValue(owner, out var values))
                owners[owner] = values = new List<GooseLeafValueSnapshot>();
            values.Add(leaf);
        }
        var pairs = new Dictionary<int, GooseLeafValueSnapshot>();
        var consumedQuality = new HashSet<int>();
        foreach (var status in statuses)
        {
            if (!TryOwner(status, out var owner, out _) || owners[owner].Count != 1) continue;
            var candidates = wire.Where(q => ModelBound(q) &&
                TryOwner(q, out var qOwner, out var isQuality) && isQuality &&
                owner.Equals(qOwner, StringComparison.OrdinalIgnoreCase) &&
                q.DataSetIndex != status.DataSetIndex && QualityIsReadable(q.Value) &&
                CompatibleFc(status, q) && IdentityDoesNotConflict(status.SignalReference, q.SignalReference))
                .ToArray();
            if (candidates.Length == 0) continue;

            // Prefer one uniquely identified full IEC DO source pair. When
            // metadata uses shortened/DO-scoped references (GE F650), the
            // adjacent FCDA is authoritative only for a unique SCL bound owner.
            var exact = candidates.Where(q =>
                !string.IsNullOrWhiteSpace(ReferenceOwner(status.SignalReference)) &&
                string.Equals(ReferenceOwner(status.SignalReference),
                    ReferenceOwner(q.SignalReference), StringComparison.OrdinalIgnoreCase)).ToArray();
            GooseLeafValueSnapshot? companion = exact.Length == 1 ? exact[0] : null;
            if (exact.Length > 1) continue;
            if (companion is null)
            {
                var adjacent = candidates.Where(q => q.DataSetIndex == status.DataSetIndex + 1).ToArray();
                if (adjacent.Length == 1 && candidates.Length == 1) companion = adjacent[0];
            }
            if (companion is null || !consumedQuality.Add(companion.DataSetIndex)) continue;
            pairs[status.DataSetIndex] = companion;
        }
        var result = new List<GooseLeafValueSnapshot>(wire.Count);
        foreach (var leaf in wire)
        {
            if (consumedQuality.Contains(leaf.DataSetIndex)) continue;
            if (pairs.TryGetValue(leaf.DataSetIndex, out var q))
            {
                var name = leaf.SignalName;
                if (name.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase))
                    name = name[..^6];
                result.Add(leaf with
                {
                    SignalName = name,
                    Quality = q.Value,
                    IsChanged = leaf.IsChanged || q.IsChanged
                });
            }
            else result.Add(leaf);
        }
        return result;
    }

    private static bool ModelBound(GooseLeafValueSnapshot leaf)
        => !string.IsNullOrWhiteSpace(leaf.SignalName) &&
           !leaf.BindingSource.Equals("Unbound", StringComparison.OrdinalIgnoreCase);

    private static bool TryOwner(GooseLeafValueSnapshot leaf, out string owner, out bool isQuality)
    {
        // Display names are established by the ordered model leaves. A
        // DO-scoped SignalReference can lack the stVal/q suffix entirely.
        owner = string.Empty;
        isQuality = false;
        var name = ReferenceSuffix(leaf.SignalName);
        if (name.EndsWith(".q", StringComparison.OrdinalIgnoreCase))
        {
            isQuality = true;
            owner = name[..^2];
            return true;
        }
        if (name.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase))
        {
            owner = name[..^6];
            return true;
        }
        if (name.EndsWith(".mag.f", StringComparison.OrdinalIgnoreCase))
        {
            owner = name[..^6];
            return true;
        }
        // Some SCL aliases use an engineering signal display label rather
        // than the FCDA leaf name. Accept only a typed source reference.
        var reference = ReferenceSuffix(leaf.SignalReference);
        if (reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase))
        {
            isQuality = true;
            owner = reference[..^2];
            return true;
        }
        if (reference.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase))
        {
            owner = reference[..^6];
            return true;
        }
        return false;
    }

    private static bool CompatibleFc(GooseLeafValueSnapshot value, GooseLeafValueSnapshot q)
        => string.IsNullOrWhiteSpace(value.FunctionalConstraint) ||
           string.IsNullOrWhiteSpace(q.FunctionalConstraint) ||
           value.FunctionalConstraint.Equals(q.FunctionalConstraint, StringComparison.OrdinalIgnoreCase);

    private static bool QualityIsReadable(string text)
        => new[] { "Good", "Invalid", "Questionable", "Reserved" }
            .Any(status => text.Equals(status, StringComparison.OrdinalIgnoreCase) ||
                           text.StartsWith(status + " ·", StringComparison.OrdinalIgnoreCase));

    private static bool IdentityDoesNotConflict(string? statusRef, string? qualityRef)
    {
        var a = ReferenceOwner(statusRef);
        var b = ReferenceOwner(qualityRef);
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return true;
        var slashA = a.LastIndexOf('/');
        var slashB = b.LastIndexOf('/');
        if (slashA >= 0 && slashB >= 0) return a.Equals(b, StringComparison.OrdinalIgnoreCase);
        return (slashA >= 0 ? a[(slashA + 1)..] : a)
            .Equals(slashB >= 0 ? b[(slashB + 1)..] : b, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReferenceOwner(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var reference = text.Trim().Replace('$', '.');
        var slash = reference.LastIndexOf('/');
        var st = reference.IndexOf(".ST.", slash + 1, StringComparison.OrdinalIgnoreCase);
        if (st >= 0) reference = reference.Remove(st, 3);
        foreach (var suffix in new[] { ".stVal", ".q", ".mag.f" })
            if (reference.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return reference[..^suffix.Length];
        return reference;
    }

    private static string ReferenceSuffix(string? text)
    {
        var normalized = text?.Trim().Replace('$', '.') ?? string.Empty;
        var slash = normalized.LastIndexOf('/');
        if (slash >= 0) normalized = normalized[(slash + 1)..];
        var st = normalized.IndexOf(".ST.", StringComparison.OrdinalIgnoreCase);
        if (st >= 0) normalized = normalized.Remove(st, 3);
        return normalized;
    }
}
