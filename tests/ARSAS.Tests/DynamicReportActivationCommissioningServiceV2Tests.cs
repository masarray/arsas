using ArIED61850Tester.Services;
using ArMms = AR.Iec61850.Mms;

namespace ARSAS.Tests;

public sealed class DynamicReportActivationCommissioningServiceV2Tests
{
    [Fact]
    public void FreshSelection_UsesForcedLiveSnapshot_WhenDiscoveryProbeFlagWasNotSet()
    {
        var inventory = Inventory(Candidate("LD0/LLN0.RP01"));
        var availability = Availability(Snapshot("LD0/LLN0.RP01", "LD0"));

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
    public void FreshSelection_RejectsUnreadDatSet()
    {
        var inventory = Inventory(Candidate("LD0/LLN0.RP01"));
        var availability = Availability(Snapshot(
            "LD0/LLN0.RP01",
            "LD0",
            ArMms.MmsRcbDataSetProbeState.ReadFailed,
            "dchg gi"));

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
    public void FreshSelection_PrefersSameLogicalDevice()
    {
        var inventory = Inventory(
            Candidate("LD_OTHER/LLN0.RP01"),
            Candidate("LD_TARGET/LLN0.RP02"));
        var availability = Availability(
            Snapshot("LD_OTHER/LLN0.RP01", "LD_OTHER"),
            Snapshot("LD_TARGET/LLN0.RP02", "LD_TARGET"));

        var selected = DynamicReportActivationCommissioningServiceV2.SelectQualifiedUrcbFromFreshAvailability(
            availability,
            inventory,
            "LD_TARGET",
            out _,
            out _,
            out _);

        Assert.NotNull(selected);
        Assert.Equal("LD_TARGET/LLN0.RP02", selected!.Reference);
    }

    [Fact]
    public void FreshSelection_ExplainsMissingGiOnOtherwiseEmptyUrcb()
    {
        var inventory = Inventory(Candidate("LD0/LLN0.RP01"));
        var availability = Availability(Snapshot(
            "LD0/LLN0.RP01",
            "LD0",
            ArMms.MmsRcbDataSetProbeState.ReadSucceeded,
            "dchg"));

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

    private static ArMms.MmsReportInventory Inventory(params ArMms.MmsReportControlCandidate[] candidates)
    {
        var inventory = new ArMms.MmsReportInventory();
        foreach (var candidate in candidates)
            inventory.ReportControls.Add(candidate);
        return inventory;
    }

    private static ArMms.MmsRcbAvailabilityResult Availability(params ArMms.MmsRcbAvailabilitySnapshot[] snapshots)
        => new() { ReportControls = snapshots };

    private static ArMms.MmsReportControlCandidate Candidate(string reference)
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
            DataSetProbeState = ArMms.MmsRcbDataSetProbeState.NotAttempted,
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

    private static ArMms.MmsRcbAvailabilitySnapshot Snapshot(
        string reference,
        string domain,
        ArMms.MmsRcbDataSetProbeState probeState = ArMms.MmsRcbDataSetProbeState.ReadSucceeded,
        string triggerOptions = "dchg gi")
        => new()
        {
            Reference = reference,
            Domain = domain,
            LogicalNode = "LLN0",
            Name = reference[(reference.LastIndexOf('.') + 1)..],
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = probeState,
            DataSetProbeMessage = probeState == ArMms.MmsRcbDataSetProbeState.ReadSucceeded
                ? "DatSet item: OK"
                : "DatSet read rejected",
            ReportId = "RPT-01",
            TriggerOptions = triggerOptions,
            OptionalFields = "data-set-name reason-for-inclusion",
            EnabledState = "false",
            ReservationState = "false",
            ReservationTimeSeconds = "0",
            Owner = string.Empty,
            Availability = probeState == ArMms.MmsRcbDataSetProbeState.ReadSucceeded
                ? ArMms.MmsRcbOperationalAvailability.NoDataSet
                : ArMms.MmsRcbOperationalAvailability.Unknown,
            Confidence = probeState == ArMms.MmsRcbDataSetProbeState.ReadSucceeded
                ? ArMms.MmsRcbAvailabilityConfidence.Exact
                : ArMms.MmsRcbAvailabilityConfidence.Reduced,
            Attributes = ["DatSet", "RptID", "RptEna", "Resv", "TrgOps", "OptFlds", "GI"]
        };
}
