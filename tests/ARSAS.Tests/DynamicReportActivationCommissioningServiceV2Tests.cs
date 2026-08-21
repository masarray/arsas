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
        Assert.Contains("transactionalProofFields=true", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshSelection_RejectsUnreadDatSet()
    {
        var inventory = Inventory(Candidate("LD0/LLN0.RP01"));
        var availability = Availability(Snapshot(
            "LD0/LLN0.RP01",
            "LD0",
            probeState: ArMms.MmsRcbDataSetProbeState.ReadFailed));

        var selected = DynamicReportActivationCommissioningServiceV2.SelectQualifiedUrcbFromFreshAvailability(
            availability,
            inventory,
            "LD0",
            out var snapshot,
            out var reason,
            out var diagnostics);

        Assert.Null(selected);
        Assert.Null(snapshot);
        Assert.Contains("transactionalLeaseEligible=0", reason, StringComparison.OrdinalIgnoreCase);
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
    public void FreshSelection_FieldObserved0204_060000_IsLeaseEligible()
    {
        var inventory = Inventory(Candidate("LD0/LLN0.RP01"));
        var availability = Availability(Snapshot(
            "LD0/LLN0.RP01",
            "LD0",
            triggerOptions: "0204",
            optionalFields: "060000"));

        var selected = DynamicReportActivationCommissioningServiceV2.SelectQualifiedUrcbFromFreshAvailability(
            availability,
            inventory,
            "LD0",
            out var snapshot,
            out var reason,
            out var diagnostics);

        Assert.NotNull(selected);
        Assert.NotNull(snapshot);
        Assert.Equal("0204", snapshot!.TriggerOptions);
        Assert.Equal("060000", snapshot.OptionalFields);
        Assert.Contains("transactionalProofFields=true", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(diagnostics, line => line.Contains("leaseable=True", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LeaseGate_RejectsMissingRptId()
    {
        var snapshot = Snapshot("LD0/LLN0.RP01", "LD0", reportId: string.Empty);

        var safe = DynamicReportActivationCommissioningServiceV2.IsLeaseableFreeUrcbForG24(snapshot, out var reason);

        Assert.False(safe);
        Assert.Contains("RptID is empty", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LeaseGate_RejectsReservedUrcb()
    {
        var snapshot = Snapshot("LD0/LLN0.RP01", "LD0", reservationState: "true");

        var safe = DynamicReportActivationCommissioningServiceV2.IsLeaseableFreeUrcbForG24(snapshot, out var reason);

        Assert.False(safe);
        Assert.Contains("Resv is not explicit false", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LeaseGate_RejectsNonEmptyOwner()
    {
        var snapshot = Snapshot("LD0/LLN0.RP01", "LD0", owner: "01020304");

        var safe = DynamicReportActivationCommissioningServiceV2.IsLeaseableFreeUrcbForG24(snapshot, out var reason);

        Assert.False(safe);
        Assert.Contains("Owner is non-empty", reason, StringComparison.OrdinalIgnoreCase);
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
            TriggerOptions = "0204",
            OptionalFields = "060000",
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
        string triggerOptions = "0204",
        string optionalFields = "060000",
        string reportId = "RPT-01",
        string reservationState = "false",
        string owner = "")
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
            ReportId = reportId,
            TriggerOptions = triggerOptions,
            OptionalFields = optionalFields,
            EnabledState = "false",
            ReservationState = reservationState,
            ReservationTimeSeconds = "0",
            Owner = owner,
            Availability = probeState == ArMms.MmsRcbDataSetProbeState.ReadSucceeded
                ? ArMms.MmsRcbOperationalAvailability.NoDataSet
                : ArMms.MmsRcbOperationalAvailability.Unknown,
            Confidence = probeState == ArMms.MmsRcbDataSetProbeState.ReadSucceeded
                ? ArMms.MmsRcbAvailabilityConfidence.Exact
                : ArMms.MmsRcbAvailabilityConfidence.Reduced,
            Attributes = ["DatSet", "RptID", "RptEna", "Resv", "TrgOps", "OptFlds", "GI"]
        };
}
