using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    /// <summary>
    /// G2.6 Smart Auto recovery for a static report segment that cannot be activated.
    ///
    /// Recovery is deliberately narrower than the original P4 experiment:
    /// - the failed static RCB is excluded from the recovery availability evidence;
    /// - static RCBs are disabled in the recovery planner, so only an alternate dynamic
    ///   BRCB/URCB can be selected;
    /// - a post-mutation static failure may recover only after rollback/cleanup is proven;
    /// - ARIEC capability + exact availability evidence remains authoritative;
    /// - StartHybridReportMonitorAsync performs another fresh discovery/revalidation before
    ///   any dynamic DataSet/RCB write and retains the process-lifetime dynamic-write circuit;
    /// - the original PlanId is preserved so runtime routing/coverage ownership does not fork.
    ///
    /// If any gate is not satisfied, bounded MMS polling remains the final fallback.
    /// </summary>
    private async Task<NativeReportMonitorStartResult> TryStartDynamicRecoveryAfterStaticFailureP4Async(
        ReportControlPlan appPlan,
        AuthoritativeHybridSubscription authoritative,
        ArMms.MmsDiscoveryResult discovery,
        ArMms.MmsRcbAvailabilityResult freshAvailability,
        string staticFailure,
        CancellationToken cancellationToken,
        bool staticCleanupProven = true)
    {
        ArgumentNullException.ThrowIfNull(appPlan);
        ArgumentNullException.ThrowIfNull(authoritative);
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(freshAvailability);
        cancellationToken.ThrowIfCancellationRequested();

        NativeReportMonitorStartResult Fallback(string reason, string detail) => new()
        {
            IsSuccess = false,
            PlanId = appPlan.PlanId,
            Message = $"{staticFailure} Smart Auto dynamic recovery withheld: {detail} Bounded MMS polling remains active for this affected signal set.",
            UsedDynamicDataSet = false,
            DynamicAttempted = false,
            DynamicAttemptState = "Skipped",
            FailureReason = reason,
            PollingFallbackReason = reason,
            Warnings = freshAvailability.Warnings
        };

        if (!IsStaticHybridKind(authoritative.Kind))
            return Fallback("StaticRecoveryNotApplicable", "the failed authoritative segment is not static.");

        if (!staticCleanupProven)
        {
            return Fallback(
                "StaticCleanupUnproven",
                "the failed static activation mutated report state and rollback/cleanup was not proven; a second RCB mutation is forbidden on this association.");
        }

        if (!_session.IsMmsInitiated)
            return Fallback("TransportUnavailable", $"the MMS association is no longer initiated ({_session.State}).");

        if (!authoritative.Options.AllowDynamicBrcb && !authoritative.Options.AllowDynamicUrcb)
            return Fallback("DynamicRecoveryDisabled", "dynamic BRCB/URCB acquisition is disabled by the current Smart Auto policy.");

        if (!string.IsNullOrWhiteSpace(appPlan.RelayId) &&
            DynamicWriteCircuitByDevice.TryGetValue(appPlan.RelayId, out var circuitReason))
        {
            return Fallback(
                "DynamicWriteCircuitOpen",
                $"the device dynamic-write circuit is already open after real field failure evidence ({circuitReason}).");
        }

        // Never turn the RCB that just failed static activation into a dynamic target.
        // Recovery must use a distinct, freshly classified RCB so a bad/busy/static object
        // cannot be immediately mutated under a different acquisition label.
        var alternateSnapshots = freshAvailability.ReportControls
            .Where(snapshot => !SameLiteralReference(snapshot.Reference, authoritative.ReportControlReference))
            .ToArray();
        if (alternateSnapshots.Length == 0)
        {
            return Fallback(
                "NoAlternateRcbEvidence",
                $"no alternate RCB has fresh availability evidence after excluding {authoritative.ReportControlReference}.");
        }

        var alternateAvailability = new ArMms.MmsRcbAvailabilityResult
        {
            CheckedAtUtc = freshAvailability.CheckedAtUtc,
            ReportControls = alternateSnapshots,
            Warnings = freshAvailability.Warnings
        };

        var recoveryOptions = new ArMms.MmsHybridReportAcquisitionOptions
        {
            AllowStaticBrcb = false,
            AllowStaticUrcb = false,
            AllowDynamicBrcb = authoritative.Options.AllowDynamicBrcb,
            AllowDynamicUrcb = authoritative.Options.AllowDynamicUrcb,
            AllowCallerOwnedReports = false,
            AllowPollingFallback = true,
            RequireExactAvailabilityEvidence = true
        };

        var recoveryCapability = ArMms.MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
            authoritative.Catalog,
            authoritative.Signals,
            discovery.ReportInventory,
            alternateAvailability,
            discovery.IedDirectory,
            _session.LastNegotiatedCapabilities,
            recoveryOptions);

        var dynamicSegment = recoveryCapability.AcquisitionPlan.Segments.FirstOrDefault(segment =>
            segment.IsReportBacked &&
            segment.ReportPlan is not null &&
            segment.Kind is ArMms.MmsHybridAcquisitionKind.DynamicBrcb or ArMms.MmsHybridAcquisitionKind.DynamicUrcb);

        if (dynamicSegment?.ReportPlan is null)
        {
            var blocker = recoveryCapability.Blockers.FirstOrDefault();
            var warning = recoveryCapability.Warnings.FirstOrDefault();
            var detail = !string.IsNullOrWhiteSpace(blocker)
                ? blocker
                : !string.IsNullOrWhiteSpace(warning)
                    ? warning
                    : "ARIEC found no exact alternate dynamic report segment for the affected signals.";
            return Fallback("NoDynamicRecoverySegment", detail);
        }

        // Preserve the runtime plan identity while replacing only its acquisition target.
        // Runtime dictionaries, report slice routing and PointPlanIds therefore continue to
        // refer to one plan even though Smart Auto escalated static -> dynamic.
        appPlan.ReportControlReference = dynamicSegment.ReportControlReference;
        appPlan.DataSetReference = dynamicSegment.DataSetReference;
        appPlan.Mode = $"ARIEC Hybrid • {dynamicSegment.Kind} • static recovery";
        appPlan.AllowDynamicDataSetWrites = true;
        appPlan.Buffered = dynamicSegment.Kind == ArMms.MmsHybridAcquisitionKind.DynamicBrcb;
        appPlan.Status = $"{dynamicSegment.Kind} recovery planned";
        appPlan.IsEngineAuthoritative = true;
        appPlan.EngineAcquisitionKind = dynamicSegment.Kind.ToString();

        _authoritativeHybridSubscriptions[appPlan.PlanId] = new AuthoritativeHybridSubscription(
            dynamicSegment.Kind,
            dynamicSegment.ReportControlReference,
            authoritative.Catalog,
            dynamicSegment.Signals.ToArray(),
            recoveryOptions);

        // This recursive entry is safe: the authoritative subscription is now dynamic, so
        // any subsequent failure cannot re-enter static recovery. It also gives the dynamic
        // target a fresh discovery + exact availability revalidation immediately before write.
        return await StartHybridReportMonitorAsync(appPlan, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsStaticHybridKind(ArMms.MmsHybridAcquisitionKind kind)
        => kind is ArMms.MmsHybridAcquisitionKind.StaticBrcb or ArMms.MmsHybridAcquisitionKind.StaticUrcb;

    private static NativeHybridPointAttemptEvidence ProjectAttemptEvidence(
        ArMms.MmsHybridSignalAttemptEvidence evidence,
        IReadOnlyDictionary<Iec61850SignalDescriptor, Iec61850MonitorPoint> descriptorPoints)
        => new()
        {
            PointKey = FindPointKeyForAssignment(evidence.SignalReference, descriptorPoints),
            IecReference = evidence.SignalReference,
            PlannedAcquisitionKind = evidence.PlannedKind.ToString(),
            DynamicAttemptDisposition = evidence.DynamicAttemptDisposition.ToString(),
            PollingFallbackReason = evidence.PollingFallbackReason.ToString(),
            Detail = evidence.Detail
        };

    private static NativeHybridPointAttemptEvidence UnmappedAttemptEvidence(Iec61850MonitorPoint point)
        => SkippedAttemptEvidence(
            point,
            "CatalogMappingUnavailable",
            "No unique literal ARIEC catalog mapping exists for this selected point; no dynamic report write is attempted from a guessed IEC reference.");

    private static NativeHybridPointAttemptEvidence SkippedAttemptEvidence(
        Iec61850MonitorPoint point,
        string reason,
        string detail)
        => new()
        {
            PointKey = point.PointKey,
            IecReference = point.IecReference,
            PlannedAcquisitionKind = "MmsPollingFallback",
            DynamicAttemptDisposition = "Skipped",
            PollingFallbackReason = reason,
            Detail = detail
        };
}
