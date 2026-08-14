using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class HybridReportPhysicalValidationTests
{
    [Fact]
    public void PlanAndActivationWithoutTraffic_DoNotBecomePhysicalReportEvidence()
    {
        var staticPlan = EnginePlan("static", "StaticBrcb", "IEDLD0/LLN0.BR.brcb01", "IEDLD0/LLN0.Events", "p-static");
        var dynamicPlan = EnginePlan("dynamic", "DynamicUrcb", "IEDLD0/LLN0.RP.urcb02", "IEDLD0/LLN0.ARSAS_DYNAMIC", "p-dynamic");
        var tracker = new HybridReportPhysicalValidationTracker();
        tracker.Reset(new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            Authority = "ARIEC61850 MmsHybridReportAcquisitionPlanner",
            Status = "FullReportCoverage",
            ReportPlans = [staticPlan, dynamicPlan],
            StaticBrcbSignalCount = 1,
            DynamicUrcbSignalCount = 1,
            Warnings = ["Characterization warning"]
        });

        tracker.RecordActivation(staticPlan, new NativeReportMonitorStartResult
        {
            IsSuccess = true,
            PlanId = staticPlan.PlanId,
            Message = "Static BRCB active"
        });
        tracker.RecordActivation(dynamicPlan, new NativeReportMonitorStartResult
        {
            IsSuccess = false,
            PlanId = dynamicPlan.PlanId,
            Message = "Dynamic URCB remained unavailable"
        });

        var snapshot = tracker.Capture(Device());

        Assert.False(snapshot.HasPhysicalReportEvidence);
        Assert.Equal(1, snapshot.ActivatedReportPlanCount);
        Assert.Equal(1, snapshot.FailedActivationCount);
        Assert.Equal(0, snapshot.ReportFrameCount);
        Assert.Equal(0, snapshot.ReportUpdateCount);
        Assert.Equal(0, snapshot.ChangeVerifiedPointCount);
        Assert.Equal(2, snapshot.Plans.Count);
        Assert.Contains("Characterization warning", snapshot.Warnings);
    }

    [Fact]
    public void RealReportSlice_IsRecordedSeparatelyFromActivationAndPollingFallback()
    {
        var reportPlan = EnginePlan("static", "StaticUrcb", "IEDLD0/LLN0.RP.urcb01", "IEDLD0/LLN0.Events", "p-report");
        var tracker = new HybridReportPhysicalValidationTracker();
        tracker.Reset(new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            Authority = "ARIEC61850 MmsHybridReportAcquisitionPlanner",
            Status = "HybridReportAndPolling",
            ReportPlans = [reportPlan],
            StaticUrcbSignalCount = 1,
            PollingFallbackSignalCount = 3,
            UncoveredSignalCount = 2
        });
        tracker.RecordActivation(reportPlan, new NativeReportMonitorStartResult
        {
            IsSuccess = true,
            PlanId = reportPlan.PlanId,
            Message = "Static URCB active"
        });

        var receivedAt = new DateTimeOffset(2026, 8, 15, 1, 2, 3, TimeSpan.Zero);
        tracker.RecordSlice(
            reportPlan,
            new NativeReportMonitorSliceResult
            {
                PlanId = reportPlan.PlanId,
                ReportFrames =
                [
                    new NativeReportFrameMetadata
                    {
                        ReportControlReference = reportPlan.ReportControlReference,
                        DataSetReference = reportPlan.DataSetReference,
                        ReceivedAt = receivedAt
                    }
                ],
                Updates =
                [
                    new NativeReportValueUpdate
                    {
                        Reference = "IEDLD0/GGIO1.Ind1.stVal",
                        Value = "true",
                        Reason = "dchg",
                        UpdatedAt = receivedAt
                    }
                ]
            },
            ["point-1"]);

        var snapshot = tracker.Capture(Device());
        var plan = Assert.Single(snapshot.Plans);

        Assert.True(snapshot.HasPhysicalReportEvidence);
        Assert.Equal(1, snapshot.ReportFrameCount);
        Assert.Equal(1, snapshot.ReportUpdateCount);
        Assert.Equal(1, snapshot.ChangeVerifiedPointCount);
        Assert.Equal(3, snapshot.PollingFallbackPointCount);
        Assert.Equal(2, snapshot.UncoveredPointCount);
        Assert.Equal(receivedAt, plan.FirstReportAtUtc);
        Assert.Equal(receivedAt, plan.LastReportAtUtc);
        Assert.Equal("StaticUrcb", plan.AcquisitionKind);
    }

    [Fact]
    public void LegacyPlanTraffic_IsNotClaimedAsAriecHybridPhysicalEvidence()
    {
        var legacyPlan = new ReportControlPlan
        {
            PlanId = "legacy",
            IsEngineAuthoritative = false,
            EngineAcquisitionKind = string.Empty,
            ReportControlReference = "IEDLD0/LLN0.RP.urcbLegacy"
        };
        var tracker = new HybridReportPhysicalValidationTracker();
        tracker.Reset(new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            ReportPlans = []
        });

        tracker.RecordActivation(legacyPlan, new NativeReportMonitorStartResult
        {
            IsSuccess = true,
            PlanId = legacyPlan.PlanId,
            Message = "Legacy active"
        });
        tracker.RecordSlice(
            legacyPlan,
            new NativeReportMonitorSliceResult
            {
                PlanId = legacyPlan.PlanId,
                ReportFrames = [new NativeReportFrameMetadata { ReceivedAt = DateTimeOffset.UtcNow }]
            },
            ["legacy-point"]);

        var snapshot = tracker.Capture(Device());

        Assert.False(snapshot.HasPhysicalReportEvidence);
        Assert.Empty(snapshot.Plans);
        Assert.Equal(0, snapshot.ReportFrameCount);
        Assert.Equal(0, snapshot.ChangeVerifiedPointCount);
    }

    private static ReportControlPlan EnginePlan(
        string planId,
        string kind,
        string rcb,
        string dataSet,
        string pointReference)
        => new()
        {
            PlanId = planId,
            IsEngineAuthoritative = true,
            EngineAcquisitionKind = kind,
            ReportControlReference = rcb,
            DataSetReference = dataSet,
            Bindings =
            [
                new Iec61850MonitorPoint
                {
                    DeviceId = "ied-1",
                    DeviceName = "IED-1",
                    SignalName = pointReference,
                    IecReference = pointReference
                }
            ]
        };

    private static Iec61850MonitorDevice Device()
        => new()
        {
            DeviceId = "ied-1",
            Name = "IED-1",
            IpAddress = "192.0.2.10",
            Port = 102
        };
}
