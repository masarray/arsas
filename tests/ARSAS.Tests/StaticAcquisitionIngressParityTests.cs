using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticAcquisitionIngressParityTests
{
    [Fact]
    public void ConcreteIndexedRcbSlot_DoesNotChangeSemanticFingerprint()
    {
        var discoveryDevice = Device(
            "IED-A",
            LiveModel("IED-A", "IED-A/LLN0.Events",
                (0, "IED-A/XCBR1.Pos.stVal", "ST"),
                (1, "IED-A/MMXU1.A.phsA.cVal.mag.f", "MX")));
        var sclDevice = Device(
            "IED-A",
            LiveModel("IED-A", "IED-A/LLN0.Events",
                (0, "IED-A/XCBR1.Pos.stVal", "ST"),
                (1, "IED-A/MMXU1.A.phsA.cVal.mag.f", "MX")));
        sclDevice.SclWorkspace = new SclIedWorkspace
        {
            IedName = "IED-A",
            AccessPointName = "AP1",
            DesignModel = sclDevice.LiveDiscoveryModel!
        };

        var discovery = StaticAcquisitionParityTracker.BuildEvidence(
            discoveryDevice,
            Planning("IED-A/LLN0.BR.Rpt_ind01"),
            StaticAcquisitionIngressKind.LiveDiscovery);
        var openScl = StaticAcquisitionParityTracker.BuildEvidence(
            sclDevice,
            Planning("IED-A/LLN0.BR.Rpt_ind02"),
            StaticAcquisitionIngressKind.OpenScl);

        Assert.Equal(discovery.SemanticFingerprint, openScl.SemanticFingerprint);
        Assert.NotEqual(discovery.RuntimeTargets.Single(), openScl.RuntimeTargets.Single());

        var parity = StaticAcquisitionParityTracker.Compare(discovery, openScl);
        Assert.Equal(StaticAcquisitionParityStatus.Equivalent, parity.Status);
        Assert.Empty(parity.Differences);
    }

    [Fact]
    public void OrderedDataSetMemberDrift_IsReportedAsMismatch()
    {
        var discoveryDevice = Device(
            "IED-A",
            LiveModel("IED-A", "IED-A/LLN0.Events",
                (0, "IED-A/XCBR1.Pos.stVal", "ST"),
                (1, "IED-A/MMXU1.A.phsA.cVal.mag.f", "MX")));
        var sclDevice = Device(
            "IED-A",
            LiveModel("IED-A", "IED-A/LLN0.Events",
                (0, "IED-A/MMXU1.A.phsA.cVal.mag.f", "MX"),
                (1, "IED-A/XCBR1.Pos.stVal", "ST")));
        // A genuine order comparison requires both ingress models to be populated.
        // Without this workspace the old regression "passed" due to missing SCL data.
        sclDevice.SclWorkspace = new SclIedWorkspace
        {
            IedName = "IED-A",
            AccessPointName = "AP1",
            DesignModel = sclDevice.LiveDiscoveryModel!
        };

        var discovery = StaticAcquisitionParityTracker.BuildEvidence(
            discoveryDevice,
            Planning("IED-A/LLN0.BR.Rpt_ind01"),
            StaticAcquisitionIngressKind.LiveDiscovery);
        var openScl = StaticAcquisitionParityTracker.BuildEvidence(
            sclDevice,
            Planning("IED-A/LLN0.BR.Rpt_ind01"),
            StaticAcquisitionIngressKind.OpenScl);

        var parity = StaticAcquisitionParityTracker.Compare(discovery, openScl);

        Assert.NotEqual(discovery.SemanticFingerprint, openScl.SemanticFingerprint);
        Assert.Equal(StaticAcquisitionParityStatus.Mismatch, parity.Status);
        Assert.Contains(parity.Differences, item =>
            item.StartsWith("Discovery only:", StringComparison.Ordinal));
        Assert.Contains(parity.Differences, item =>
            item.StartsWith("Open SCL only:", StringComparison.Ordinal));
    }

    [Fact]
    public void Record_KeepsBothIngressesOnOneLogicalDevice()
    {
        var device = Device(
            "IED-A",
            LiveModel("IED-A", "IED-A/LLN0.Events",
                (0, "IED-A/XCBR1.Pos.stVal", "ST"),
                (1, "IED-A/MMXU1.A.phsA.cVal.mag.f", "MX")));

        var first = StaticAcquisitionParityTracker.Record(device, Planning("IED-A/LLN0.BR.Rpt_ind01"));
        Assert.Equal(StaticAcquisitionParityStatus.AwaitingOtherIngress, first.Status);
        Assert.NotNull(first.Discovery);
        Assert.Null(first.OpenScl);

        device.SclWorkspace = new SclIedWorkspace
        {
            IedName = "IED-A",
            AccessPointName = "AP1",
            DesignModel = device.LiveDiscoveryModel!
        };

        var second = StaticAcquisitionParityTracker.Record(device, Planning("IED-A/LLN0.BR.Rpt_ind02"));
        Assert.Equal(StaticAcquisitionParityStatus.Equivalent, second.Status);
        Assert.NotNull(second.Discovery);
        Assert.NotNull(second.OpenScl);
    }

    [Fact]
    public void TwoMissingStaticPlans_MustNotBeMistakenForEquivalentRuntimeReporting()
    {
        var device = Device("IED-A", LiveModel("IED-A", "IED-A/LLN0.Events",
            (0, "IED-A/XCBR1.Pos.stVal", "ST")));
        var noPlan = new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            RequestedPointCount = 1,
            UncoveredSignalCount = 1
        };
        var discovery = StaticAcquisitionParityTracker.BuildEvidence(
            device, noPlan, StaticAcquisitionIngressKind.LiveDiscovery);
        var scl = StaticAcquisitionParityTracker.BuildEvidence(
            device, noPlan, StaticAcquisitionIngressKind.OpenScl);

        // Equal hashes for equally absent plans do not prove parity.
        Assert.Equal(discovery.SemanticFingerprint, scl.SemanticFingerprint);
        Assert.False(discovery.IsComparable);
        Assert.False(scl.IsComparable);
        var parity = StaticAcquisitionParityTracker.Compare(discovery, scl);
        Assert.Equal(StaticAcquisitionParityStatus.InsufficientEvidence, parity.Status);
        Assert.Contains("INCOMPLETE EVIDENCE", parity.Summary, StringComparison.Ordinal);
        Assert.NotEmpty(parity.Differences);
    }

    [Fact]
    public void MissingOrderedDataSetAuthority_MustNotProduceMatch()
    {
        var model = LiveModel("IED-A", "IED-A/LLN0.Unrelated",
            (0, "IED-A/XCBR1.Pos.stVal", "ST"));
        var device = Device("IED-A", model);
        device.SclWorkspace = new SclIedWorkspace
        {
            IedName = "IED-A",
            AccessPointName = "AP1",
            DesignModel = model
        };

        var discovery = StaticAcquisitionParityTracker.BuildEvidence(
            device, Planning("IED-A/LLN0.BR.Rpt_ind01"),
            StaticAcquisitionIngressKind.LiveDiscovery);
        var scl = StaticAcquisitionParityTracker.BuildEvidence(
            device, Planning("IED-A/LLN0.BR.Rpt_ind02"),
            StaticAcquisitionIngressKind.OpenScl);

        Assert.Equal(discovery.SemanticFingerprint, scl.SemanticFingerprint);
        Assert.False(discovery.IsComparable);
        Assert.False(scl.IsComparable);
        Assert.Contains("missing", discovery.IncomparableReason, StringComparison.Ordinal);
        Assert.Equal(StaticAcquisitionParityStatus.InsufficientEvidence,
            StaticAcquisitionParityTracker.Compare(discovery, scl).Status);
    }

    [Fact]
    public void IncompleteNewIngress_ReplacesOldProofRatherThanRetainingStaleMatch()
    {
        var device = Device("IED-A", LiveModel("IED-A", "IED-A/LLN0.Events",
            (0, "IED-A/XCBR1.Pos.stVal", "ST"),
            (1, "IED-A/MMXU1.A.phsA.cVal.mag.f", "MX")));
        StaticAcquisitionParityTracker.Record(device, Planning("IED-A/LLN0.BR.Rpt_ind01"));
        device.SclWorkspace = new SclIedWorkspace
        {
            IedName = "IED-A",
            AccessPointName = "AP1",
            DesignModel = device.LiveDiscoveryModel!
        };
        var matched = StaticAcquisitionParityTracker.Record(device, Planning("IED-A/LLN0.BR.Rpt_ind02"));
        Assert.Equal(StaticAcquisitionParityStatus.Equivalent, matched.Status);

        var failure = new NativeHybridReportPlanningResult
        {
            IsAuthoritative = false,
            RequestedPointCount = 2,
            UncoveredSignalCount = 2
        };
        var afterFailure = StaticAcquisitionParityTracker.Record(device, failure);
        Assert.Equal(StaticAcquisitionParityStatus.InsufficientEvidence, afterFailure.Status);
        Assert.False(afterFailure.OpenScl!.IsComparable);
    }

    [Fact]
    public void P73_RuntimeAndDiagnosticsExposeParityWithoutUsingConcreteSlotAsIdentity()
    {
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        var diagnostics = Read("Services/DiagnosticReportBuilder.cs");
        var tracker = Read("Services/StaticAcquisitionParityTracker.cs");

        Assert.Contains("StaticAcquisitionParityTracker.Record", runtime, StringComparison.Ordinal);
        Assert.Contains("Static ingress parity evidence", runtime, StringComparison.Ordinal);
        Assert.Contains("AppendStaticIngressParity", diagnostics, StringComparison.Ordinal);
        Assert.Contains("Concrete live RCB slots are diagnostic-only", runtime, StringComparison.Ordinal);
        Assert.Contains("RuntimeTargets", tracker, StringComparison.Ordinal);

        // Slot identity belongs to runtime diagnostics; only semanticLines are hashed.
        // The old guard included runtimeTargets.Add(...) and falsely rejected valid diagnostics.
        var fingerprintRegion = tracker[
            tracker.IndexOf("var canonicalText", StringComparison.Ordinal)..
            tracker.IndexOf("return new StaticAcquisitionIngressEvidence", StringComparison.Ordinal)];
        Assert.Contains("SHA256.HashData", fingerprintRegion, StringComparison.Ordinal);
        Assert.Contains("Encoding.UTF8.GetBytes(canonicalText)", fingerprintRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("ReportControlReference", fingerprintRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("runtimeTargets", fingerprintRegion, StringComparison.Ordinal);
    }

    private static Iec61850MonitorDevice Device(
        string name,
        LiveIedModelDiscoveryDocument model)
        => new()
        {
            Name = name,
            IpAddress = "192.0.2.10",
            LiveDiscoveryModel = model
        };

    private static LiveIedModelDiscoveryDocument LiveModel(
        string iedName,
        string dataSetReference,
        params (int Index, string Reference, string Fc)[] members)
        => new()
        {
            IedName = iedName,
            Source = "Test",
            DataSets =
            [
                new LiveIedDataSetModel
                {
                    Reference = dataSetReference,
                    Domain = iedName,
                    LogicalNode = "LLN0",
                    Name = "Events",
                    MemberCount = members.Length,
                    Members = members
                        .Select(member => new LiveIedDataSetMemberModel
                        {
                            Index = member.Index,
                            Reference = member.Reference,
                            FunctionalConstraint = member.Fc
                        })
                        .ToArray()
                }
            ]
        };

    private static NativeHybridReportPlanningResult Planning(string concreteRcb)
    {
        var point1 = new Iec61850MonitorPoint
        {
            DeviceId = "device-a",
            IecReference = "IED-A/XCBR1.Pos.stVal",
            FunctionalConstraint = "ST"
        };
        var point2 = new Iec61850MonitorPoint
        {
            DeviceId = "device-a",
            IecReference = "IED-A/MMXU1.A.phsA.cVal.mag.f",
            FunctionalConstraint = "MX"
        };

        return new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            Authority = "ARIEC61850 canonical static acquisition",
            Status = "Ready",
            RequestedPointCount = 2,
            CatalogMappedPointCount = 2,
            StaticBrcbSignalCount = 2,
            UncoveredSignalCount = 0,
            ReportPlans =
            [
                new ReportControlPlan
                {
                    PlanId = "static-events",
                    IsEngineAuthoritative = true,
                    EngineAcquisitionKind = "StaticBrcb",
                    Buffered = true,
                    DataSetReference = "IED-A/LLN0.Events",
                    ReportControlReference = concreteRcb,
                    Bindings = [point1, point2]
                }
            ]
        };
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
