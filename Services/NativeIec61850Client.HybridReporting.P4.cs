using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    /// <summary>
    /// P6.1 baseline-safety compatibility hook.
    ///
    /// P4 originally converted a failed static activation into a brand-new dynamic
    /// DataSet/RCB write attempt. That changed the proven pre-P0 failure semantics and made
    /// one static problem capable of mutating another RCB or destabilizing the association.
    /// Static failure is now isolated again: no dynamic DataSet is created, no alternate RCB
    /// is written, and bounded MMS polling remains the fallback for the affected signal set.
    ///
    /// The method name is retained temporarily so existing call-sites stay source-compatible;
    /// its behavior is deliberately fail-closed and side-effect free.
    /// </summary>
    private Task<NativeReportMonitorStartResult> TryStartDynamicRecoveryAfterStaticFailureP4Async(
        ReportControlPlan appPlan,
        AuthoritativeHybridSubscription authoritative,
        ArMms.MmsDiscoveryResult discovery,
        ArMms.MmsRcbAvailabilityResult freshAvailability,
        string staticFailure,
        CancellationToken cancellationToken)
    {
        _ = authoritative;
        _ = discovery;
        _ = freshAvailability;
        _ = cancellationToken;

        return Task.FromResult(new NativeReportMonitorStartResult
        {
            IsSuccess = false,
            PlanId = appPlan.PlanId,
            Message = $"{staticFailure} P6.1 preserved baseline static-failure isolation: no dynamic DataSet/RCB write was attempted; bounded MMS polling remains active for this affected signal set.",
            UsedDynamicDataSet = false,
            DynamicAttempted = false,
            DynamicAttemptState = "Skipped",
            FailureReason = "StaticActivationFailed",
            PollingFallbackReason = "StaticActivationFailed"
        });
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
