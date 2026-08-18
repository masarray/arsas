namespace ArIED61850Tester.Models;

/// <summary>
/// ARSAS projection of an ARIEC-owned hybrid report acquisition plan.
/// The application never infers report capability from these counters; they only expose
/// the typed decision already returned by the engine planner.
/// </summary>
public sealed class NativeHybridReportPlanningResult
{
    public bool IsAuthoritative { get; init; }
    public string Authority { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<ReportControlPlan> ReportPlans { get; init; } = Array.Empty<ReportControlPlan>();
    public IReadOnlyList<string> PollingPointKeys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UncoveredPointKeys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UnmappedPointKeys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<NativeHybridPointAttemptEvidence> PointAttemptEvidence { get; init; } = Array.Empty<NativeHybridPointAttemptEvidence>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public int RequestedPointCount { get; init; }
    public int CatalogMappedPointCount { get; init; }
    public int StaticBrcbSignalCount { get; init; }
    public int StaticUrcbSignalCount { get; init; }
    public int DynamicBrcbSignalCount { get; init; }
    public int DynamicUrcbSignalCount { get; init; }
    public int PollingFallbackSignalCount { get; init; }
    public int UncoveredSignalCount { get; init; }

    public bool HasReportPlans => ReportPlans.Count > 0;
}

/// <summary>
/// Application projection of ARIEC P4 evidence. Planned means the runtime still owes a
/// real dynamic activation attempt. Skipped means final polling is explainable without a
/// write attempt because the engine supplied a concrete reason.
/// </summary>
public sealed class NativeHybridPointAttemptEvidence
{
    public string PointKey { get; init; } = string.Empty;
    public string IecReference { get; init; } = string.Empty;
    public string PlannedAcquisitionKind { get; init; } = string.Empty;
    public string DynamicAttemptDisposition { get; init; } = string.Empty;
    public string PollingFallbackReason { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    public bool DynamicAttemptRequired => DynamicAttemptDisposition.Equals("Planned", StringComparison.OrdinalIgnoreCase);
    public bool IsExplicitDynamicSkip => DynamicAttemptDisposition.Equals("Skipped", StringComparison.OrdinalIgnoreCase) &&
                                         !string.IsNullOrWhiteSpace(PollingFallbackReason) &&
                                         !PollingFallbackReason.Equals("None", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Physical-validation evidence collected by ARSAS after executing an engine-authoritative
/// report plan against a real IED. Silence is never interpreted as signal absence.
/// </summary>
public sealed class HybridReportPhysicalValidationSnapshot
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string DeviceId { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public int PlannedStaticBrcbCount { get; init; }
    public int PlannedStaticUrcbCount { get; init; }
    public int PlannedDynamicBrcbCount { get; init; }
    public int PlannedDynamicUrcbCount { get; init; }
    public int ActivatedReportPlanCount { get; init; }
    public int FailedActivationCount { get; init; }
    public int DynamicAttemptedCount { get; init; }
    public int DynamicAttemptFailedCount { get; init; }
    public int DynamicSkippedPointCount { get; init; }
    public int ReportFrameCount { get; init; }
    public int ReportUpdateCount { get; init; }
    public int ChangeVerifiedPointCount { get; init; }
    public int PollingFallbackPointCount { get; init; }
    public int UncoveredPointCount { get; init; }
    public IReadOnlyList<HybridReportPhysicalValidationPlan> Plans { get; init; } = Array.Empty<HybridReportPhysicalValidationPlan>();
    public IReadOnlyList<NativeHybridPointAttemptEvidence> PointAttemptEvidence { get; init; } = Array.Empty<NativeHybridPointAttemptEvidence>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool HasPhysicalReportEvidence => ReportFrameCount > 0 || ReportUpdateCount > 0;
}

public sealed class HybridReportPhysicalValidationPlan
{
    public string PlanId { get; init; } = string.Empty;
    public string AcquisitionKind { get; init; } = string.Empty;
    public string ReportControlReference { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public int PlannedSignalCount { get; init; }
    public bool ActivationSucceeded { get; init; }
    public string ActivationMessage { get; init; } = string.Empty;
    public string SubscriptionSummary { get; init; } = string.Empty;
    public int MemberCount { get; init; }
    public int SetupWriteStepCount { get; init; }
    public bool UsedDynamicDataSet { get; init; }
    public bool DynamicAttempted { get; init; }
    public string DynamicAttemptState { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public string PollingFallbackReason { get; init; } = string.Empty;
    public bool CleanupAttempted { get; init; }
    public bool CleanupSucceeded { get; init; } = true;
    public int ReportFrameCount { get; init; }
    public int ReportUpdateCount { get; init; }
    public int ChangeVerifiedPointCount { get; init; }
    public DateTimeOffset? FirstReportAtUtc { get; init; }
    public DateTimeOffset? LastReportAtUtc { get; init; }
}
