using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Collects application-side physical evidence after ARIEC has produced an authoritative
/// hybrid report plan. The tracker records only observations from actual start/slice
/// results. A lack of report traffic is intentionally not converted into signal absence.
/// </summary>
internal sealed class HybridReportPhysicalValidationTracker
{
    private sealed class PlanState
    {
        public required ReportControlPlan Plan { get; init; }
        public bool ActivationAttempted { get; set; }
        public bool ActivationSucceeded { get; set; }
        public string ActivationMessage { get; set; } = "Not attempted";
        public int ReportFrameCount { get; set; }
        public int ReportUpdateCount { get; set; }
        public HashSet<string> ChangeVerifiedPointKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTimeOffset? FirstReportAtUtc { get; set; }
        public DateTimeOffset? LastReportAtUtc { get; set; }
    }

    private readonly Dictionary<string, PlanState> _plans = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _warnings = new();
    private NativeHybridReportPlanningResult? _planning;

    public void Reset(NativeHybridReportPlanningResult? planning)
    {
        _planning = planning;
        _plans.Clear();
        _warnings.Clear();

        if (planning is null)
            return;

        foreach (var warning in planning.Warnings)
            AddWarning(warning);

        foreach (var plan in planning.ReportPlans)
            _plans[plan.PlanId] = new PlanState { Plan = plan };
    }

    public void RecordActivation(ReportControlPlan plan, NativeReportMonitorStartResult result)
    {
        if (!plan.IsEngineAuthoritative)
            return;

        if (!_plans.TryGetValue(plan.PlanId, out var state))
        {
            state = new PlanState { Plan = plan };
            _plans[plan.PlanId] = state;
        }

        state.ActivationAttempted = true;
        state.ActivationSucceeded = result.IsSuccess;
        state.ActivationMessage = result.Message;
        foreach (var warning in result.Warnings)
            AddWarning(warning);
    }

    public void RecordSlice(
        ReportControlPlan plan,
        NativeReportMonitorSliceResult slice,
        IEnumerable<string>? changeVerifiedPointKeys = null)
    {
        if (!plan.IsEngineAuthoritative || !_plans.TryGetValue(plan.PlanId, out var state))
            return;

        state.ReportFrameCount += slice.ReportFrames.Count;
        state.ReportUpdateCount += slice.Updates.Count;

        var observedTimes = slice.ReportFrames
            .Select(frame => frame.ReceivedAt)
            .Where(value => value != default)
            .OrderBy(value => value)
            .ToArray();
        if (observedTimes.Length > 0)
        {
            state.FirstReportAtUtc ??= observedTimes[0];
            state.LastReportAtUtc = observedTimes[^1];
        }
        else if (slice.ReportFrames.Count > 0 || slice.Updates.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            state.FirstReportAtUtc ??= now;
            state.LastReportAtUtc = now;
        }

        if (changeVerifiedPointKeys is not null)
        {
            foreach (var key in changeVerifiedPointKeys.Where(key => !string.IsNullOrWhiteSpace(key)))
                state.ChangeVerifiedPointKeys.Add(key);
        }

        foreach (var warning in slice.Warnings)
            AddWarning(warning);
    }

    public HybridReportPhysicalValidationSnapshot Capture(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var planning = _planning;
        var plans = _plans.Values
            .OrderBy(state => state.Plan.EngineAcquisitionKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(state => state.Plan.ReportControlReference, StringComparer.OrdinalIgnoreCase)
            .Select(state => new HybridReportPhysicalValidationPlan
            {
                PlanId = state.Plan.PlanId,
                AcquisitionKind = state.Plan.EngineAcquisitionKind,
                ReportControlReference = state.Plan.ReportControlReference,
                DataSetReference = state.Plan.DataSetReference,
                PlannedSignalCount = state.Plan.Bindings.Count,
                ActivationSucceeded = state.ActivationSucceeded,
                ActivationMessage = state.ActivationMessage,
                ReportFrameCount = state.ReportFrameCount,
                ReportUpdateCount = state.ReportUpdateCount,
                ChangeVerifiedPointCount = state.ChangeVerifiedPointKeys.Count,
                FirstReportAtUtc = state.FirstReportAtUtc,
                LastReportAtUtc = state.LastReportAtUtc
            })
            .ToArray();

        return new HybridReportPhysicalValidationSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            DeviceId = device.DeviceId,
            DeviceName = device.Name,
            Endpoint = device.EndpointText,
            PlannedStaticBrcbCount = planning?.StaticBrcbSignalCount ?? 0,
            PlannedStaticUrcbCount = planning?.StaticUrcbSignalCount ?? 0,
            PlannedDynamicBrcbCount = planning?.DynamicBrcbSignalCount ?? 0,
            PlannedDynamicUrcbCount = planning?.DynamicUrcbSignalCount ?? 0,
            ActivatedReportPlanCount = _plans.Values.Count(state => state.ActivationAttempted && state.ActivationSucceeded),
            FailedActivationCount = _plans.Values.Count(state => state.ActivationAttempted && !state.ActivationSucceeded),
            ReportFrameCount = plans.Sum(plan => plan.ReportFrameCount),
            ReportUpdateCount = plans.Sum(plan => plan.ReportUpdateCount),
            ChangeVerifiedPointCount = plans.Sum(plan => plan.ChangeVerifiedPointCount),
            PollingFallbackPointCount = planning?.PollingFallbackSignalCount ?? 0,
            UncoveredPointCount = planning?.UncoveredSignalCount ?? 0,
            Plans = plans,
            Warnings = _warnings.ToArray()
        };
    }

    private void AddWarning(string? warning)
    {
        var text = (warning ?? string.Empty).Trim();
        if (text.Length == 0 || _warnings.Contains(text, StringComparer.OrdinalIgnoreCase))
            return;
        _warnings.Add(text);
    }
}
