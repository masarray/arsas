using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed record Iec61850DataSetCompletenessDataSetSnapshot(
    string Reference,
    int StaticMemberCount,
    int RepresentedCount,
    IReadOnlyList<string> MissingReferences)
{
    public int MissingCount => MissingReferences.Count;
}

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
    public IReadOnlyList<Iec61850DataSetCompletenessDataSetSnapshot> DataSets { get; init; }
        = Array.Empty<Iec61850DataSetCompletenessDataSetSnapshot>();

    public string Summary =>
        $"DataSets={DataSetCount:N0}; static members={StaticMemberCount:N0}; semantic descriptors={MandatoryInventoryCount:N0}; " +
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
        var staticMemberCount = 0;
        var dataSetSnapshots = new List<Iec61850DataSetCompletenessDataSetSnapshot>();

        foreach (var dataSet in model.DataSets.OrderBy(item => item.Reference, StringComparer.OrdinalIgnoreCase))
        {
            var dataSetMissing = new List<string>();
            var dataSetRepresented = 0;
            var members = dataSet.Members.OrderBy(member => member.Index).ToArray();
            staticMemberCount += members.Length;

            foreach (var member in members)
            {
                var memberReference = Literal(member.Reference);
                if (memberReference.Length > 0 && signalReferences.Contains(memberReference))
                {
                    represented++;
                    dataSetRepresented++;
                    continue;
                }

                var reference = memberReference.Length == 0 ? "<no static member reference>" : memberReference;
                var diagnosticReference = $"{dataSet.Reference}[{member.Index}] -> {reference}";
                missing.Add(diagnosticReference);
                dataSetMissing.Add(diagnosticReference);
            }

            dataSetSnapshots.Add(new Iec61850DataSetCompletenessDataSetSnapshot(
                dataSet.Reference,
                members.Length,
                dataSetRepresented,
                dataSetMissing));
        }

        return new Iec61850DataSetCompletenessSnapshot(
            model.DataSets.Count,
            staticMemberCount,
            mandatory.Count,
            represented,
            mandatory.Count(descriptor => descriptor.ResolutionStatus == Iec61850SignalCatalogResolutionStatus.Unresolved),
            missing)
        {
            DataSets = dataSetSnapshots
        };
    }

    public static IEnumerable<string> FormatReportLines(Iec61850DataSetCompletenessSnapshot snapshot, int maxMissing = 12)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        yield return $"Static DataSets      : {snapshot.DataSetCount:N0}";
        yield return $"Static members       : {snapshot.StaticMemberCount:N0}";
        yield return $"Signal Selection     : {snapshot.RepresentedCount:N0}/{snapshot.StaticMemberCount:N0} static member(s) represented";
        yield return $"Missing static member: {snapshot.MissingCount:N0}";
        yield return $"Semantic descriptors : {snapshot.MandatoryInventoryCount:N0}";
        yield return $"Primary unresolved   : {snapshot.PrimaryLeafUnresolvedCount:N0}";

        foreach (var dataSet in snapshot.DataSets)
        {
            yield return $"  {dataSet.Reference}: {dataSet.RepresentedCount:N0}/{dataSet.StaticMemberCount:N0} represented • {dataSet.MissingCount:N0} missing";
        }

        if (snapshot.MissingCount == 0 || maxMissing <= 0)
            yield break;

        // Sample every failing DataSet instead of taking only the first N members from
        // the first DataSet. This keeps Analog and Digital evidence visible together.
        var failingDataSets = snapshot.DataSets
            .Where(dataSet => dataSet.MissingCount > 0)
            .ToArray();
        var perDataSetLimit = Math.Max(1, maxMissing / Math.Max(1, failingDataSets.Length));
        var emitted = 0;

        foreach (var dataSet in failingDataSets)
        {
            foreach (var reference in dataSet.MissingReferences.Take(perDataSetLimit))
            {
                if (emitted >= maxMissing)
                    break;
                yield return $"  MISSING            : {reference}";
                emitted++;
            }

            if (emitted >= maxMissing)
                break;
        }

        if (snapshot.MissingCount > emitted)
            yield return $"  ...                : {snapshot.MissingCount - emitted:N0} more missing member(s)";
    }

    private static string Literal(string? reference)
        => (reference ?? string.Empty).Trim();
}
