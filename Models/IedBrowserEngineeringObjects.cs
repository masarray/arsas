using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Browser-only schema coalescing: a discovery or SCL importer may give
/// separate DO declarations per FC. Those declarations are one engineering
/// object only when the exact IEC DO reference AND object name match.
/// Distinct FC leaves remain separate and the canonical model is untouched.
/// </summary>
public static class IedBrowserEngineeringObjects
{
    public static IReadOnlyList<LiveIedDataObjectModel> GroupExact(
        IReadOnlyList<LiveIedDataObjectModel> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        return objects.GroupBy(o => (o.Reference, o.Name))
            .Select(group =>
            {
                var first = group.First();
                if (group.Count() == 1) return first;
                // A conflicting CDC or type declaration cannot be silently
                // resolved by first-value-wins. Keep ambiguous metadata blank.
                var cdc = Unique(group.Select(o => o.InferredCdc));
                var doType = Unique(group.Select(o => o.ProposedDoTypeId));
                var attributes = group.SelectMany(o => o.Attributes)
                    .DistinctBy(a => (a.MmsReference, a.FunctionalConstraint,
                                      a.AttributePath, a.MmsTypeSignature))
                    .ToArray();
                return new LiveIedDataObjectModel
                {
                    Reference = first.Reference,
                    Name = first.Name,
                    InferredCdc = cdc,
                    ProposedDoTypeId = doType,
                    CdcConfidence = cdc.Length > 0 ? group.Max(x => x.CdcConfidence) : 0,
                    ConfidenceLevel = group.All(x => x.ConfidenceLevel == first.ConfidenceLevel)
                        ? first.ConfidenceLevel : LiveIedDiscoveryConfidenceLevel.Unknown,
                    Evidence = group.SelectMany(x => x.Evidence)
                        .Distinct(StringComparer.Ordinal).ToArray(),
                    TypeDeclarationOrder = group.Select(x => x.TypeDeclarationOrder)
                        .Where(s => s.Length > 0)
                        .OrderBy(s => s, StringComparer.Ordinal).FirstOrDefault() ?? "",
                    Attributes = attributes
                };
            }).ToArray();
    }

    private static string Unique(IEnumerable<string> candidates)
    {
        var values = candidates.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return values.Length == 1 ? values[0] : "";
    }
}
