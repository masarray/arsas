using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed record Iec61850DataSetSignalInventoryMergeResult(
    IReadOnlyList<SignalDefinition> AddedSignals,
    int EnrichedExistingCount,
    int MandatoryCatalogCount)
{
    public int AddedCount => AddedSignals.Count;
}

/// <summary>
/// Application-side projection of ARIEC-owned DataSet signal authority.
///
/// ARIEC decides which IEC 61850 signals are mandatory primary DataSet members.
/// ARSAS only guarantees that those already-classified engine signals are present in
/// the user-visible signal inventory. No IEC reference guessing, fuzzy matching, or
/// DataSet semantic inference is performed here.
/// </summary>
public static class Iec61850DataSetSignalInventoryService
{
    public static Iec61850DataSetSignalInventoryMergeResult EnsureMandatorySignals(
        Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.LiveDiscoveryModel is null)
            return new Iec61850DataSetSignalInventoryMergeResult(Array.Empty<SignalDefinition>(), 0, 0);

        var catalog = Iec61850SignalCatalogBuilder.Build(device.LiveDiscoveryModel);
        var mandatory = catalog.GetMandatoryPrimarySignals();
        if (mandatory.Count == 0)
            return new Iec61850DataSetSignalInventoryMergeResult(Array.Empty<SignalDefinition>(), 0, 0);

        var existing = device.Signals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.ObjectReference))
            .GroupBy(signal => NormalizeReference(signal.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var added = new List<SignalDefinition>();
        var enriched = 0;

        foreach (var descriptor in mandatory)
        {
            var reference = FirstNonEmpty(
                descriptor.DesignReference,
                descriptor.PrimaryValueReference,
                descriptor.ObservedReference);
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            var key = NormalizeReference(reference);
            if (existing.TryGetValue(key, out var current))
            {
                if (ApplyEngineDataSetAuthority(current, descriptor))
                    enriched++;
                continue;
            }

            var signal = CreateSignal(descriptor, reference);
            device.Signals.Add(signal);
            existing[key] = signal;
            added.Add(signal);
        }

        return new Iec61850DataSetSignalInventoryMergeResult(added, enriched, mandatory.Count);
    }

    private static SignalDefinition CreateSignal(
        Iec61850SignalDescriptor descriptor,
        string reference)
    {
        var primaryMembership = FirstMembership(descriptor);
        var report = descriptor.ReportMemberships.FirstOrDefault();
        var dataType = FirstNonEmpty(descriptor.MmsType, descriptor.SclBType, "Unknown");
        var signal = new SignalDefinition
        {
            Name = FirstNonEmpty(descriptor.DataObject, descriptor.DataAttributePath, reference),
            ObjectReference = reference,
            FunctionalConstraint = descriptor.FunctionalConstraint,
            DataType = dataType,
            Category = "DataSet",
            Confidence = "High",
            DataSetReference = primaryMembership?.DataSetReference ?? string.Empty,
            ReportControlReference = report?.ReportControlReference ?? string.Empty,
            QualityReference = descriptor.QualityReference,
            TimestampReference = descriptor.TimestampReference,
            Source = "ARIEC61850 signal catalog • mandatory static DataSet member",
            IsSelected = false,
            IsReportCapable = true,
            ReportCoverage = report is null
                ? "Static DataSet member • MMS polling fallback"
                : "Static report/DataSet • polling fallback",
            ReportCoverageReason = BuildCoverageReason(descriptor),
            ProbeStatus = "DataSet member / ready",
            Value = "-",
            Quality = "Unknown",
            DeviceTimestamp = "-"
        };

        return signal;
    }

    private static bool ApplyEngineDataSetAuthority(
        SignalDefinition signal,
        Iec61850SignalDescriptor descriptor)
    {
        var changed = false;
        var membership = FirstMembership(descriptor);
        var report = descriptor.ReportMemberships.FirstOrDefault();

        if (membership is not null &&
            !string.Equals(signal.DataSetReference, membership.DataSetReference, StringComparison.OrdinalIgnoreCase))
        {
            signal.DataSetReference = membership.DataSetReference;
            changed = true;
        }

        if (report is not null &&
            string.IsNullOrWhiteSpace(signal.ReportControlReference))
        {
            signal.ReportControlReference = report.ReportControlReference;
            changed = true;
        }

        if (!signal.IsReportCapable)
        {
            signal.IsReportCapable = true;
            changed = true;
        }

        var reason = BuildCoverageReason(descriptor);
        if (!string.Equals(signal.ReportCoverageReason, reason, StringComparison.Ordinal))
        {
            signal.ReportCoverageReason = reason;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(signal.QualityReference) && !string.IsNullOrWhiteSpace(descriptor.QualityReference))
        {
            signal.QualityReference = descriptor.QualityReference;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(signal.TimestampReference) && !string.IsNullOrWhiteSpace(descriptor.TimestampReference))
        {
            signal.TimestampReference = descriptor.TimestampReference;
            changed = true;
        }

        return changed;
    }

    private static Iec61850SignalDataSetMembership? FirstMembership(Iec61850SignalDescriptor descriptor)
        => descriptor.DataSetMemberships
            .OrderBy(membership => membership.DataSetReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(membership => membership.MemberIndex)
            .FirstOrDefault();

    private static string BuildCoverageReason(Iec61850SignalDescriptor descriptor)
    {
        var memberships = descriptor.DataSetMemberships
            .OrderBy(membership => membership.DataSetReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(membership => membership.MemberIndex)
            .Select(membership => $"{membership.DataSetReference}[{membership.MemberIndex}]")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var membershipText = memberships.Length == 0
            ? "static DataSet membership"
            : string.Join(", ", memberships);

        return $"ARIEC61850 mandatory primary DataSet signal: {membershipText}. " +
               "Inventory presence is engine-authoritative; user selection remains independent.";
    }

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".")
            .ToLowerInvariant();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
