using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticAcquisitionCausalTrafficTests
{
    [Fact]
    public void SemanticMatchAlone_IsNotDualIngressTrafficProof()
    {
        var device = DeviceWithTwoDataSets();
        var discoveryPlans = Plans("Rpt_ind01", "Rpt_meas01");
        var discovery = StaticAcquisitionParityTracker.Record(device, discoveryPlans);
        Assert.Equal(StaticAcquisitionParityStatus.AwaitingOtherIngress, discovery.Status);
        Assert.False(discovery.DualIngressTrafficProven);
        var discoveryAttempt = discovery.Discovery!.PlanningAttemptId;

        // Each exact RCB must provide a routed, schema-safe process update.
        Assert.True(StaticAcquisitionParityTracker.TryRecordRoutedReport(
            device, discoveryPlans.ReportPlans[0], discoveryAttempt, out var first));
        Assert.Equal(1, first.Discovery!.RoutedTargetCount);
        Assert.False(first.Discovery.AllPlannedTargetsRouted);
        Assert.False(first.DualIngressTrafficProven);
        Assert.True(StaticAcquisitionParityTracker.TryRecordRoutedReport(
            device, discoveryPlans.ReportPlans[1], discoveryAttempt, out var bothDiscovery));
        Assert.True(bothDiscovery.Discovery!.AllPlannedTargetsRouted);

        device.SclWorkspace = new SclIedWorkspace
        {
            IedName = device.Name,
            AccessPointName = "AP1",
            DesignModel = device.LiveDiscoveryModel!
        };
        var sclPlans = Plans("Rpt_ind02", "Rpt_meas01");
        var matched = StaticAcquisitionParityTracker.Record(device, sclPlans);
        Assert.Equal(StaticAcquisitionParityStatus.Equivalent, matched.Status);
        Assert.Equal(matched.Discovery!.SemanticFingerprint, matched.OpenScl!.SemanticFingerprint);
        Assert.False(matched.DualIngressTrafficProven);
        Assert.Equal(0, matched.OpenScl.RoutedTargetCount);

        var sclAttempt = matched.OpenScl.PlanningAttemptId;
        Assert.True(StaticAcquisitionParityTracker.TryRecordRoutedReport(
            device, sclPlans.ReportPlans[0], sclAttempt, out var partiallyRouted));
        Assert.Equal(1, partiallyRouted.OpenScl!.RoutedTargetCount);
        Assert.False(partiallyRouted.DualIngressTrafficProven);
        Assert.True(StaticAcquisitionParityTracker.TryRecordRoutedReport(
            device, sclPlans.ReportPlans[1], sclAttempt, out var complete));
        Assert.True(complete.DualIngressTrafficProven);
        Assert.Contains("DUAL INGRESS TRAFFIC PROVEN", complete.TrafficQualificationSummary);
        Assert.Equal(StaticAcquisitionParityStatus.Equivalent, complete.Status);
    }

    [Fact]
    public void StaleAssociationAndOtherRcb_CannotBorrowPreviousTrafficProof()
    {
        var device = DeviceWithTwoDataSets();
        var plans = Plans("Rpt_ind01", "Rpt_meas01");
        var first = StaticAcquisitionParityTracker.Record(device, plans);
        var oldAttempt = first.Discovery!.PlanningAttemptId;
        var notSelected = Plans("Rpt_ind99", "Rpt_meas01").ReportPlans[0];

        Assert.False(StaticAcquisitionParityTracker.TryRecordRoutedReport(
            device, notSelected, oldAttempt, out _));
        Assert.False(StaticAcquisitionParityTracker.TryRecordRoutedReport(
            device, plans.ReportPlans[0], Guid.Empty, out _));
        Assert.True(StaticAcquisitionParityTracker.TryRecordRoutedReport(
            device, plans.ReportPlans[0], oldAttempt, out var routed));
        Assert.False(StaticAcquisitionParityTracker.TryRecordRoutedReport(
            device, plans.ReportPlans[0], oldAttempt, out _));
        Assert.Equal(1, routed.Discovery!.RoutedTargetCount);

        // A new plan on the same ingress is a replacement MMS association's attempt.
        var rearmed = StaticAcquisitionParityTracker.Record(device, plans);
        var newAttempt = rearmed.Discovery!.PlanningAttemptId;
        Assert.NotEqual(oldAttempt, newAttempt);
        Assert.Equal(0, rearmed.Discovery.RoutedTargetCount);
        Assert.False(StaticAcquisitionParityTracker.TryRecordRoutedReport(
            device, plans.ReportPlans[1], oldAttempt, out _));
        Assert.True(StaticAcquisitionParityTracker.TryRecordRoutedReport(
            device, plans.ReportPlans[1], newAttempt, out var newReport));
        Assert.Equal(1, newReport.Discovery!.RoutedTargetCount);
        Assert.False(newReport.Discovery.AllPlannedTargetsRouted);
    }

    [Fact]
    public void SwitchingIngress_DoesNotRecreditOldAssociation()
    {
        var device = DeviceWithTwoDataSets();
        var plans = Plans("Rpt_ind01", "Rpt_meas01");
        var recorded = StaticAcquisitionParityTracker.Record(device, plans);
        var discoveryAttempt = recorded.Discovery!.PlanningAttemptId;
        device.SclWorkspace = new SclIedWorkspace
        {
            IedName = device.Name,
            AccessPointName = "AP1",
            DesignModel = device.LiveDiscoveryModel!
        };

        Assert.False(StaticAcquisitionParityTracker.TryRecordRoutedReport(
            device, plans.ReportPlans[0], discoveryAttempt, out _));
    }

    [Fact]
    public void RuntimeOnlyCreditsAcceptedProcessValueAndDoesNotChangeReportingSetup()
    {
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        var tracker = Read("Services/StaticAcquisitionParityTracker.cs");
        var diagnostic = Read("Services/DiagnosticReportBuilder.cs");

        Assert.Contains("session.StaticDataSetReportOnly && update.HasValue", runtime);
        Assert.Contains("StaticAcquisitionParityTracker.TryRecordRoutedReport", runtime);
        Assert.Contains("CurrentStaticParityAttemptId = Guid.Empty", runtime);
        Assert.Contains("trustReportEdge: true", runtime);
        Assert.Contains("expectedPlanningAttemptId == Guid.Empty", tracker);
        Assert.Contains("RoutedReportTargets.Contains(target", tracker);
        Assert.Contains("Routed traffic   :", diagnostic);
        Assert.Contains("Physical qualifier: semantic MATCH is not traffic proof", diagnostic);
    }

    private static Iec61850MonitorDevice DeviceWithTwoDataSets()
        => new()
        {
            Name = "IED-A",
            IpAddress = "192.0.2.10",
            LiveDiscoveryModel = new LiveIedModelDiscoveryDocument
            {
                IedName = "IED-A",
                Source = "Test",
                DataSets =
                [
                    DataSet("IED-A/LLN0.Indications", "IED-A/XCBR1.Pos.stVal", "ST"),
                    DataSet("IED-A/LLN0.Measurements", "IED-A/MMXU1.A.phsA.cVal.mag.f", "MX")
                ]
            }
        };

    private static LiveIedDataSetModel DataSet(string reference, string point, string fc)
        => new()
        {
            Reference = reference,
            Domain = "IED-A",
            LogicalNode = "LLN0",
            Name = reference.Split('.').Last(),
            MemberCount = 1,
            Members =
            [
                new LiveIedDataSetMemberModel
                {
                    Index = 0,
                    Reference = point,
                    FunctionalConstraint = fc
                }
            ]
        };

    private static NativeHybridReportPlanningResult Plans(string brcb, string urcb)
        => new()
        {
            IsAuthoritative = true,
            Authority = "ARIEC61850 canonical static acquisition",
            Status = "Ready",
            RequestedPointCount = 2,
            CatalogMappedPointCount = 2,
            StaticBrcbSignalCount = 1,
            StaticUrcbSignalCount = 1,
            UncoveredSignalCount = 0,
            ReportPlans =
            [
                Plan("StaticBrcb", "IED-A/LLN0.Indications", "IED-A/LLN0.BR." + brcb, "IED-A/XCBR1.Pos.stVal", "ST"),
                Plan("StaticUrcb", "IED-A/LLN0.Measurements", "IED-A/LLN0.RP." + urcb, "IED-A/MMXU1.A.phsA.cVal.mag.f", "MX")
            ]
        };

    private static ReportControlPlan Plan(string kind, string dataSet, string rcb, string point, string fc)
        => new()
        {
            PlanId = dataSet,
            IsEngineAuthoritative = true,
            EngineAcquisitionKind = kind,
            Buffered = kind == "StaticBrcb",
            DataSetReference = dataSet,
            ReportControlReference = rcb,
            Bindings =
            [
                new Iec61850MonitorPoint
                {
                    DeviceId = "device-a",
                    IecReference = point,
                    FunctionalConstraint = fc
                }
            ]
        };

    private static string Read(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(path))
                return File.ReadAllText(path);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
