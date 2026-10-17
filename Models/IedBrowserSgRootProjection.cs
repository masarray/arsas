using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Admission of IEC FC=SG scalars from one wire-read LN$SG STRUCTURE,
/// only with the ordered wire GetVariableAccessAttributes TypeSpecification.
/// There is no ordinal heuristic, SCL-value fallback, second cache or write.
/// </summary>
public static class IedBrowserSgRootProjection
{
    public static string RootFor(IedBrowserReadTarget target)
    {
        if (!string.Equals(target.FunctionalConstraint, "SG",
                StringComparison.OrdinalIgnoreCase))
            return "";
        var wire = target.Reference;
        var cut = wire.IndexOf("$SG$", StringComparison.Ordinal);
        if (cut > 0 && wire.Contains('/'))
            return wire[..cut] + "$SG";
        // Canonical IEC dotted naming; FC comes ONLY from verified target.
        var canonical = IedBrowserLiveValues.CanonicalReference(wire);
        var slash = canonical.IndexOf('/');
        var dot = slash < 0 ? -1 : canonical.IndexOf('.', slash + 1);
        return dot > slash + 1 ? canonical[..dot] + "$SG" : "";
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
        var fc = root.LastIndexOf("$SG", StringComparison.Ordinal);
        if (slash < 1 || fc <= slash) return [];
        var prefix = root[..fc];
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
                schema.Children.Count != 0 || path.Length == 0)
                return;
            var canonical = prefix + "." + path;
            var key = canonical + "|SG";
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
}
