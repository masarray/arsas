using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    /// <summary>
    /// P4 recovery path used only after an engine-authoritative static report can no longer
    /// be activated from fresh association evidence. Static planning is disabled for this
    /// recovery pass so the same signal must either obtain a dynamic BRCB/URCB attempt or
    /// return an explicit engine reason explaining why polling is the final fallback.
    /// </summary>
    private async Task<NativeReportMonitorStartResult> TryStartDynamicRecoveryAfterStaticFailureP4Async(
        ReportControlPlan appPlan,
        AuthoritativeHybridSubscription authoritative,
        ArMms.MmsDiscoveryResult discovery,
        ArMms.MmsRcbAvailabilityResult freshAvailability,
        string staticFailure,
        CancellationToken cancellationToken)
    {
        // P6: a previous real dynamic write failure is field evidence that this device must
        // not receive another temporary DataSet/RCB write in the current application run.
        // Static reporting remains eligible on every fresh association; only this recovery
        // path is circuit-broken so reconnect cannot become a dynamic-write retry loop.
        if (!string.IsNullOrWhiteSpace(appPlan.RelayId) &&
            DynamicWriteCircuitByDevice.TryGetValue(appPlan.RelayId, out var circuitReason))
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = appPlan.PlanId,
                Message = $"{staticFailure} P6 dynamic recovery is circuit-broken after a previous real activation failure ({circuitReason}); MMS polling is the bounded fallback for this residual signal set.",
                UsedDynamicDataSet = true,
                DynamicAttempted = false,
                DynamicAttemptState = "Skipped",
                FailureReason = "DynamicWriteCircuitOpen",
                PollingFallbackReason = "DynamicWriteCircuitOpen"
            };
        }

        if (!authoritative.Options.AllowDynamicBrcb && !authoritative.Options.AllowDynamicUrcb)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = appPlan.PlanId,
                Message = $"{staticFailure} Dynamic recovery is disabled by policy; MMS polling is the final fallback.",
                DynamicAttemptState = "Skipped",
                FailureReason = "StaticActivationFailed",
                PollingFallbackReason = ArMms.MmsHybridPollingFallbackReason.DynamicDisabledByPolicy.ToString()
            };
        }

        var dynamicOnlyOptions = new ArMms.MmsHybridReportAcquisitionOptions
        {
            AllowStaticBrcb = false,
            AllowStaticUrcb = false,
            AllowDynamicBrcb = authoritative.Options.AllowDynamicBrcb,
            AllowDynamicUrcb = authoritative.Options.AllowDynamicUrcb,
            AllowCallerOwnedReports = false,
            AllowPollingFallback = true,
            RequireExactAvailabilityEvidence = authoritative.Options.RequireExactAvailabilityEvidence,
            MaxDynamicMembersPerReport = authoritative.Options.MaxDynamicMembersPerReport,
            MaxDynamicReportPlans = authoritative.Options.MaxDynamicReportPlans
        };

        var recovery = ArMms.MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
            authoritative.Catalog,
            authoritative.Signals,
            discovery.ReportInventory,
            freshAvailability,
            discovery.IedDirectory,
            _session.LastNegotiatedCapabilities,
            dynamicOnlyOptions);
        var attemptEvidence = ArMms.MmsHybridDynamicAttemptEvidenceBuilder.Build(recovery, dynamicOnlyOptions);
        var dynamicSegment = recovery.AcquisitionPlan.Segments.FirstOrDefault(segment =>
            segment.ReportPlan is not null &&
            segment.Kind is ArMms.MmsHybridAcquisitionKind.DynamicBrcb or ArMms.MmsHybridAcquisitionKind.DynamicUrcb);

        if (dynamicSegment?.ReportPlan is null)
        {
            var fallbackReasons = attemptEvidence
                .Where(item => item.DynamicAttemptDisposition == ArMms.MmsHybridDynamicAttemptDisposition.Skipped)
                .Select(item => item.PollingFallbackReason.ToString())
                .Where(reason => !reason.Equals("None", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var reason = fallbackReasons.Length == 0
                ? ArMms.MmsHybridPollingFallbackReason.DynamicPlanUnavailableAfterCapabilityQualification.ToString()
                : string.Join(",", fallbackReasons);

            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = appPlan.PlanId,
                Message = $"{staticFailure} ARIEC P4 dynamic recovery produced no ready dynamic report segment; MMS polling is now the final fallback. reason={reason}.",
                DynamicAttempted = false,
                DynamicAttemptState = "Skipped",
                FailureReason = "StaticActivationFailed",
                PollingFallbackReason = reason,
                Warnings = recovery.Warnings
                    .Concat(recovery.Blockers)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        var subscription = dynamicSegment.ReportPlan;
        var coveredReferences = ExtractSubscriptionMemberReferences(subscription.Members);
        var attempt = await RunMmsOperationAsync(
            () => _session.StartPersistentReportMonitorWithAttemptEvidenceAsync(
                subscription,
                triggerGeneralInterrogation: true,
                deleteDynamicDataSetOnStop: true,
                discovery.IedDirectory,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var start = attempt.StartResult;
        var warnings = start.Warnings
            .Concat(subscription.Warnings)
            .Concat(attempt.CleanupWarnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!attempt.IsSuccess || start.Session is null)
        {
            if (attempt.DynamicAttempted && !string.IsNullOrWhiteSpace(appPlan.RelayId))
            {
                var failure = attempt.FailureReason.ToString();
                if (string.IsNullOrWhiteSpace(failure) || failure.Equals("None", StringComparison.OrdinalIgnoreCase))
                    failure = "DynamicRecoveryActivationFailed";
                DynamicWriteCircuitByDevice[appPlan.RelayId] = failure;
            }

            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = appPlan.PlanId,
                Message = $"{staticFailure} Dynamic recovery was {(attempt.DynamicAttempted ? "attempted" : "not attempted")} and failed: {start.Message}",
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                WriteStepCount = start.WriteSteps.Count,
                UsedDynamicDataSet = true,
                DynamicAttempted = attempt.DynamicAttempted,
                DynamicAttemptState = attempt.DynamicAttemptState.ToString(),
                FailureReason = attempt.FailureReason.ToString(),
                PollingFallbackReason = attempt.DynamicAttempted ? "DynamicActivationFailed" : "DynamicActivationNotAttempted",
                CleanupAttempted = attempt.CleanupAttempted,
                CleanupSucceeded = attempt.CleanupSucceeded,
                CoveredReferences = coveredReferences,
                Warnings = warnings
            };
        }

        appPlan.EngineAcquisitionKind = dynamicSegment.Kind.ToString();
        appPlan.AllowDynamicDataSetWrites = true;
        appPlan.Buffered = dynamicSegment.Kind == ArMms.MmsHybridAcquisitionKind.DynamicBrcb;
        appPlan.Mode = $"ARIEC Hybrid • {dynamicSegment.Kind} • P4 static recovery";
        appPlan.Status = $"{dynamicSegment.Kind} active after static recovery";
        if (!string.IsNullOrWhiteSpace(start.Session.ReportControl.Reference))
            appPlan.ReportControlReference = start.Session.ReportControl.Reference;
        if (!string.IsNullOrWhiteSpace(start.Session.Plan.DataSetReference))
            appPlan.DataSetReference = start.Session.Plan.DataSetReference;

        _reportMonitorSessions[appPlan.PlanId] = start.Session;
        _reportMonitorCoverage[appPlan.PlanId] = coveredReferences;

        return new NativeReportMonitorStartResult
        {
            IsSuccess = true,
            PlanId = appPlan.PlanId,
            Message = $"ARIEC P4 recovered failed static acquisition with {dynamicSegment.Kind}. {start.Message}",
            SubscriptionSummary = subscription.Summary,
            MemberCount = subscription.Members.Count,
            WriteStepCount = start.WriteSteps.Count,
            UsedDynamicDataSet = true,
            DynamicAttempted = true,
            DynamicAttemptState = attempt.DynamicAttemptState.ToString(),
            FailureReason = string.Empty,
            ReportControlReference = appPlan.ReportControlReference,
            DataSetReference = appPlan.DataSetReference,
            AcquisitionLabel = $"ARIEC Hybrid: {dynamicSegment.Kind} (P4 recovery)",
            CoveredReferences = coveredReferences,
            Warnings = warnings
        };
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
