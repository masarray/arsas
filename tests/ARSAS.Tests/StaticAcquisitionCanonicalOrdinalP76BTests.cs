using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticAcquisitionCanonicalOrdinalP76BTests
{
    private const string Indications = "IED-A/LLN0.Indications";
    private const string Measurements = "IED-A/LLN0.Measurements";

    // Exact field failure shape, fully synthetic: SCL FCDA ordinals 1..N versus
    // MMS DataSet-directory ordinals 0..N-1 on two different static RCB families.
    [Fact]
    public void OneBasedSclAndZeroBasedDiscovery_AreSemanticallyEquivalentWithoutRewritingSourceModels()
    {
        var liveModel = Model(
            DataSet(Indications, 0, ("IED-A/XCBR1.Pos", "ST"), ("IED-A/XSWI1.Pos", "ST")),
            DataSet(Measurements, 0, ("IED-A/MMXU1.A.phsA", "MX"), ("IED-A/MMXU1.TotW", "MX")));
        var sclModel = Model(
            DataSet(Indications, 1, ("IED-A/XCBR1.Pos", "ST"), ("IED-A/XSWI1.Pos", "ST")),
            DataSet(Measurements, 1, ("IED-A/MMXU1.A.phsA", "MX"), ("IED-A/MMXU1.TotW", "MX")));

        var discoveryDevice = Device(liveModel);
        var sclDevice = Device(sclModel, isOpenScl: true);
        var discovered = Build(discoveryDevice, Planning("Rpt_ind02"), StaticAcquisitionIngressKind.LiveDiscovery);
        var opened = Build(sclDevice, Planning("Rpt_ind01"), StaticAcquisitionIngressKind.OpenScl);

        Assert.True(discovered.IsComparable, discovered.IncomparableReason);
        Assert.True(opened.IsComparable, opened.IncomparableReason);
        Assert.Equal(discovered.SemanticLines, opened.SemanticLines);
        Assert.Equal(discovered.SemanticFingerprint, opened.SemanticFingerprint);
        Assert.NotEqual(discovered.RuntimeTargets[0], opened.RuntimeTargets[0]);
        Assert.Equal(StaticAcquisitionParityStatus.Equivalent,
            StaticAcquisitionParityTracker.Compare(discovered, opened).Status);
        Assert.StartsWith("static-brcb|dataset=", discovered.SemanticLines[1], StringComparison.Ordinal);
        Assert.Contains("members=2[0:ied-a/xcbr1.pos@ST,1:ied-a/xswi1.pos@ST]", discovered.SemanticLines[1], StringComparison.Ordinal);
        Assert.Equal(0, liveModel.DataSets[0].Members[0].Index);
        Assert.Equal(1, sclModel.DataSets[0].Members[0].Index);
    }

    [Fact]
    public void ReorderedMembers_RemainMismatchEvenAcrossDifferentIndexBases()
    {
        var discovered = Build(Device(Model(DataSet(Indications, 0,
                ("IED-A/XCBR1.Pos", "ST"), ("IED-A/XSWI1.Pos", "ST")))),
            PlanningOneDataSet(), StaticAcquisitionIngressKind.LiveDiscovery);
        var opened = Build(Device(Model(DataSet(Indications, 1,
                ("IED-A/XSWI1.Pos", "ST"), ("IED-A/XCBR1.Pos", "ST"))), true),
            PlanningOneDataSet(), StaticAcquisitionIngressKind.OpenScl);

        Assert.True(discovered.IsComparable);
        Assert.True(opened.IsComparable);
        Assert.Equal(StaticAcquisitionParityStatus.Mismatch,
            StaticAcquisitionParityTracker.Compare(discovered, opened).Status);
    }

    [Fact]
    public void FunctionalConstraintAndReferenceDrift_RemainMismatch()
    {
        var baseline = Build(Device(Model(DataSet(Indications, 0,
                ("IED-A/XCBR1.Pos", "ST"), ("IED-A/XSWI1.Pos", "ST")))),
            PlanningOneDataSet(), StaticAcquisitionIngressKind.LiveDiscovery);
        var fcDrift = Build(Device(Model(DataSet(Indications, 1,
                ("IED-A/XCBR1.Pos", "MX"), ("IED-A/XSWI1.Pos", "ST"))), true),
            PlanningOneDataSet(), StaticAcquisitionIngressKind.OpenScl);
        var referenceDrift = Build(Device(Model(DataSet(Indications, 1,
                ("IED-A/XCBR1.Pos", "ST"), ("IED-A/XSWI2.Pos", "ST"))), true),
            PlanningOneDataSet(), StaticAcquisitionIngressKind.OpenScl);

        Assert.Equal(StaticAcquisitionParityStatus.Mismatch,
            StaticAcquisitionParityTracker.Compare(baseline, fcDrift).Status);
        Assert.Equal(StaticAcquisitionParityStatus.Mismatch,
            StaticAcquisitionParityTracker.Compare(baseline, referenceDrift).Status);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 3)]
    [InlineData(100, 102)]
    public void NoncontiguousIndex_IsNotNormalizedIntoFalseMatch(int first, int second)
    {
        var valid = Build(Device(Model(DataSet(Indications, 0,
                ("IED-A/XCBR1.Pos", "ST"), ("IED-A/XSWI1.Pos", "ST")))),
            PlanningOneDataSet(), StaticAcquisitionIngressKind.LiveDiscovery);
        var brokenModel = Model(new LiveIedDataSetModel
        {
            Reference = Indications,
            Domain = "IED-A",
            LogicalNode = "LLN0",
            Name = "Indications",
            MemberCount = 2,
            Members =
            [
                Member(first, "IED-A/XCBR1.Pos", "ST"),
                Member(second, "IED-A/XSWI1.Pos", "ST")
            ]
        });
        var broken = Build(Device(brokenModel, true), PlanningOneDataSet(), StaticAcquisitionIngressKind.OpenScl);

        Assert.Equal(valid.SemanticFingerprint, broken.SemanticFingerprint);
        Assert.False(broken.IsComparable);
        Assert.Contains("noncontiguous", broken.IncomparableReason, StringComparison.Ordinal);
        Assert.Equal(StaticAcquisitionParityStatus.InsufficientEvidence,
            StaticAcquisitionParityTracker.Compare(valid, broken).Status);
    }

    [Fact]
    public void ArbitraryContiguousIndexOrigin_IsSourceNeutralWithoutOverflow()
    {
        var original = Build(Device(Model(DataSet(Indications, 0,
                ("IED-A/XCBR1.Pos", "ST"), ("IED-A/XSWI1.Pos", "ST")))),
            PlanningOneDataSet(), StaticAcquisitionIngressKind.LiveDiscovery);
        var shifted = Build(Device(Model(DataSet(Indications, int.MaxValue - 1,
                ("IED-A/XCBR1.Pos", "ST"), ("IED-A/XSWI1.Pos", "ST"))), true),
            PlanningOneDataSet(), StaticAcquisitionIngressKind.OpenScl);
        Assert.True(shifted.IsComparable, shifted.IncomparableReason);
        Assert.Equal(original.SemanticFingerprint, shifted.SemanticFingerprint);
    }

    private static StaticAcquisitionIngressEvidence Build(
        Iec61850MonitorDevice device, NativeHybridReportPlanningResult plan, StaticAcquisitionIngressKind ingress)
        => StaticAcquisitionParityTracker.BuildEvidence(device, plan, ingress);

    private static Iec61850MonitorDevice Device(LiveIedModelDiscoveryDocument model, bool isOpenScl = false)
    {
        var device = new Iec61850MonitorDevice { Name = "IED-A", IpAddress = "192.0.2.20", LiveDiscoveryModel = model };
        if (isOpenScl)
            device.SclWorkspace = new SclIedWorkspace
            {
                IedName = "IED-A",
                AccessPointName = "AP1",
                DesignModel = model
            };
        return device;
    }

    private static LiveIedModelDiscoveryDocument Model(params LiveIedDataSetModel[] dataSets)
        => new() { IedName = "IED-A", Source = "SyntheticFixture", DataSets = dataSets };

    private static LiveIedDataSetModel DataSet(
        string reference, int indexOrigin, params (string Reference, string Fc)[] members)
        => new()
        {
            Reference = reference,
            Domain = "IED-A",
            LogicalNode = "LLN0",
            Name = reference.Split('.').Last(),
            MemberCount = members.Length,
            Members = members.Select((member, ordinal) =>
                Member(checked(indexOrigin + ordinal), member.Reference, member.Fc)).ToArray()
        };

    private static LiveIedDataSetMemberModel Member(int index, string reference, string fc)
        => new() { Index = index, Reference = reference, FunctionalConstraint = fc };

    private static NativeHybridReportPlanningResult Planning(string brcbSlot)
        => new()
        {
            IsAuthoritative = true,
            RequestedPointCount = 4,
            CatalogMappedPointCount = 4,
            StaticBrcbSignalCount = 2,
            StaticUrcbSignalCount = 2,
            UncoveredSignalCount = 0,
            ReportPlans =
            [
                Report("StaticBrcb", Indications, "IED-A/LLN0.BR." + brcbSlot,
                    ("IED-A/XCBR1.Pos.stVal", "ST"), ("IED-A/XSWI1.Pos.stVal", "ST")),
                Report("StaticUrcb", Measurements, "IED-A/LLN0.RP.Rpt_meas01",
                    ("IED-A/MMXU1.A.phsA.cVal.mag.f", "MX"), ("IED-A/MMXU1.TotW.mag.f", "MX"))
            ]
        };

    private static NativeHybridReportPlanningResult PlanningOneDataSet()
        => new()
        {
            IsAuthoritative = true,
            RequestedPointCount = 2,
            CatalogMappedPointCount = 2,
            StaticBrcbSignalCount = 2,
            UncoveredSignalCount = 0,
            ReportPlans =
            [
                Report("StaticBrcb", Indications, "IED-A/LLN0.BR.Rpt_ind01",
                    ("IED-A/XCBR1.Pos.stVal", "ST"), ("IED-A/XSWI1.Pos.stVal", "ST"))
            ]
        };

    private static ReportControlPlan Report(
        string kind, string dataSet, string rcb, params (string Reference, string Fc)[] points)
        => new()
        {
            IsEngineAuthoritative = true,
            EngineAcquisitionKind = kind,
            Buffered = string.Equals(kind, "StaticBrcb", StringComparison.Ordinal),
            DataSetReference = dataSet,
            ReportControlReference = rcb,
            Bindings = points.Select(point => new Iec61850MonitorPoint
            {
                DeviceId = "device-a",
                IecReference = point.Reference,
                FunctionalConstraint = point.Fc
            }).ToArray()
        };
}
