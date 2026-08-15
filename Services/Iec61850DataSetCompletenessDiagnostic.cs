using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed record Iec61850DataSetCompletenessSnapshot(
    int DataSetCount,
    int StaticMemberCount,
    int MandatoryInventoryCount,
    int RepresentedCount,
    int PrimaryLeafUnresolvedCount,
    IReadOnlyList<string> MissingReferences)
{
    public int MissingCount => MissingReferences.Count;
    public bool IsComplete => StaticMemberCount == RepresentedCount && MissingCount == 0;

    public string Summary =>
        $"DataSets={DataSetCount:N0}; static members={StaticMemberCount:N0}; mandatory inventory={MandatoryInventoryCount:N0}; " +
        $"represented={RepresentedCount:N0}/{StaticMemberCount:N0}; primary leaf unresolved={PrimaryLeafUnresolvedCount:N0}; missing={MissingCount:N0}";
}

/// <summary>
/// Actionable diagnostic for static IEC 61850 DataSet inventory completeness.
/// ARIEC remains the authority for reference identity and primary-leaf resolution.
/// ARSAS measures every static FCDA member directly so projection aggregation can never
/// make a partially represented DataSet appear complete.
/// </summary>
public static class Iec61850DataSetCompletenessDiagnostic
{
    public static Iec61850DataSetCompletenessSnapshot Evaluate(
        LiveIedModelDiscoveryDocument? model,
        IEnumerable<SignalDefinition> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        if (model is null)
            return new Iec61850DataSetCompletenessSnapshot(0, 0, 0, 0, 0, Array.Empty<string>());

        var mandatory = Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(model);
        var signalReferences = signals
            .SelectMany(signal => new[] { signal.DisplayReference, signal.ObjectReference })
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(Literal)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        var represented = 0;
        var staticMembers = model.DataSets
            .OrderBy(dataSet => dataSet.Reference, StringComparer.OrdinalIgnoreCase)
            .SelectMany(dataSet => dataSet.Members
                .OrderBy(member => member.Index)
                .Select(member => new
                {
                    DataSetReference = dataSet.Reference,
                    member.Index,
                    Reference = Literal(member.Reference)
                }))
            .ToArray();

        foreach (var member in staticMembers)
        {
            if (member.Reference.Length > 0 && signalReferences.Contains(member.Reference))
            {
                represented++;
                continue;
            }

            var reference = member.Reference.Length == 0 ? "<no static member reference>" : member.Reference;
            missing.Add($"{member.DataSetReference}[{member.Index}] -> {reference}");
        }

        return new Iec61850DataSetCompletenessSnapshot(
            model.DataSets.Count,
            staticMembers.Length,
            mandatory.Count,
            represented,
            mandatory.Count(descriptor => descriptor.ResolutionStatus == Iec61850SignalCatalogResolutionStatus.Unresolved),
            missing);
    }

    public static IEnumerable<string> FormatReportLines(Iec61850DataSetCompletenessSnapshot snapshot, int maxMissing = 12)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        yield return $"Static DataSets   : {snapshot.DataSetCount:N0}";
        yield return $"Static members    : {snapshot.StaticMemberCount:N0}";
        yield return $"Mandatory inventory: {snapshot.MandatoryInventoryCount:N0} descriptor(s)";
        yield return $"Signal Selection  : {snapshot.RepresentedCount:N0}/{snapshot.StaticMemberCount:N0} static member(s) represented";
        yield return $"Primary unresolved: {snapshot.PrimaryLeafUnresolvedCount:N0}";
        yield return $"Missing inventory : {snapshot.MissingCount:N0}";

        if (snapshot.MissingCount == 0)
            yield break;

        foreach (var reference in snapshot.MissingReferences.Take(Math.Max(0, maxMissing)))
            yield return $"  MISSING         : {reference}";

        if (snapshot.MissingCount > maxMissing)
            yield return $"  ...             : {snapshot.MissingCount - maxMissing:N0} more missing member(s)";
    }

    private static string Literal(string? reference)
        => (reference ?? string.Empty).Trim();
}
