using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Fail-closed, read-only fallback for an exact MMS LN$FC$DO parent.
/// A per-attribute MMS Read is not always interoperable with vendors that
/// expose the corresponding DO as one typed structure (notably SE/ST).
/// GVA component names, wire field order and actual decoded primitive types
/// are mandatory. SCL declarations never become observed values.
/// </summary>
public static class IedBrowserVerifiedDoProjection
{
    public static string ParentFor(IedBrowserReadTarget target)
    {
        var fc = (target.FunctionalConstraint ?? "").Trim().ToUpperInvariant();
        if (fc is not ("ST" or "SE")) return "";

        // Resolve the requested DO without case-insensitive matching or
        // assuming that another DO on the same LN has interchangeable fields.
        var canonical = IedBrowserLiveValues.CanonicalReference(target.Reference);
        var slash = canonical.IndexOf('/');
        if (slash < 1) return "";
        var lnStart = slash + 1;
        var firstDot = canonical.IndexOf('.', lnStart);
        if (firstDot <= lnStart) return "";
        var secondDot = canonical.IndexOf('.', firstDot + 1);
        if (secondDot <= firstDot + 1 || secondDot == canonical.Length - 1)
            return "";
        var ln = canonical[lnStart..firstDot];
        var dataObject = canonical[(firstDot + 1)..secondDot];
        if (!IsIdentifier(ln) || !IsIdentifier(dataObject)) return "";
        var domain = canonical[..slash];
        if (!IsIdentifier(domain)) return "";

        var declaredMms = target.Reference;
        if (declaredMms.Contains('$'))
        {
            // Never recast a different wire FC, DO or spelling as this parent.
            var exactPrefix = domain + "/" + ln + "$" + fc + "$" + dataObject + "$";
            if (!declaredMms.StartsWith(exactPrefix, StringComparison.Ordinal))
                return "";
        }
        return domain + "/" + ln + "$" + fc + "$" + dataObject;
    }

    private static bool IsIdentifier(string text)
        => text.Length is > 0 and <= 128 &&
           text.All(ch => char.IsLetterOrDigit(ch) || ch == '_');

    public static IReadOnlyList<IedBrowserReadResult> Project(
        string parent, MmsTypeSpecificationNode? type,
        MmsDataValue? data, IReadOnlyList<IedBrowserReadTarget> verifiedTargets)
    {
        if (type is null || data is null ||
            !string.Equals(type.MmsType, "structure", StringComparison.OrdinalIgnoreCase) ||
            data.Kind != MmsDataKind.Structure ||
            type.Children.Count is < 1 or > 128 ||
            type.Children.Count != data.Children.Count ||
            verifiedTargets.Count is < 1 or > 128)
            return [];

        var slash = parent.IndexOf('/');
        var marker = parent.LastIndexOf('$');
        var fcMarker = parent.IndexOf('$');
        if (slash < 1 || fcMarker < slash || marker <= fcMarker)
            return [];
        var fc = parent[(fcMarker + 1)..marker];
        if (fc is not ("ST" or "SE")) return [];
        var prefix = parent[..fcMarker] + "." + parent[(marker + 1)..];
        var targets = verifiedTargets
            .Where(t => string.Equals(ParentFor(t), parent, StringComparison.Ordinal))
            .GroupBy(t => IedBrowserLiveValues.CanonicalReference(t.Reference) + "|" +
                          t.FunctionalConstraint.Trim().ToUpperInvariant(),
                     StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        if (targets.Count == 0) return [];

        // Reject a malformed/misaligned structure IN FULL before any leaf
        // reaches the canonical evidence publisher. No partial ordinal mapping.
        static bool ShapeIsVerified(MmsTypeSpecificationNode schema,
            MmsDataValue wire, int depth)
        {
            if (depth > 12) return false;
            if (wire.Kind != MmsDataKind.Structure)
                return schema.Children.Count == 0 &&
                    wire.Kind is not (MmsDataKind.Structure or MmsDataKind.Array) &&
                    IedBrowserSgRootProjection.IsMatchingPrimitiveType(
                        schema.MmsType, wire.Kind);
            if (!schema.MmsType.Equals("structure", StringComparison.OrdinalIgnoreCase) ||
                schema.Children.Count is < 1 or > 128 ||
                schema.Children.Count != wire.Children.Count ||
                schema.Children.Any(x => string.IsNullOrWhiteSpace(x.Name)) ||
                schema.Children.Select(x => x.Name).Distinct(StringComparer.Ordinal)
                    .Count() != schema.Children.Count)
                return false;
            for (var i = 0; i < schema.Children.Count; i++)
                if (!ShapeIsVerified(schema.Children[i], wire.Children[i], depth + 1))
                    return false;
            return true;
        }
        if (!ShapeIsVerified(type, data, 0)) return [];

        var found = new List<IedBrowserReadResult>();
        void Walk(MmsTypeSpecificationNode schema, MmsDataValue wire,
            string path, int depth)
        {
            if (depth > 12) return;
            if (wire.Kind == MmsDataKind.Structure)
            {
                for (var i = 0; i < schema.Children.Count; i++)
                    Walk(schema.Children[i], wire.Children[i],
                        path.Length == 0 ? schema.Children[i].Name :
                        path + "." + schema.Children[i].Name, depth + 1);
                return;
            }
            if (path.Length == 0) return;
            if (!targets.TryGetValue(prefix + "." + path + "|" + fc, out var target))
                return;
            var stringKind = wire.Kind is MmsDataKind.VisibleString or MmsDataKind.MmsString
                ? wire.Kind : (MmsDataKind?)null;
            var rendered = stringKind is not null && wire.Value is string direct
                ? direct : MmsDataValueRenderer.ToCompactString(wire);
            if (IedBrowserReadValuePolicy.TryAcceptVerifiedMmsString(
                    rendered, target.DataType, stringKind, out var accepted, out _))
                found.Add(new IedBrowserReadResult(target, accepted, "", "", "",
                    TypedBoolean: wire.Kind == MmsDataKind.Boolean && wire.Value is bool typed
                        ? typed : null,
                    VerifiedStringWireKind: stringKind,
                    WireShapeEvidence: IedBrowserReadWireShapeEvidence.Describe(wire)));
        }
        Walk(type, data, "", 0);
        return found;
    }
}
