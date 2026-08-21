using ArIED61850Tester.Services;
using ArMms = AR.Iec61850.Mms;

namespace ARSAS.Tests;

public sealed class DynamicReportSpontaneousDataChangeCommissioningServiceTests
{
    [Fact]
    public void G25A_UsesDchgOnlyCanonicalTargetAndNoGiReceive()
    {
        Assert.Equal("dchg", DynamicReportSpontaneousDataChangeCommissioningService.TemporaryTriggerOptions);
        Assert.Equal("0240", DynamicReportSpontaneousDataChangeCommissioningService.ExpectedCanonicalTriggerRaw);
        Assert.Equal("reason-for-inclusion data-set-name", DynamicReportSpontaneousDataChangeCommissioningService.TemporaryOptionalFields);
        Assert.Equal("061800", DynamicReportSpontaneousDataChangeCommissioningService.ExpectedCanonicalOptionalFieldsRaw);

        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "DynamicReportSpontaneousDataChangeCommissioningService.cs"));
        Assert.Contains("triggerGeneralInterrogation: false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("triggerGeneralInterrogation: true", source, StringComparison.Ordinal);
        Assert.Contains("GIrequested=false", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AR_G25A_", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PostLeaseGate_AcceptsStrictDchgOnlyCallerOwnedState()
    {
        var snapshot = Snapshot("0240", "061800", owner: "C0A851F0");

        var ok = DynamicReportSpontaneousDataChangeCommissioningService.IsPostLeaseUrcbSafeForDchg(
            snapshot,
            "192.168.81.240",
            out var reason);

        Assert.True(ok, reason);
        Assert.Contains("dchg-only", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0244")] // dchg + GI
    [InlineData("0248")] // dchg + integrity
    [InlineData("0260")] // dchg + qchg
    [InlineData("0250")] // dchg + dupd
    [InlineData("0204")] // GI only
    public void PostLeaseGate_RejectsAnyNonDchgOnlyTriggerShape(string trgOps)
    {
        var ok = DynamicReportSpontaneousDataChangeCommissioningService.IsPostLeaseUrcbSafeForDchg(
            Snapshot(trgOps, "061800", owner: "C0A851F0"),
            "192.168.81.240",
            out var reason);

        Assert.False(ok);
        Assert.Contains("not strict dchg-only", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostLeaseGate_RejectsWrongOwnerOrExternalUse()
    {
        var wrongOwner = Snapshot("0240", "061800", owner: "C0A851F0");
        var ok = DynamicReportSpontaneousDataChangeCommissioningService.IsPostLeaseUrcbSafeForDchg(
            wrongOwner,
            "192.168.81.241",
            out var reason);
        Assert.False(ok);
        Assert.Contains("Owner does not match", reason, StringComparison.OrdinalIgnoreCase);

        var external = Snapshot("0240", "061800", owner: "C0A851F0", availability: ArMms.MmsRcbOperationalAvailability.InUse);
        ok = DynamicReportSpontaneousDataChangeCommissioningService.IsPostLeaseUrcbSafeForDchg(
            external,
            "192.168.81.240",
            out reason);
        Assert.False(ok);
        Assert.Contains("UsedByCaller", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpontaneousGate_AcceptsPartialIncludedMemberWithDataChangeReason()
    {
        var refs = Refs();
        var members = Members();
        var frame = Frame(
            "R1",
            "LD0/LLN0.AR_G25A_TEST",
            [members[1]],
            [1],
            ["data-change"]);

        var result = DynamicReportSpontaneousDataChangeCommissioningService.ValidateSpontaneousDataChangeFrame(
            frame,
            "R1",
            "LD0/LLN0.AR_G25A_TEST",
            refs);

        Assert.True(result.IsSuccess, result.Reason);
        Assert.Equal([1], result.IncludedIndexes);
        Assert.Equal([refs[1]], result.IncludedMemberReferences);
        Assert.Contains("data-change", result.Reasons, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpontaneousGate_AcceptsMultipleCorrectDataChangeMembersWithoutRequiringEightOfEight()
    {
        var refs = Refs();
        var members = Members();
        var frame = Frame(
            "R1",
            "LD0/LLN0.AR_G25A_TEST",
            [members[0], members[2]],
            [0, 2],
            ["data-change"]);

        var result = DynamicReportSpontaneousDataChangeCommissioningService.ValidateSpontaneousDataChangeFrame(
            frame,
            "R1",
            "LD0/LLN0.AR_G25A_TEST",
            refs);

        Assert.True(result.IsSuccess, result.Reason);
        Assert.Equal([0, 2], result.IncludedIndexes);
    }

    [Theory]
    [InlineData("general-interrogation")]
    [InlineData("integrity")]
    [InlineData("quality-change")]
    [InlineData("data-update")]
    public void SpontaneousGate_RejectsNonDchgReasons(string reasonForInclusion)
    {
        var refs = Refs();
        var members = Members();
        var frame = Frame("R1", "LD0/LLN0.AR_G25A_TEST", [members[0]], [0], [reasonForInclusion]);

        var result = DynamicReportSpontaneousDataChangeCommissioningService.ValidateSpontaneousDataChangeFrame(
            frame,
            "R1",
            "LD0/LLN0.AR_G25A_TEST",
            refs);

        Assert.False(result.IsSuccess);
        Assert.Contains("does not carry", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpontaneousGate_RejectsMixedDataChangeAndGiReason()
    {
        var refs = Refs();
        var members = Members();
        var frame = Frame("R1", "LD0/LLN0.AR_G25A_TEST", [members[0]], [0], ["data-change", "general-interrogation"]);

        var result = DynamicReportSpontaneousDataChangeCommissioningService.ValidateSpontaneousDataChangeFrame(
            frame,
            "R1",
            "LD0/LLN0.AR_G25A_TEST",
            refs);

        Assert.False(result.IsSuccess);
        Assert.Contains("non-dchg reason", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SpontaneousGate_RejectsWrongIdentityIndexMemberAndFailedValue()
    {
        var refs = Refs();
        var members = Members();
        var valid = Frame("R1", "LD0/LLN0.AR_G25A_TEST", [members[1]], [1], ["data-change"]);

        Assert.False(DynamicReportSpontaneousDataChangeCommissioningService.ValidateSpontaneousDataChangeFrame(valid, "OTHER", "LD0/LLN0.AR_G25A_TEST", refs).IsSuccess);
        Assert.False(DynamicReportSpontaneousDataChangeCommissioningService.ValidateSpontaneousDataChangeFrame(valid, "R1", "LD0/LLN0.OTHER", refs).IsSuccess);

        var wrongIndex = Frame("R1", "LD0/LLN0.AR_G25A_TEST", [members[0]], [1], ["data-change"]);
        Assert.False(DynamicReportSpontaneousDataChangeCommissioningService.ValidateSpontaneousDataChangeFrame(wrongIndex, "R1", "LD0/LLN0.AR_G25A_TEST", refs).IsSuccess);

        var failed = new ArMms.MmsReportFrame
        {
            Header = new ArMms.MmsReportHeader { ReportId = "R1", DataSetReference = "LD0/LLN0.AR_G25A_TEST" },
            DecoderMode = "optflds-driven",
            IncludedDataSetIndexes = [0],
            Values =
            [
                new ArMms.MmsReportValue
                {
                    Index = 0,
                    Member = members[0],
                    FailureCode = 3,
                    ReasonForInclusion = ["data-change"]
                }
            ]
        };
        Assert.False(DynamicReportSpontaneousDataChangeCommissioningService.ValidateSpontaneousDataChangeFrame(failed, "R1", "LD0/LLN0.AR_G25A_TEST", refs).IsSuccess);
    }

    [Fact]
    public void G25A_SourceRequiresFreshCleanupClosureAndDoesNotTouchProductionPolicy()
    {
        var service = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "DynamicReportSpontaneousDataChangeCommissioningService.cs"));
        var ui = File.ReadAllText(Path.Combine(RepoRoot(), "DynamicReportQualificationUiBehavior.cs"));
        var runtime = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "Iec61850MonitorRuntime.cs"));

        Assert.Contains("ProveFreshCleanupClosureAsync", service, StringComparison.Ordinal);
        Assert.Contains("IsFreshCleanupClosed", service, StringComparison.Ordinal);
        Assert.Contains("IsTemporaryDataSetAbsentFromNameList", service, StringComparison.Ordinal);
        Assert.Contains("FreshCleanupClosureSucceeded", service, StringComparison.Ordinal);
        Assert.Contains("e.Key != Key.D", ui, StringComparison.Ordinal);
        Assert.Contains("RunG25ASpontaneousDataChangeAsync", ui, StringComparison.Ordinal);
        Assert.Contains("G2.5-A ARMED — NO GI", ui, StringComparison.Ordinal);
        Assert.Contains("Do NOT manually edit any RCB or DataSet", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicBrcb = true", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicUrcb = true", runtime, StringComparison.Ordinal);
    }

    private static string[] Refs()
        =>
        [
            "LD0/GGIO1$ST$Ind1$stVal",
            "LD0/GGIO1$ST$Ind2$stVal",
            "LD0/GGIO1$ST$Ind3$stVal"
        ];

    private static ArMms.MmsDataSetDirectoryMember[] Members()
        =>
        [
            Member("LD0", "GGIO1$ST$Ind1$stVal", "Ind1.stVal"),
            Member("LD0", "GGIO1$ST$Ind2$stVal", "Ind2.stVal"),
            Member("LD0", "GGIO1$ST$Ind3$stVal", "Ind3.stVal")
        ];

    private static ArMms.MmsDataSetDirectoryMember Member(string domain, string item, string path)
        => new()
        {
            Domain = domain,
            MmsItemName = item,
            UserReference = $"{domain}/{item}",
            FunctionalConstraint = "ST",
            LogicalNode = "GGIO1",
            DataObjectPath = path
        };

    private static ArMms.MmsReportFrame Frame(
        string reportId,
        string dataSet,
        IReadOnlyList<ArMms.MmsDataSetDirectoryMember> members,
        IReadOnlyList<int> included,
        IReadOnlyList<string> reasons)
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
                ReasonForInclusion = reasons
            }).ToArray()
        };

    private static ArMms.MmsRcbAvailabilitySnapshot Snapshot(
        string trgOps,
        string optFlds,
        string owner = "",
        ArMms.MmsRcbOperationalAvailability availability = ArMms.MmsRcbOperationalAvailability.UsedByCaller)
        => new()
        {
            Reference = "LD0/LLN0.RP01",
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "RP01",
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = ArMms.MmsRcbDataSetProbeState.ReadSucceeded,
            ReportId = "R1",
            TriggerOptions = trgOps,
            OptionalFields = optFlds,
            EnabledState = "false",
            ReservationState = "true",
            ReservationTimeSeconds = "0",
            Owner = owner,
            Availability = availability,
            Confidence = ArMms.MmsRcbAvailabilityConfidence.Exact,
            Attributes = ["DatSet", "RptID", "RptEna", "Resv", "TrgOps", "OptFlds", "GI"]
        };

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ArIED61850Tester.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("ARSAS repository root not found.");
    }
}
