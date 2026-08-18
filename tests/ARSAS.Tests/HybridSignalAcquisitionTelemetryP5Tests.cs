using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class HybridSignalAcquisitionTelemetryP5Tests
{
    [Fact]
    public void StaticActivation_IsVisibleAsFinalStaticReportPerSignal()
    {
        var point = Point("static", "IEDLD0/XCBR1.Pos.stVal");
        var plan = Plan("p-static", "StaticBrcb", point);
        var tracker = Tracker(plan, Evidence(point, "StaticBrcb", "NotRequired", "None"));

        tracker.RecordActivation(plan, new NativeReportMonitorStartResult
        {
            IsSuccess = true,
            PlanId = plan.PlanId,
            Message = "Static BRCB active"
        });

        var telemetry = Assert.Single(tracker.Capture(Device()).SignalTelemetry);
        Assert.Equal(HybridSignalAcquisitionState.StaticReport, telemetry.State);
        Assert.Equal("STATIC REPORT", telemetry.StateLabel);
        Assert.True(telemetry.IsReportBacked);
        Assert.False(telemetry.IsFinalPollingFallback);
        Assert.Equal("Report active", telemetry.ExactReason);
    }

    [Fact]
    public void DynamicActivation_IsVisibleAsFinalDynamicReportPerSignal()
    {
        var point = Point("dynamic", "IEDLD0/PTOC1.Op.general");
        var plan = Plan("p-dynamic", "DynamicUrcb", point);
        var tracker = Tracker(plan, Evidence(point, "DynamicUrcb", "Planned", "None"));

        tracker.RecordActivation(plan, new NativeReportMonitorStartResult
        {
            IsSuccess = true,
            PlanId = plan.PlanId,
            Message = "Dynamic URCB active",
            UsedDynamicDataSet = true,
            DynamicAttempted = true,
            DynamicAttemptState = "AttemptedSucceeded"
        });

        var snapshot = tracker.Capture(Device());
        var telemetry = Assert.Single(snapshot.SignalTelemetry);
        Assert.Equal(HybridSignalAcquisitionState.DynamicReport, telemetry.State);
        Assert.Equal("DYNAMIC REPORT", telemetry.StateLabel);
        Assert.True(telemetry.DynamicAttempted);
        Assert.Equal(1, snapshot.DynamicReportSignalCount);
        Assert.Equal(0, snapshot.FinalPollingSignalCount);
    }

    [Fact]
    public void FailedDynamicAttempt_BecomesExplicitDynamicFailedPollingWithCleanupEvidence()
    {
        var point = Point("dynamic-failed", "IEDLD0/GGIO1.Ind1.stVal");
        var plan = Plan("p-dynamic-failed", "DynamicBrcb", point);
        var tracker = Tracker(plan, Evidence(point, "DynamicBrcb", "Planned", "None"));

        tracker.RecordActivation(plan, new NativeReportMonitorStartResult
        {
            IsSuccess = false,
            PlanId = plan.PlanId,
            Message = "RptEna write failed after temporary DataSet creation",
            UsedDynamicDataSet = true,
            DynamicAttempted = true,
            DynamicAttemptState = "AttemptedFailed",
            FailureReason = "ReportEnableFailed",
            PollingFallbackReason = "DynamicActivationFailed",
            CleanupAttempted = true,
            CleanupSucceeded = true
        });

        var snapshot = tracker.Capture(Device());
        var telemetry = Assert.Single(snapshot.SignalTelemetry);
        Assert.Equal(HybridSignalAcquisitionState.DynamicFailedPolling, telemetry.State);
        Assert.Equal("DYNAMIC FAILED → POLLING", telemetry.StateLabel);
        Assert.True(telemetry.IsFinalPollingFallback);
        Assert.Equal("ReportEnableFailed • DynamicActivationFailed", telemetry.ExactReason);
        Assert.Equal("Rollback OK", telemetry.CleanupLabel);
        Assert.Equal(1, snapshot.DynamicFailedPollingSignalCount);
        Assert.Equal(1, snapshot.FinalPollingSignalCount);
    }

    [Fact]
    public void ExplicitEngineSkip_IsVisibleAsFinalPollingWithExactReason()
    {
        var point = Point("polling", "IEDLD0/MMXU1.A.phsA.cVal.mag.f");
        var evidence = Evidence(
            point,
            "MmsPollingFallback",
            "Skipped",
            "DefineNamedVariableListUnsupported");
        var tracker = new HybridReportPhysicalValidationTracker();
        tracker.Reset(new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            PollingPointKeys = [point.PointKey],
            PollingFallbackSignalCount = 1,
            PointAttemptEvidence = [evidence]
        });

        var snapshot = tracker.Capture(Device());
        var telemetry = Assert.Single(snapshot.SignalTelemetry);
        Assert.Equal(HybridSignalAcquisitionState.PollingFallback, telemetry.State);
        Assert.Equal("POLLING", telemetry.StateLabel);
        Assert.False(telemetry.DynamicAttempted);
        Assert.Equal("DefineNamedVariableListUnsupported", telemetry.ExactReason);
        Assert.Equal(1, snapshot.FinalPollingSignalCount);
    }

    [Fact]
    public void DiagnosticsPanel_ShowsFinalStateExactReasonAttemptAndCleanupPerSignal()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.HybridAcquisitionDiagnostics.cs"));

        Assert.Contains("IEC 61850 signal acquisition evidence", source, StringComparison.Ordinal);
        Assert.Contains("CaptureHybridReportPhysicalValidation", source, StringComparison.Ordinal);
        Assert.Contains("HybridAcquisitionTelemetry", source, StringComparison.Ordinal);
        Assert.Contains("Final state", source, StringComparison.Ordinal);
        Assert.Contains("Dynamic attempt", source, StringComparison.Ordinal);
        Assert.Contains("Exact reason", source, StringComparison.Ordinal);
        Assert.Contains("Cleanup", source, StringComparison.Ordinal);
        Assert.Contains("Dynamic failed→polling", source, StringComparison.Ordinal);
    }

    private static HybridReportPhysicalValidationTracker Tracker(
        ReportControlPlan plan,
        NativeHybridPointAttemptEvidence evidence)
    {
        var tracker = new HybridReportPhysicalValidationTracker();
        tracker.Reset(new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            ReportPlans = [plan],
            PointAttemptEvidence = [evidence]
        });
        return tracker;
    }

    private static NativeHybridPointAttemptEvidence Evidence(
        Iec61850MonitorPoint point,
        string kind,
        string disposition,
        string fallbackReason)
        => new()
        {
            PointKey = point.PointKey,
            IecReference = point.IecReference,
            PlannedAcquisitionKind = kind,
            DynamicAttemptDisposition = disposition,
            PollingFallbackReason = fallbackReason,
            Detail = $"P5 fixture: {kind}/{disposition}/{fallbackReason}"
        };

    private static ReportControlPlan Plan(string id, string kind, Iec61850MonitorPoint point)
        => new()
        {
            PlanId = id,
            IsEngineAuthoritative = true,
            EngineAcquisitionKind = kind,
            ReportControlReference = $"IEDLD0/LLN0.{(kind.Contains("Brcb", StringComparison.OrdinalIgnoreCase) ? "BR" : "RP")}.{id}",
            DataSetReference = $"IEDLD0/LLN0.{id}-dataset",
            Bindings = [point]
        };

    private static Iec61850MonitorPoint Point(string name, string reference)
        => new()
        {
            DeviceId = "ied-1",
            DeviceName = "IED-1",
            SignalName = name,
            IecReference = reference
        };

    private static Iec61850MonitorDevice Device()
        => new()
        {
            DeviceId = "ied-1",
            Name = "IED-1",
            IpAddress = "192.0.2.10",
            Port = 102
        };

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
