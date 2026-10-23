using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Services;

/// <summary>
/// Maps one typed WYE/DEL whole-DO static FCDA into named phase magnitudes.
/// No MMS reads, numeric-child selection, or additional DataSet/RCB are created.
/// </summary>
public static class Iec61850StaticPhaseLeafProjection
{
    public sealed record Leaf(
        string Reference, string Phase, string DataType,
        string QualityReference, string TimestampReference);

    public static IReadOnlyList<Leaf> Resolve(
        LiveIedModelDiscoveryDocument? model, string? memberReference, string? fc)
    {
        if (model is null || string.IsNullOrWhiteSpace(memberReference) ||
            !string.Equals(fc, "MX", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<Leaf>();

        var normalized = Normalize(memberReference);
        var owners = model.LogicalDevices.SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects)
            .Where(obj => string.Equals(Normalize(obj.Reference), normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (owners.Length != 1)
            return Array.Empty<Leaf>();

        var owner = owners[0];
        var phases = (owner.InferredCdc ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "WYE" => new[] { "phsA", "phsB", "phsC", "neut", "net", "res" },
            "DEL" => new[] { "phsAB", "phsBC", "phsCA" },
            _ => Array.Empty<string>()
        };
        var result = new List<Leaf>(phases.Length);
        foreach (var phase in phases)
        {
            var prefix = normalized + "." + phase;
            if (!SchemaSafeAggregateProjectionService.TryResolveStaticDataSetPrimaryLeaf(
                    model, prefix, "MX", out var leaf, out _) ||
                !IsNumeric(leaf.DataType) ||
                !Normalize(leaf.Reference).StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(new Leaf(
                leaf.Reference, phase, leaf.DataType,
                HasAttribute(owner, prefix + ".q") ? prefix + ".q" : "",
                HasAttribute(owner, prefix + ".t") ? prefix + ".t" : ""));
        }
        return result;
    }

    private static bool HasAttribute(LiveIedDataObjectModel owner, string reference)
        => owner.Attributes.Any(attribute =>
            string.Equals(attribute.FunctionalConstraint, "MX", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Normalize(string.IsNullOrWhiteSpace(attribute.ObjectReference)
                ? owner.Reference + "." + attribute.AttributePath : attribute.ObjectReference),
                reference, StringComparison.OrdinalIgnoreCase));

    private static bool IsNumeric(string? type)
    {
        var value = (type ?? string.Empty).Trim();
        return value.Equals("FLOAT32", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("FLOAT64", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("floating-point", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("INT32", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("INT64", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');
}
