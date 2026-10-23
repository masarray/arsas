using ArIED61850Tester.Services;
using ArMms = AR.Iec61850.Mms;

namespace ARSAS.Tests;

public sealed class DynamicReportActivationCommissioningServiceTests
{
    [Fact]
    public void ExactQualifiedMemberResolution_UsesMmsIdentityWithoutSuffixFallback()
    {
        var point = Point("LD0", "GGIO1$ST$Ind1$stVal", "GGIO1", "ST", "Ind1.stVal");
        var directory = new ArMms.MmsIedModelDirectory([point]);

        var ok = DynamicReportActivationCommissioningService.TryResolveExactQualifiedMembers(
            directory,
            ["LD0/GGIO1$ST$Ind1$stVal"],
            out var resolved,
            out _);
        var fuzzy = DynamicReportActivationCommissioningService.TryResolveExactQualifiedMembers(
            directory,
            ["LD0/Ind1$stVal"],
            out _,
            out _);

        Assert.True(ok);
        Assert.Single(resolved);
        Assert.False(fuzzy);
    }

    [Fact]
    public void UrcbSelection_RejectsBrcbAndRequiresGiDataSetNameAndRptId()
    {
        var inventory = new ArMms.MmsReportInventory();
        inventory.ReportControls.Add(Rcb("LD0/LLN0.BR01", buffered: true, rptId: "B1", trgOps: "gi", optFlds: "data-set-name"));
        inventory.ReportControls.Add(Rcb("LD0/LLN0.RP01", buffered: false, rptId: "R1", trgOps: "dchg", optFlds: "data-set-name"));
        inventory.ReportControls.Add(Rcb("LD0/LLN0.RP02", buffered: false, rptId: "R2", trgOps: "gi", optFlds: "sequence-number"));
        inventory.ReportControls.Add(Rcb("LD0/LLN0.RP03", buffered: false, rptId: "R3", trgOps: "dchg gi", optFlds: "data-set-name reason-for-inclusion"));

        var selected = DynamicReportActivationCommissioningService.SelectQualifiedUrcb(inventory, "LD0", out var reason);

        Assert.NotNull(selected);
        Assert.False(selected!.Buffered);
        Assert.Equal("LD0/LLN0.RP03", selected.Reference);
        Assert.Contains("selected=LD0/LLN0.RP03", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshUrcbGate_RequiresExplicitFreeStateWhenResvIsExposed()
    {
        var safe = Snapshot(resv: "false", rptEna: "false", dataSet: "", owner: "");
        var reserved = Snapshot(resv: "true", rptEna: "false", dataSet: "", owner: "");
        var occupied = Snapshot(resv: "false", rptEna: "true", dataSet: "", owner: "");
        var bound = Snapshot(resv: "false", rptEna: "false", dataSet: "LD0/LLN0.Static", owner: "");

        Assert.True(DynamicReportActivationCommissioningService.IsFreshUrcbSafeForG24(safe, out _));
        Assert.False(DynamicReportActivationCommissioningService.IsFreshUrcbSafeForG24(reserved, out _));
        Assert.False(DynamicReportActivationCommissioningService.IsFreshUrcbSafeForG24(occupied, out _));
        Assert.False(DynamicReportActivationCommissioningService.IsFreshUrcbSafeForG24(bound, out _));
    }

    [Fact]
    public void PlanGate_RequiresExactOrderedQualifiedMembersAndOneUrcb()
    {
        var refs = new[]
        {
            "LD0/GGIO1$ST$Ind1$stVal",
            "LD0/GGIO1$ST$Ind2$stVal"
        };
        var p1 = Point("LD0", "GGIO1$ST$Ind1$stVal", "GGIO1", "ST", "Ind1.stVal");
        var p2 = Point("LD0", "GGIO1$ST$Ind2$stVal", "GGIO1", "ST", "Ind2.stVal");
        var rcb = Rcb("LD0/LLN0.RP01", buffered: false, rptId: "R1", trgOps: "gi", optFlds: "data-set-name");
        var plan = new ArMms.MmsReportSubscriptionPlan
        {
            Mode = ArMms.MmsReportSubscriptionPlanMode.DynamicDataSet,
            Status = ArMms.MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
            ReportControl = rcb,
            DataSetReference = "LD0/LLN0.AR_G24_TEST",
            DynamicPoints = [p1, p2],
            Members = [Member(p1), Member(p2)]
        };

        Assert.True(DynamicReportActivationCommissioningService.ValidatePlanAgainstEnvelope(plan, rcb.Reference, refs, out _));
        Assert.False(DynamicReportActivationCommissioningService.ValidatePlanAgainstEnvelope(plan, rcb.Reference, refs.Reverse().ToArray(), out _));
    }

    [Fact]
    public void InformationReportGate_AcceptsOnlyFullExactOrderedMappedFrame()
    {
        var refs = new[]
        {
            "LD0/GGIO1$ST$Ind1$stVal",
            "LD0/GGIO1$ST$Ind2$stVal"
        };
        var p1 = Point("LD0", "GGIO1$ST$Ind1$stVal", "GGIO1", "ST", "Ind1.stVal");
        var p2 = Point("LD0", "GGIO1$ST$Ind2$stVal", "GGIO1", "ST", "Ind2.stVal");
        var valid = Frame(
            "R1",
            "LD0/LLN0.AR_G24_TEST",
            [Member(p1), Member(p2)],
            [0, 1],
            reasons: ["general-interrogation"]);

        var result = DynamicReportActivationCommissioningService.ValidateInformationReportFrame(
            valid,
            "R1",
            "LD0/LLN0.AR_G24_TEST",
            refs);

        Assert.True(result.IsSuccess);
        Assert.Equal(ArMms.MmsDynamicInformationReportKind.GeneralInterrogation, result.Kind);
        Assert.Equal(2, result.AuthoritativePointCount);
    }

    [Fact]
    public void InformationReportGate_RejectsWrongDataSetPartialAndReorderedFrames()
    {
        var refs = new[]
        {
            "LD0/GGIO1$ST$Ind1$stVal",
            "LD0/GGIO1$ST$Ind2$stVal"
        };
        var p1 = Point("LD0", "GGIO1$ST$Ind1$stVal", "GGIO1", "ST", "Ind1.stVal");
        var p2 = Point("LD0", "GGIO1$ST$Ind2$stVal", "GGIO1", "ST", "Ind2.stVal");
        var members = new[] { Member(p1), Member(p2) };

        var wrongDataSet = Frame("R1", "LD0/LLN0.OTHER", members, [0, 1]);
        var partial = Frame("R1", "LD0/LLN0.AR_G24_TEST", [members[0]], [0]);
        var reordered = Frame("R1", "LD0/LLN0.AR_G24_TEST", [members[1], members[0]], [0, 1]);

        Assert.False(DynamicReportActivationCommissioningService.ValidateInformationReportFrame(wrongDataSet, "R1", "LD0/LLN0.AR_G24_TEST", refs).IsSuccess);
        Assert.False(DynamicReportActivationCommissioningService.ValidateInformationReportFrame(partial, "R1", "LD0/LLN0.AR_G24_TEST", refs).IsSuccess);
        Assert.False(DynamicReportActivationCommissioningService.ValidateInformationReportFrame(reordered, "R1", "LD0/LLN0.AR_G24_TEST", refs).IsSuccess);
    }

    [Fact]
    public void InformationReportGate_RejectsDecoderQuarantineAndAccessFailure()
    {
        var reference = "LD0/GGIO1$ST$Ind1$stVal";
        var point = Point("LD0", "GGIO1$ST$Ind1$stVal", "GGIO1", "ST", "Ind1.stVal");
        var member = Member(point);
        var quarantined = WithDecoder(Frame("R1", "LD0/LLN0.AR_G24_TEST", [member], [0]), "rejected-unmapped");
        var failed = new ArMms.MmsReportFrame
        {
            Header = new ArMms.MmsReportHeader { ReportId = "R1", DataSetReference = "LD0/LLN0.AR_G24_TEST" },
            DecoderMode = "optflds-driven",
            IncludedDataSetIndexes = [0],
            Values =
            [
                new ArMms.MmsReportValue
                {
                    Index = 0,
                    Member = member,
                    FailureCode = 3
                }
            ]
        };

        Assert.False(DynamicReportActivationCommissioningService.ValidateInformationReportFrame(quarantined, "R1", "LD0/LLN0.AR_G24_TEST", [reference]).IsSuccess);
        Assert.False(DynamicReportActivationCommissioningService.ValidateInformationReportFrame(failed, "R1", "LD0/LLN0.AR_G24_TEST", [reference]).IsSuccess);
    }

    private static ArMms.MmsFcResolvedPoint Point(string domain, string item, string ln, string fc, string path)
        => new()
        {
            Domain = domain,
            MmsItemName = item,
            LogicalNode = ln,
            FunctionalConstraint = fc,
            DataObjectPath = path
        };

    private static ArMms.MmsDataSetDirectoryMember Member(ArMms.MmsFcResolvedPoint point)
        => new()
        {
            Domain = point.Domain,
            MmsItemName = point.MmsItemName,
            UserReference = point.UserReference,
            FunctionalConstraint = point.FunctionalConstraint,
            LogicalNode = point.LogicalNode,
            DataObjectPath = point.DataObjectPath
        };

    private static ArMms.MmsReportControlCandidate Rcb(
        string reference,
        bool buffered,
        string rptId,
        string trgOps,
        string optFlds)
    {
        var slash = reference.IndexOf('/');
        var dot = reference.IndexOf('.', slash + 1);
        return new ArMms.MmsReportControlCandidate
        {
            Domain = reference[..slash],
            LogicalNode = reference[(slash + 1)..dot],
            Name = reference[(dot + 1)..],
            Reference = reference,
            Buffered = buffered,
            DataSetReference = string.Empty,
            DataSetProbeState = ArMms.MmsRcbDataSetProbeState.ReadSucceeded,
            ReportId = rptId,
            TriggerOptions = trgOps,
            OptionalFields = optFlds,
            EnabledState = "false",
            ReservationState = "false",
            ReservationTimeSeconds = "0",
            Status = "AttributeProbe",
            Attributes = ["DatSet", "RptEna", "Resv", "TrgOps", "OptFlds", "GI"]
        };
    }

    private static ArMms.MmsRcbAvailabilitySnapshot Snapshot(string resv, string rptEna, string dataSet, string owner)
        => new()
        {
            Reference = "LD0/LLN0.RP01",
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "RP01",
            Buffered = false,
            DataSetReference = dataSet,
            DataSetProbeState = ArMms.MmsRcbDataSetProbeState.ReadSucceeded,
            ReportId = "R1",
            TriggerOptions = "dchg gi",
            OptionalFields = "data-set-name reason-for-inclusion",
            EnabledState = rptEna,
            ReservationState = resv,
            ReservationTimeSeconds = "0",
            Owner = owner,
            Attributes = ["DatSet", "RptEna", "Resv", "TrgOps", "OptFlds", "GI"]
        };

    private static ArMms.MmsReportFrame Frame(
        string reportId,
        string dataSet,
        IReadOnlyList<ArMms.MmsDataSetDirectoryMember> members,
        IReadOnlyList<int> included,
        IReadOnlyList<string>? reasons = null)
        => new()
        {
            ReceivedAt = DateTimeOffset.UtcNow,
            Header = new ArMms.MmsReportHeader
            {
                ReportId = reportId,
                DataSetReference = dataSet
            },
            DecoderMode = "optflds-driven",
            IncludedDataSetIndexes = included.ToArray(),
            Values = members.Select((member, index) => new ArMms.MmsReportValue
            {
                Index = included[index],
                Member = member,
                Value = ArMms.MmsDataValue.Boolean(true),
                ReasonForInclusion = reasons ?? Array.Empty<string>()
            }).ToArray()
        };

    private static ArMms.MmsReportFrame WithDecoder(ArMms.MmsReportFrame source, string decoder)
        => new()
        {
            ReceivedAt = source.ReceivedAt,
            Header = source.Header,
            Values = source.Values,
            RawAccessResultCount = source.RawAccessResultCount,
            InclusionBitstringItemIndex = source.InclusionBitstringItemIndex,
            IncludedDataSetIndexes = source.IncludedDataSetIndexes,
            DecoderMode = decoder,
            ParseWarnings = source.ParseWarnings,
            Message = source.Message,
            ResponseHexPreview = source.ResponseHexPreview
        };
}
