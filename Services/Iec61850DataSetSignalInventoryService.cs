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

        // When an SCL workspace is open, its configured DataSet membership remains the
        // engineering authority across cached/fast reconnects. A partial live discovery
        // model must not erase FCDA/FCD rows that were proven by the opened CID/SCD. For an
        // online-only IED with no SCL workspace, live discovery remains the authority.
        var authoritativeModel = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel;
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
            // Scalar members remain backward compatible: when the static member itself is
            // the resolved primary leaf, an existing exact IEC or engine-provided MMS-form
            // signal may be enriched instead of duplicated.
            var runtimeBinding = ResolveRuntimeBinding(model, descriptor, inventoryReference);

            var current = FindExistingMembershipSignal(signals, descriptor, inventoryReference);
            if (current is not null)
            {
                if (ApplyEngineDataSetAuthority(current, descriptor, inventoryReference, runtimeBinding))
                    enriched++;
                continue;
            }

            var reference = FirstNonEmpty(runtimeBinding.Reference, inventoryReference);
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            var signal = CreateSignal(descriptor, runtimeBinding, inventoryReference);
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
        var materialized = signals.ToArray();

        // Strongest identity: the presentation row already carries both the exact static
        // member and the exact DataSet. This is safe even when its runtime ObjectReference
        // is shared with another membership.
        var exact = materialized.FirstOrDefault(signal =>
            ReferenceEquals(signal.DisplayReference, inventoryReference) &&
            ReferenceEquals(signal.DataSetReference, dataSetReference));
        if (exact is not null)
            return exact;

        // An unclaimed row whose DisplayReference is already the exact static member may
        // be promoted into this membership. Never steal a row already owned by a different
        // DataSet merely because its runtime leaf happens to match.
        var unclaimedDisplay = materialized.FirstOrDefault(signal =>
            ReferenceEquals(signal.DisplayReference, inventoryReference) &&
            string.IsNullOrWhiteSpace(signal.DataSetReference));
        if (unclaimedDisplay is not null)
            return unclaimedDisplay;

        // Legacy/live catalogs sometimes leave DisplayReference empty. Reuse their exact
        // ObjectReference only when the static member itself is that same scalar identity.
        var exactStaticObject = materialized.FirstOrDefault(signal =>
            ReferenceEquals(signal.ObjectReference, inventoryReference) &&
            (string.IsNullOrWhiteSpace(signal.DisplayReference) ||
             ReferenceEquals(signal.DisplayReference, inventoryReference)) &&
            (string.IsNullOrWhiteSpace(signal.DataSetReference) ||
             ReferenceEquals(signal.DataSetReference, dataSetReference)));
        if (exactStaticObject is not null)
            return exactStaticObject;

        // Preserve the long-standing literal MMS-reference compatibility only for a true
        // scalar member: the static membership and resolved primary IEC reference must be
        // identical. Structured/intermediate members deliberately fail this test, so a
        // shared/container MMS alias cannot absorb ThdA.phsA/phsB/phsC-style memberships.
        var staticIsResolvedScalar =
            !string.IsNullOrWhiteSpace(descriptor.PrimaryValueReference) &&
            ReferenceEquals(descriptor.PrimaryValueReference, inventoryReference);
        if (!staticIsResolvedScalar || string.IsNullOrWhiteSpace(descriptor.PrimaryValueMmsReference))
            return null;

        return materialized.FirstOrDefault(signal =>
            ReferenceEquals(signal.ObjectReference, descriptor.PrimaryValueMmsReference) &&
            (string.IsNullOrWhiteSpace(signal.DataSetReference) ||
             ReferenceEquals(signal.DataSetReference, dataSetReference)));
    }

    private static Iec61850DataSetSignalInventoryMergeResult EmptyResult()
        => new(Array.Empty<SignalDefinition>(), 0, 0);

    private sealed record RuntimeBinding(
        string Reference,
        string DataType,
        bool ResolvedFromExactSchema);

    private static SignalDefinition CreateSignal(
        Iec61850SignalDescriptor descriptor,
        RuntimeBinding runtimeBinding,
        string inventoryReference)
    {
        var primaryMembership = FirstMembership(descriptor);
        var report = descriptor.ReportMemberships.FirstOrDefault();
        var descriptorUnresolved = descriptor.ResolutionStatus == Iec61850SignalCatalogResolutionStatus.Unresolved;
        var runtimeResolved = !string.IsNullOrWhiteSpace(runtimeBinding.Reference) &&
                              (!descriptorUnresolved || runtimeBinding.ResolvedFromExactSchema);
        var staticReference = FirstNonEmpty(inventoryReference, runtimeBinding.Reference);
        var objectReference = runtimeResolved
            ? runtimeBinding.Reference
            : staticReference;
        var dataType = FirstNonEmpty(runtimeBinding.DataType, descriptor.MmsType, descriptor.SclBType, "Unknown");

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
            Confidence = runtimeResolved ? "High" : "Medium",
            DataSetReference = primaryMembership?.DataSetReference ?? string.Empty,
            ReportControlReference = report?.ReportControlReference ?? string.Empty,
            QualityReference = descriptor.QualityReference,
            TimestampReference = descriptor.TimestampReference,
            Source = runtimeBinding.ResolvedFromExactSchema
                ? "ARIEC61850 signal inventory • mandatory static DataSet member • exact schema primary leaf"
                : runtimeResolved
                    ? "ARIEC61850 signal inventory • mandatory static DataSet member"
                    : "ARIEC61850 signal inventory • mandatory static DataSet member • primary leaf unresolved",
            IsSelected = false,
            IsReportCapable = true,
            ReportCoverage = runtimeResolved
                ? report is null
                    ? "Static DataSet member • exact runtime leaf"
                    : "Static report/DataSet • exact runtime leaf"
                : report is null
                    ? "Static DataSet member • primary leaf unresolved"
                    : "Static report/DataSet • primary leaf unresolved",
            ReportCoverageReason = BuildCoverageReason(descriptor),
            ProbeStatus = runtimeBinding.ResolvedFromExactSchema
                ? "DataSet member — exact schema primary leaf"
                : runtimeResolved ? "Not probed" : "DataSet member — primary leaf unresolved",
            Value = "-",
            Quality = "Unknown",
            DeviceTimestamp = "-"
        };
    }

    private static bool ApplyEngineDataSetAuthority(
        SignalDefinition signal,
        Iec61850SignalDescriptor descriptor,
        string inventoryReference,
        RuntimeBinding runtimeBinding)
    {
        var changed = false;
        var membership = FirstMembership(descriptor);
        var report = descriptor.ReportMemberships.FirstOrDefault();
        var staticReference = FirstNonEmpty(inventoryReference, signal.DisplayReference, signal.ObjectReference);

        // A row created from a previous unresolved discovery may be safely upgraded only
        // when the current authoritative schema resolves one exact named primary leaf.
        // The static membership identity remains DisplayReference; ObjectReference is the
        // runtime leaf used for report matching/presentation.
        if (runtimeBinding.ResolvedFromExactSchema &&
            !string.IsNullOrWhiteSpace(runtimeBinding.Reference) &&
            (string.Equals(signal.Category, "DataSet", StringComparison.OrdinalIgnoreCase) ||
             (signal.Source ?? string.Empty).Contains("mandatory static DataSet member", StringComparison.OrdinalIgnoreCase)))
        {
            if (!ReferenceEquals(signal.ObjectReference, runtimeBinding.Reference))
            {
                signal.ObjectReference = runtimeBinding.Reference;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(runtimeBinding.DataType) &&
                !string.Equals(signal.DataType, runtimeBinding.DataType, StringComparison.OrdinalIgnoreCase))
            {
                signal.DataType = runtimeBinding.DataType;
                changed = true;
            }

            signal.Confidence = "High";
            signal.Source = "ARIEC61850 signal inventory • mandatory static DataSet member • exact schema primary leaf";
            signal.ProbeStatus = "DataSet member — exact schema primary leaf";
        }

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

    private static RuntimeBinding ResolveRuntimeBinding(
        LiveIedModelDiscoveryDocument model,
        Iec61850SignalDescriptor descriptor,
        string inventoryReference)
    {
        var descriptorReference = FirstNonEmpty(
            descriptor.PrimaryValueReference,
            descriptor.DesignReference,
            descriptor.ObservedReference);

        var descriptorType = FirstNonEmpty(
            FindExactAttributeDataType(model, descriptorReference),
            descriptor.SclBType,
            descriptor.MmsType);

        if (descriptor.ResolutionStatus != Iec61850SignalCatalogResolutionStatus.Unresolved &&
            !string.IsNullOrWhiteSpace(descriptorReference))
        {
            return new RuntimeBinding(descriptorReference, descriptorType, false);
        }

        if (SchemaSafeAggregateProjectionService.TryResolveStaticDataSetPrimaryLeaf(
                model,
                inventoryReference,
                descriptor.FunctionalConstraint,
                out var leaf,
                out _))
        {
            var schemaType = FirstNonEmpty(
                FindExactAttributeDataType(model, leaf.Reference),
                leaf.DataType,
                descriptorType);
            return new RuntimeBinding(leaf.Reference, schemaType, true);
        }

        return new RuntimeBinding(descriptorReference, descriptorType, false);
    }

    private static string FindExactAttributeDataType(
        LiveIedModelDiscoveryDocument model,
        string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var normalized = LiteralReference(reference).Replace('
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
, '.');
        var matches = model.LogicalDevices
            .SelectMany(device => device.LogicalNodes)
            .SelectMany(node => node.DataObjects)
            .SelectMany(dataObject => dataObject.Attributes)
            .Where(attribute => ReferenceEquals(
                (attribute.ObjectReference ?? string.Empty).Replace('
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
, '.'),
                normalized))
            .ToArray();

        if (matches.Length != 1)
            return string.Empty;

        return FirstNonEmpty(matches[0].SclBType, matches[0].MmsType);
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
