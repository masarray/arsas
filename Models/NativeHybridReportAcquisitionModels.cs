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
    public int ReportFrameCount { get; init; }
    public int ReportUpdateCount { get; init; }
    public int ChangeVerifiedPointCount { get; init; }
    public int PollingFallbackPointCount { get; init; }
    public int UncoveredPointCount { get; init; }
    public IReadOnlyList<HybridReportPhysicalValidationPlan> Plans { get; init; } = Array.Empty<HybridReportPhysicalValidationPlan>();
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
    public int ReportFrameCount { get; init; }
    public int ReportUpdateCount { get; init; }
    public int ChangeVerifiedPointCount { get; init; }
    public DateTimeOffset? FirstReportAtUtc { get; init; }
    public DateTimeOffset? LastReportAtUtc { get; init; }
}
