using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticAcquisitionParityIntegrityP76BTests
{
    [Fact]
    public void MatchingPlanHash_DoesNotProveParityAgainstDifferentModelIed()
    {
        var device = Device(Model("IED-B", DataSet()));
        var evidence = Evidence(device, Plan());
        Assert.False(evidence.IsComparable);
        Assert.Contains("IED identity", evidence.IncomparableReason, StringComparison.Ordinal);
        Assert.Equal(StaticAcquisitionParityStatus.InsufficientEvidence,
            StaticAcquisitionParityTracker.Compare(evidence, evidence).Status);
    }

    [Fact]
    public void DuplicateNormalizedDataSetReferences_AreNotSilentlyFirstWins()
    {
        var original = DataSet();
        var differentlyEncoded = DataSet("IED-A/LLN0$Events");
        var device = Device(Model("IED-A", original, differentlyEncoded));
        var evidence = Evidence(device, Plan());
        Assert.False(evidence.IsComparable);
        Assert.Contains("ambiguous", evidence.IncomparableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateMemberOrdinal_CannotProduceFalseSemanticMatch()
    {
        var members = new[]
        {
            Member(0, "IED-A/XCBR1.Pos.stVal", "ST"),
            Member(0, "IED-A/XSWI1.Pos.stVal", "ST")
        };
        var device = Device(Model("IED-A", DataSet(members: members)));
        var evidence = Evidence(device, Plan());
        Assert.False(evidence.IsComparable);
        Assert.Contains("duplicate ordered member indices", evidence.IncomparableReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "ST", "member identity")]
    [InlineData("IED-A/XCBR1.Pos.stVal", "", "member identity")]
    public void MissingOrderedMemberReferenceOrFc_IsIncomparable(string reference, string fc, string reason)
    {
        var device = Device(Model("IED-A", DataSet(members: [Member(0, reference, fc)])));
        var evidence = Evidence(device, Plan());
        Assert.False(evidence.IsComparable);
        Assert.Contains(reason, evidence.IncomparableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void FakeEngineCounters_CannotQualifyIdenticalPartialPlans()
    {
        var device = Device(Model("IED-A", DataSet()));
        var partial = Plan(requested: 2, brcb: 2);
        var evidence = Evidence(device, partial);
        Assert.False(evidence.IsComparable);
        Assert.Contains("coverage", evidence.IncomparableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void WrongRcbFamilyAndMissingSelectedFc_AreEvidenceFailures()
    {
        var device = Device(Model("IED-A", DataSet()));
        var report = Plan(kind: "StaticUrcb", selectedFc: "");
        var evidence = Evidence(device, report);
        Assert.False(evidence.IsComparable);
        Assert.Contains("RCB family", evidence.IncomparableReason, StringComparison.Ordinal);
        Assert.Contains("selected signal identity", evidence.IncomparableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteCanonicalStaticEvidence_StaysComparableWithoutSlotIdentity()
    {
        var device = Device(Model("IED-A", DataSet()));
        var first = Evidence(device, Plan(rcb: "IED-A/LLN0.BR.Rpt_ind01"));
        var second = Evidence(device, Plan(rcb: "IED-A/LLN0.BR.Rpt_ind02"));
        Assert.True(first.IsComparable, first.IncomparableReason);
        Assert.Equal(first.SemanticFingerprint, second.SemanticFingerprint);
        Assert.Equal(StaticAcquisitionParityStatus.Equivalent,
            StaticAcquisitionParityTracker.Compare(first, second).Status);
    }

    private static StaticAcquisitionIngressEvidence Evidence(
        Iec61850MonitorDevice device, NativeHybridReportPlanningResult plan)
        => StaticAcquisitionParityTracker.BuildEvidence(
            device, plan, StaticAcquisitionIngressKind.LiveDiscovery);

    private static Iec61850MonitorDevice Device(LiveIedModelDiscoveryDocument model)
        => new() { Name = "IED-A", IpAddress = "192.0.2.10", LiveDiscoveryModel = model };

    private static LiveIedModelDiscoveryDocument Model(string iedName, params LiveIedDataSetModel[] dataSets)
        => new() { IedName = iedName, DataSets = dataSets };

    private static LiveIedDataSetModel DataSet(
        string reference = "IED-A/LLN0.Events",
        LiveIedDataSetMemberModel[]? members = null)
    {
        members ??= [Member(0, "IED-A/XCBR1.Pos.stVal", "ST")];
        return new LiveIedDataSetModel
        {
            Reference = reference,
            Domain = "IED-A",
            LogicalNode = "LLN0",
            Name = "Events",
            MemberCount = members.Length,
            Members = members
        };
    }

    private static LiveIedDataSetMemberModel Member(int index, string reference, string fc)
        => new() { Index = index, Reference = reference, FunctionalConstraint = fc };

    private static NativeHybridReportPlanningResult Plan(
        int requested = 1,
        int brcb = 1,
        string kind = "StaticBrcb",
        string selectedFc = "ST",
        string rcb = "IED-A/LLN0.BR.Rpt_ind01")
        => new()
        {
            IsAuthoritative = true,
            RequestedPointCount = requested,
            CatalogMappedPointCount = requested,
            StaticBrcbSignalCount = brcb,
            UncoveredSignalCount = 0,
            ReportPlans =
            [
                new ReportControlPlan
                {
                    IsEngineAuthoritative = true,
                    EngineAcquisitionKind = kind,
                    Buffered = true,
                    DataSetReference = "IED-A/LLN0.Events",
                    ReportControlReference = rcb,
                    Bindings =
                    [
                        new Iec61850MonitorPoint
                        {
                            DeviceId = "device-a",
                            IecReference = "IED-A/XCBR1.Pos.stVal",
                            FunctionalConstraint = selectedFc
                        }
                    ]
                }
            ]
        };
}
