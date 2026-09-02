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
/// ARIEC decides which IEC 61850 signals are mandatory DataSet inventory members.
/// ARSAS only guarantees that those engine-owned descriptors are present in the
/// user-visible signal inventory. No IEC reference guessing, fuzzy matching, or
/// DataSet semantic inference is performed here.
/// </summary>
public static class Iec61850DataSetSignalInventoryService
{
    public static Iec61850DataSetSignalInventoryMergeResult EnsureMandatorySignals(
        Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Signal Selection is also opened directly from an offline CID/SCD workspace.
        // In that workflow LiveDiscoveryModel is intentionally null; the SCL design model
        // is the authoritative inventory and must not be ignored. Prefer the live model
        // only after a real association/discovery has produced one.
        var authoritativeModel = device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel;
        if (authoritativeModel is null)
            return EmptyResult();

        return EnsureMandatorySignals(device.Signals, authoritativeModel);
    }

    /// <summary>
    /// Merges the engine-authoritative DataSet inventory into an arbitrary signal collection.
    /// This is intentionally shared by live MMS discovery and offline SCL projection so both
    /// paths expose the same mandatory DataSet members in Signal Selection.
    /// </summary>
    public static Iec61850DataSetSignalInventoryMergeResult EnsureMandatorySignals(
        ICollection<SignalDefinition> signals,
        LiveIedModelDiscoveryDocument model)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(model);

        var mandatory = Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(model);
        if (mandatory.Count == 0)
            return EmptyResult();

        var added = new List<SignalDefinition>();
        var enriched = 0;

        foreach (var descriptor in mandatory)
        {
            var inventoryReference = InventoryReference(descriptor);
            if (string.IsNullOrWhiteSpace(inventoryReference))
                continue;

            // The static DataSet membership is the inventory identity. A structured FCDA
            // such as ThdA.phsA can resolve to a scalar runtime leaf such as
            // ThdA.phsA.cVal.mag.f, but that scalar leaf must never be used to claim the
            // membership row. Reusing by runtime-primary identity made diagnostics appear
            // complete while several FAT rows had no distinct static identity to bind.
            //
            // Reuse only a signal that already represents this exact static member in this
            // DataSet. A plain scalar/live signal remains untouched and a dedicated DataSet
            // projection row is created beside it. Runtime monitoring may still deduplicate
            // identical ObjectReference reads later; FAT membership identity is not lost.
            var current = FindExistingMembershipSignal(signals, descriptor, inventoryReference);
            if (current is not null)
            {
                if (ApplyEngineDataSetAuthority(current, descriptor, inventoryReference))
                    enriched++;
                continue;
            }

            var runtimeReference = FirstNonEmpty(
                descriptor.PrimaryValueReference,
                descriptor.DesignReference,
                descriptor.ObservedReference);
            var reference = FirstNonEmpty(runtimeReference, inventoryReference);
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            var signal = CreateSignal(descriptor, reference, inventoryReference);
            signals.Add(signal);
            added.Add(signal);
        }

        return new Iec61850DataSetSignalInventoryMergeResult(added, enriched, mandatory.Count);
    }

    private static SignalDefinition? FindExistingMembershipSignal(
        IEnumerable<SignalDefinition> signals,
        Iec61850SignalDescriptor descriptor,
        string inventoryReference)
    {
        var membership = FirstMembership(descriptor);
        var dataSetReference = membership?.DataSetReference ?? string.Empty;

        // Strongest identity: the presentation row already carries both the exact static
        // member and the exact DataSet. This is safe even when its runtime ObjectReference
        // is shared with another membership.
        var exact = signals.FirstOrDefault(signal =>
            ReferenceEquals(signal.DisplayReference, inventoryReference) &&
            ReferenceEquals(signal.DataSetReference, dataSetReference));
        if (exact is not null)
            return exact;

        // An unclaimed row whose DisplayReference is already the exact static member may
        // be promoted into this membership. Never steal a row already owned by a different
        // DataSet merely because its runtime leaf happens to match.
        var unclaimedDisplay = signals.FirstOrDefault(signal =>
            ReferenceEquals(signal.DisplayReference, inventoryReference) &&
            string.IsNullOrWhiteSpace(signal.DataSetReference));
        if (unclaimedDisplay is not null)
            return unclaimedDisplay;

        // Legacy/live catalogs sometimes leave DisplayReference empty. Reuse their exact
        // ObjectReference only when the static member itself is that same scalar identity.
        // This intentionally does NOT compare against descriptor.PrimaryValueReference when
        // the static member is a structured/intermediate component.
        return signals.FirstOrDefault(signal =>
            ReferenceEquals(signal.ObjectReference, inventoryReference) &&
            (string.IsNullOrWhiteSpace(signal.DisplayReference) ||
             ReferenceEquals(signal.DisplayReference, inventoryReference)) &&
            (string.IsNullOrWhiteSpace(signal.DataSetReference) ||
             ReferenceEquals(signal.DataSetReference, dataSetReference)));
    }

    private static Iec61850DataSetSignalInventoryMergeResult EmptyResult()
        => new(Array.Empty<SignalDefinition>(), 0, 0);

    private static SignalDefinition CreateSignal(
        Iec61850SignalDescriptor descriptor,
        string runtimeReference,
        string inventoryReference)
    {
        var primaryMembership = FirstMembership(descriptor);
        var report = descriptor.ReportMemberships.FirstOrDefault();
        var dataType = FirstNonEmpty(descriptor.MmsType, descriptor.SclBType, "Unknown");
        var unresolved = descriptor.ResolutionStatus == Iec61850SignalCatalogResolutionStatus.Unresolved;
        var staticReference = FirstNonEmpty(inventoryReference, runtimeReference);
        var objectReference = unresolved ? staticReference : FirstNonEmpty(runtimeReference, staticReference);

        return new SignalDefinition
        {
            Name = FirstNonEmpty(descriptor.DataObject, descriptor.DataAttributePath, staticReference),
            ObjectReference = objectReference,
            // Signal Selection binds IEC Telegram to DisplayReference. Preserve the exact
            // static FCDA/FCD member here even when ARIEC resolves a readable primary leaf.
            DisplayReference = staticReference,
            FunctionalConstraint = descriptor.FunctionalConstraint,
            DataType = dataType,
            Category = "DataSet",
            Confidence = unresolved ? "Medium" : "High",
            DataSetReference = primaryMembership?.DataSetReference ?? string.Empty,
            ReportControlReference = report?.ReportControlReference ?? string.Empty,
            QualityReference = descriptor.QualityReference,
            TimestampReference = descriptor.TimestampReference,
            Source = unresolved
                ? "ARIEC61850 signal inventory • mandatory static DataSet member • primary leaf unresolved"
                : "ARIEC61850 signal inventory • mandatory static DataSet member",
            IsSelected = false,
            IsReportCapable = true,
            ReportCoverage = unresolved
                ? report is null
                    ? "Static DataSet member • primary leaf unresolved"
                    : "Static report/DataSet • primary leaf unresolved"
                : report is null
                    ? "Static DataSet member • MMS polling fallback"
                    : "Static report/DataSet • polling fallback",
            ReportCoverageReason = BuildCoverageReason(descriptor),
            ProbeStatus = unresolved ? "DataSet member — primary leaf unresolved" : "Not probed",
            Value = "-",
            Quality = "Unknown",
            DeviceTimestamp = "-"
        };
    }

    private static bool ApplyEngineDataSetAuthority(
        SignalDefinition signal,
        Iec61850SignalDescriptor descriptor,
        string inventoryReference)
    {
        var changed = false;
        var membership = FirstMembership(descriptor);
        var report = descriptor.ReportMemberships.FirstOrDefault();
        var staticReference = FirstNonEmpty(inventoryReference, signal.DisplayReference, signal.ObjectReference);

        // Never replace the user-visible static DataSet member with a guessed/resolved leaf.
        // ObjectReference can remain the engine-resolved runtime leaf for MMS reads; the
        // selector's IEC Telegram column is bound to DisplayReference.
        if (!string.IsNullOrWhiteSpace(staticReference) &&
            !string.Equals(signal.DisplayReference, staticReference, StringComparison.OrdinalIgnoreCase))
        {
            signal.DisplayReference = staticReference;
            changed = true;
        }

        if (membership is not null &&
            !string.Equals(signal.DataSetReference, membership.DataSetReference, StringComparison.OrdinalIgnoreCase))
        {
            signal.DataSetReference = membership.DataSetReference;
            changed = true;
        }

        if (report is not null && string.IsNullOrWhiteSpace(signal.ReportControlReference))
        {
            signal.ReportControlReference = report.ReportControlReference;
            changed = true;
        }

        if (!signal.IsReportCapable)
        {
            signal.IsReportCapable = true;
            changed = true;
        }

        if ((string.IsNullOrWhiteSpace(signal.ReportCoverage) ||
             signal.ReportCoverage.Equals("Polling fallback", StringComparison.OrdinalIgnoreCase)) &&
            (membership is not null || report is not null))
        {
            var unresolved = descriptor.ResolutionStatus == Iec61850SignalCatalogResolutionStatus.Unresolved;
            signal.ReportCoverage = unresolved
                ? report is null
                    ? "Static DataSet member • primary leaf unresolved"
                    : "Static report/DataSet • primary leaf unresolved"
                : report is null
                    ? "Static DataSet member • MMS polling fallback"
                    : "Static report/DataSet • polling fallback";
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

    private static string InventoryReference(Iec61850SignalDescriptor descriptor)
    {
        var membership = FirstMembership(descriptor);
        return FirstNonEmpty(
            membership?.CanonicalMemberReference,
            membership?.OriginalMemberReference,
            descriptor.DesignReference,
            descriptor.ObservedReference,
            descriptor.PrimaryValueReference);
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
        var unresolved = descriptor.ResolutionStatus == Iec61850SignalCatalogResolutionStatus.Unresolved;
        var authorityText = unresolved
            ? "mandatory static DataSet member"
            : "mandatory primary DataSet signal";
        var resolutionText = unresolved
            ? " The original DataSet member is preserved while its unique primary DataAttribute remains unresolved."
            : " The static FCDA identity stays visible even when a readable primary DataAttribute is resolved for runtime acquisition.";

        return $"ARIEC61850 {authorityText}: {membershipText}." +
               resolutionText +
               " Inventory presence is engine-authoritative; user selection remains independent.";
    }

    private static bool ReferenceEquals(string? left, string? right)
        => string.Equals(LiteralReference(left), LiteralReference(right), StringComparison.OrdinalIgnoreCase);

    private static string LiteralReference(string? reference)
        => (reference ?? string.Empty).Trim();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
