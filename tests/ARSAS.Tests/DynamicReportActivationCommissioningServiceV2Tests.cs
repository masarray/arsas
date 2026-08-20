using ArIED61850Tester.Services;
using ArMms = AR.Iec61850.Mms;

namespace ARSAS.Tests;

public sealed class DynamicReportActivationCommissioningServiceV2Tests
{
    [Fact]
    public void FreshSelection_UsesForcedLiveSnapshot_WhenDiscoveryProbeFlagWasNotSet()
    {
        var inventory = new ArMms.MmsReportInventory();
        inventory.ReportControls.Add(Candidate(
            "LD0/LLN0.RP01",
            dataSetProbeState: ArMms.MmsRcbDataSetProbeState.NotAttempted));

        var availability = new ArMms.MmsRcbAvailabilityResult
        {
            ReportControls = [SafeSnapshot("LD0/LLN0.RP01", "LD0")]
        };

        var selected = DynamicReportActivationCommissioningServiceV2.SelectQualifiedUrcbFromFreshAvailability(
            availability,
            inventory,
            "LD0",
            out var snapshot,
            out var reason,
            out _);

        Assert.NotNull(selected);
        Assert.NotNull(snapshot);
        Assert.Equal("LD0/LLN0.RP01", selected!.Reference);
        Assert.Equal(ArMms.MmsRcbDataSetProbeState.ReadSucceeded, snapshot!.DataSetProbeState);
        Assert.Contains("forcedLiveProbe=ReadSucceeded", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshSelection_RejectsUnreadDatSet_EvenWhenDiscoveryReferenceIsBlank()
    {
        var inventory = new ArMms.MmsReportInventory();
        inventory.ReportControls.Add(Candidate("LD0/LLN0.RP01", ArMms.MmsRcbDataSetProbeState.NotAttempted));

        var unread = SafeSnapshot("LD0/LLN0.RP01", "LD0") with
        {
            DataSetProbeState = ArMms.MmsRcbDataSetProbeState.ReadFailed,
            DataSetProbeMessage = "DatSet read rejected"
        };
        var availability = new ArMms.MmsRcbAvailabilityResult { ReportControls = [unread] };

        var selected = DynamicReportActivationCommissioningServiceV2.SelectQualifiedUrcbFromFreshAvailability(
            availability,
            inventory,
            "LD0",
            out var snapshot,
            out var reason,
            out var diagnostics);

        Assert.Null(selected);
        Assert.Null(snapshot);
        Assert.Contains("strictProofEligible=0", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(diagnostics, line => line.Contains("probe=ReadFailed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FreshSelection_PrefersSameLogicalDevice_WhenMultipleSafeUrcbsExist()
    {
        var inventory = new ArMms.MmsReportInventory();
        inventory.ReportControls.Add(Candidate("LD_OTHER/LLN0.RP01", ArMms.MmsRcbDataSetProbeState.NotAttempted));
        inventory.ReportControls.Add(Candidate("LD_TARGET/LLN0.RP02", ArMms.MmsRcbDataSetProbeState.NotAttempted));

        var availability = new ArMms.MmsRcbAvailabilityResult
        {
            ReportControls =
            [
                SafeSnapshot("LD_OTHER/LLN0.RP01", "LD_OTHER"),
                SafeSnapshot("LD_TARGET/LLN0.RP02", "LD_TARGET")
            ]
        };

        var selected = DynamicReportActivationCommissioningServiceV2.SelectQualifiedUrcbFromFreshAvailability(
            availability,
            inventory,
            "LD_TARGET",
            out var snapshot,
            out _,
            out _);

        Assert.NotNull(selected);
        Assert.NotNull(snapshot);
        Assert.Equal("LD_TARGET/LLN0.RP02", selected!.Reference);
    }

    [Fact]
    public void FreshSelection_ReportsWhyStrictIdentityFieldsBlockAnOtherwiseEmptyUrcb()
    {
        var inventory = new ArMms.MmsReportInventory();
        inventory.ReportControls.Add(Candidate("LD0/LLN0.RP01", ArMms.MmsRcbDataSetProbeState.NotAttempted));

        var missingGi = SafeSnapshot("LD0/LLN0.RP01", "LD0") with
        {
            TriggerOptions = "dchg"
        };
        var availability = new ArMms.MmsRcbAvailabilityResult { ReportControls = [missingGi] };

        var selected = DynamicReportActivationCommissioningServiceV2.SelectQualifiedUrcbFromFreshAvailability(
            availability,
            inventory,
            "LD0",
            out _,
            out var reason,
            out var diagnostics);

        Assert.Null(selected);
        Assert.Contains("forced-live empty DatSet=1", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(diagnostics, line => line.Contains("strict G2.4 report identity fields", StringComparison.OrdinalIgnoreCase));
    }

    private static ArMms.MmsReportControlCandidate Candidate(
        string reference,
        ArMms.MmsRcbDataSetProbeState dataSetProbeState)
    {
        var slash = reference.IndexOf('/');
        var dot = reference.IndexOf('.', slash + 1);
        return new ArMms.MmsReportControlCandidate
        {
            Domain = reference[..slash],
            LogicalNode = reference[(slash + 1)..dot],
            Name = reference[(dot + 1)..],
            Reference = reference,
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = dataSetProbeState,
            ReportId = "RPT-01",
            TriggerOptions = "dchg gi",
            OptionalFields = "data-set-name reason-for-inclusion",
            EnabledState = "false",
            ReservationState = "false",
            ReservationTimeSeconds = "0",
            Status = "Attribute-probed",
            Attributes = ["DatSet", "RptID", "RptEna", "Resv", "TrgOps", "OptFlds", "GI"]
        };
    }

    private static ArMms.MmsRcbAvailabilitySnapshot SafeSnapshot(string reference, string domain)
        => new()
        {
            Reference = reference,
            Domain = domain,
            LogicalNode = "LLN0",
            Name = reference[(reference.LastIndexOf('.') + 1)..],
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = ArMms.MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "DatSet item: OK",
            ReportId = "RPT-01",
            TriggerOptions = "dchg gi",
            OptionalFields = "data-set-name reason-for-inclusion",
            EnabledState = "false",
            ReservationState = "false",
            ReservationTimeSeconds = "0",
            Owner = string.Empty,
            Availability = ArMms.MmsRcbOperationalAvailability.NoDataSet,
            Confidence = ArMms.MmsRcbAvailabilityConfidence.Exact,
            Attributes = ["DatSet", "RptID", "RptEna", "Resv", "TrgOps", "OptFlds", "GI"]
        };
}
