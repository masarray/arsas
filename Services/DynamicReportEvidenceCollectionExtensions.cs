namespace ArIED61850Tester.Services;

/// <summary>
/// Keeps commissioning evidence helpers usable with the ICollection contract used by
/// staged recovery routines without forcing callers to expose a concrete List type.
/// </summary>
internal static class DynamicReportEvidenceCollectionExtensions
{
    internal static void AddRange(this ICollection<string> target, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values)
            target.Add(value);
    }
}
