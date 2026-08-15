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
    public bool IsComplete => MandatoryInventoryCount == RepresentedCount && MissingCount == 0;

    public string Summary =>
        $"DataSets={DataSetCount:N0}; static members={StaticMemberCount:N0}; mandatory inventory={MandatoryInventoryCount:N0}; " +
        $"represented={RepresentedCount:N0}/{MandatoryInventoryCount:N0}; primary leaf unresolved={PrimaryLeafUnresolvedCount:N0}; missing={MissingCount:N0}";
}

/// <summary>
/// Actionable diagnostic for static IEC 61850 DataSet inventory completeness.
/// ARIEC remains the authority for mandatory signal descriptors and reference identity;
/// ARSAS only measures whether those descriptors are represented in Signal Selection.
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
            .Select(signal => Literal(signal.ObjectReference))
            .Where(reference => reference.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        var represented = 0;
        foreach (var descriptor in mandatory)
        {
            var candidates = EngineReferenceCandidates(descriptor).ToArray();
            if (candidates.Any(signalReferences.Contains))
            {
                represented++;
                continue;
            }

            var reference = candidates.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(reference))
                reference = "<no engine reference>";

            var membership = descriptor.DataSetMemberships
                .OrderBy(item => item.DataSetReference, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.MemberIndex)
                .FirstOrDefault();
            missing.Add(membership is null
                ? reference
                : $"{membership.DataSetReference}[{membership.MemberIndex}] -> {reference}");
        }

        return new Iec61850DataSetCompletenessSnapshot(
            model.DataSets.Count,
            model.DataSets.Sum(dataSet => dataSet.Members.Count),
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
        yield return $"Mandatory inventory: {snapshot.MandatoryInventoryCount:N0}";
        yield return $"Signal Selection  : {snapshot.RepresentedCount:N0}/{snapshot.MandatoryInventoryCount:N0} represented";
        yield return $"Primary unresolved: {snapshot.PrimaryLeafUnresolvedCount:N0}";
        yield return $"Missing inventory : {snapshot.MissingCount:N0}";

        if (snapshot.MissingCount == 0)
            yield break;

        foreach (var reference in snapshot.MissingReferences.Take(Math.Max(0, maxMissing)))
            yield return $"  MISSING         : {reference}";

        if (snapshot.MissingCount > maxMissing)
            yield return $"  ...             : {snapshot.MissingCount - maxMissing:N0} more missing member(s)";
    }

    private static IEnumerable<string> EngineReferenceCandidates(Iec61850SignalDescriptor descriptor)
    {
        return new[]
            {
                descriptor.PrimaryValueReference,
                descriptor.DesignReference,
                descriptor.ObservedReference,
                descriptor.PrimaryValueMmsReference,
                descriptor.CanonicalMmsReference,
                descriptor.EffectiveMmsReference,
                descriptor.ObservedMmsReference
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Literal)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string Literal(string? reference)
        => (reference ?? string.Empty).Trim();
}
