using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Admission of IEC FC=SG or SP scalars from one verified wire-read LN$SG / LN$SP0 STRUCTURE,
/// only with the ordered wire GetVariableAccessAttributes TypeSpecification.
/// There is no ordinal heuristic, SCL-value fallback, second cache or write.
/// </summary>
public static class IedBrowserSgRootProjection
{
    // A dense SGCB may declare hundreds of siblings under a single verified
    // typed FC root. The former 128-target veto forced hundreds of individual
    // leaf attempts even when one typed root Read could prove exact children.
    // This is a per-root bound, never permission to scan other LNs or FCs.
    public static bool CanReadRoot(int siblingCount)
        => siblingCount is > 0 and <= 512;

    public static string RootFor(IedBrowserReadTarget target)
    {
        var fc = (target.FunctionalConstraint ?? "").Trim().ToUpperInvariant();
        if (fc is not ("SG" or "SP" or "SE" or "CF" or "DC" or "EX")) return "";
        var marker = "$" + fc + "$";
        // BCUGE's named-variable inventory exposes FC group variables
        // LN$SP0, LN$SG0, LN$SE0, LN$CF0, LN$DC0 and LN$EX0. The name is a
        // CANDIDATE only: an exact GVA typed structure and successful
        // read must precede every scalar's ledger publication.
        var suffix = "$" + fc + "0";
        var wire = target.Reference;
        var cut = wire.IndexOf(marker, StringComparison.Ordinal);
        if (cut > 0 && wire.Contains('/'))
            return wire[..cut] + suffix;
        // Canonical IEC dotted naming; FC comes ONLY from verified target.
        var canonical = IedBrowserLiveValues.CanonicalReference(wire);
        var slash = canonical.IndexOf('/');
        var dot = slash < 0 ? -1 : canonical.IndexOf('.', slash + 1);
        return dot > slash + 1 ? canonical[..dot] + suffix : "";
    }

    public static IReadOnlyList<IedBrowserReadResult> Project(
        string root, MmsTypeSpecificationNode? wireType,
        MmsDataValue? wireData, IReadOnlyList<IedBrowserReadTarget> expected)
    {
        if (wireType is null || wireData is null ||
            wireType.Children.Count == 0 ||
            wireData.Kind != MmsDataKind.Structure ||
            !string.Equals(wireType.MmsType, "structure",
                StringComparison.OrdinalIgnoreCase))
            return [];
        var targetIndex = expected
            .Where(target => string.Equals(RootFor(target), root, StringComparison.Ordinal))
            .GroupBy(target => IedBrowserLiveValues.CanonicalReference(target.Reference) +
                "|" + target.FunctionalConstraint, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var slash = root.IndexOf('/');
        var constraint = root.EndsWith("$SP0", StringComparison.Ordinal) ? "SP"
            : root.EndsWith("$SG0", StringComparison.Ordinal) ? "SG"
            : root.EndsWith("$SE0", StringComparison.Ordinal) ? "SE"
            : root.EndsWith("$CF0", StringComparison.Ordinal) ? "CF"
            : root.EndsWith("$DC0", StringComparison.Ordinal) ? "DC"
            : root.EndsWith("$EX0", StringComparison.Ordinal) ? "EX" : "";
        var cut = root.LastIndexOf('$');
        if (slash < 1 || cut <= slash || constraint.Length == 0) return [];
        var prefix = root[..cut];
        var output = new List<IedBrowserReadResult>();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        void Walk(MmsTypeSpecificationNode schema, MmsDataValue value, string path, int depth)
        {
            if (depth > 12) return;
            if (value.Kind == MmsDataKind.Structure)
            {
                // Ordered MMS type components are the sole path authority.
                if (schema.Children.Count == 0 ||
                    schema.Children.Count != value.Children.Count ||
                    schema.Children.Any(node => string.IsNullOrWhiteSpace(node.Name)) ||
                    schema.Children.Select(node => node.Name).Distinct(StringComparer.Ordinal)
                        .Count() != schema.Children.Count)
                    return;
                for (var i = 0; i < schema.Children.Count; i++)
                {
                    var child = schema.Children[i];
                    Walk(child, value.Children[i],
                        path.Length == 0 ? child.Name : path + "." + child.Name,
                        depth + 1);
                }
                return;
            }
            // IEC SG array values need indexed schema identity and are not
            // flattened into imaginary scalar targets.
            if (value.Kind is MmsDataKind.Array or MmsDataKind.Unknown ||
                schema.Children.Count != 0 || path.Length == 0 ||
                !IsMatchingPrimitiveType(schema.MmsType, value.Kind))
                return;
            var canonical = prefix + "." + path;
            var key = canonical + "|" + constraint;
            if (!paths.Add(key) || !targetIndex.TryGetValue(key, out var target))
                return;
            var display = MmsDataValueRenderer.ToCompactString(value);
            if (!IedBrowserReadValuePolicy.TryAcceptDisplay(
                    display, out var accepted, out _))
                return;
            output.Add(new IedBrowserReadResult(target, accepted, "", "", ""));
        }
        Walk(wireType, wireData, "", 0);
        return output;
    }

    internal static bool IsMatchingPrimitiveType(string mmsType, MmsDataKind kind)
        => mmsType.Trim().ToLowerInvariant() switch
        {
            "boolean" => kind == MmsDataKind.Boolean,
            "enumerated" => kind is MmsDataKind.Integer or MmsDataKind.Unsigned,
            "enum" => kind is MmsDataKind.Integer or MmsDataKind.Unsigned,
            // Preserve signedness proven by the live GVA type tree. A
            // negative Integer is never an Unsigned scalar and vice versa.
            "integer" => kind == MmsDataKind.Integer,
            "unsigned" => kind == MmsDataKind.Unsigned,
            "floating-point" => kind == MmsDataKind.FloatingPoint,
            "visible-string" => kind is MmsDataKind.VisibleString or MmsDataKind.MmsString,
            "mms-string" => kind is MmsDataKind.MmsString or MmsDataKind.VisibleString,
            "bit-string" => kind == MmsDataKind.BitString,
            "utc-time" => kind == MmsDataKind.UtcTime,
            "octet-string" => kind == MmsDataKind.OctetString,
            "binary-time" => kind == MmsDataKind.BinaryTime,
            _ => false
        };
}
