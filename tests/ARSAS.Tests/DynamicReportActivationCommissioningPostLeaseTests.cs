using ArIED61850Tester.Services;
using ArMms = AR.Iec61850.Mms;

namespace ARSAS.Tests;

public sealed class DynamicReportActivationCommissioningPostLeaseTests
{
    [Fact]
    public void PostLeaseAvailabilityOptions_MarkOnlyExactSelectedUrcbCallerOwned()
    {
        const string selected = "LD0/LLN0.RP01";

        var options = DynamicReportActivationCommissioningServiceV2.BuildPostLeaseAvailabilityOptions(selected);

        Assert.Equal(1, options.MaxReportControls);
        Assert.False(options.ReadDataSetDirectories);
        Assert.Single(options.CallerOwnedRcbReferences);
        Assert.Contains(selected, options.CallerOwnedRcbReferences, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostLeaseGate_AcceptsSameAssociationAutoReservation()
    {
        var snapshot = Snapshot(
            reservationState: "true",
            availability: ArMms.MmsRcbOperationalAvailability.UsedByCaller,
            triggerOptions: "0244",
            optionalFields: "061800");

        var safe = DynamicReportActivationCommissioningServiceV2.IsPostLeaseUrcbSafeForG24(snapshot, out var reason);

        Assert.True(safe, reason);
        Assert.Contains("caller-owned", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Resv=true", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostLeaseGate_RejectsReservedUrcbNotCallerOwned()
    {
        var snapshot = Snapshot(
            reservationState: "true",
            availability: ArMms.MmsRcbOperationalAvailability.InUse,
            triggerOptions: "0244",
            optionalFields: "061800");

        var safe = DynamicReportActivationCommissioningServiceV2.IsPostLeaseUrcbSafeForG24(snapshot, out var reason);

        Assert.False(safe);
        Assert.Contains("ownership is not proven", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostLeaseGate_RejectsMissingStrictIdentityBits()
    {
        var snapshot = Snapshot(
            reservationState: "true",
            availability: ArMms.MmsRcbOperationalAvailability.UsedByCaller,
            triggerOptions: "0204",
            optionalFields: "060000");

        var safe = DynamicReportActivationCommissioningServiceV2.IsPostLeaseUrcbSafeForG24(snapshot, out var reason);

        Assert.False(safe);
        Assert.Contains("strict G2.4 report identity fields", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostLeaseGate_RejectsNonEmptyOwnerEvenWhenMarkedCallerOwned()
    {
        var snapshot = Snapshot(
            reservationState: "true",
            availability: ArMms.MmsRcbOperationalAvailability.UsedByCaller,
            triggerOptions: "0244",
            optionalFields: "061800",
            owner: "01020304");

        var safe = DynamicReportActivationCommissioningServiceV2.IsPostLeaseUrcbSafeForG24(snapshot, out var reason);

        Assert.False(safe);
        Assert.Contains("Owner is non-empty", reason, StringComparison.OrdinalIgnoreCase);
    }

    private static ArMms.MmsRcbAvailabilitySnapshot Snapshot(
        string reservationState,
        ArMms.MmsRcbOperationalAvailability availability,
        string triggerOptions,
        string optionalFields,
        string owner = "")
        => new()
        {
            Reference = "LD0/LLN0.RP01",
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "RP01",
            Buffered = false,
            DataSetReference = string.Empty,
            DataSetProbeState = ArMms.MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "DatSet item: OK",
            ReportId = "RPT-01",
            TriggerOptions = triggerOptions,
            OptionalFields = optionalFields,
            EnabledState = "false",
            ReservationState = reservationState,
            ReservationTimeSeconds = "0",
            Owner = owner,
            Availability = availability,
            Confidence = ArMms.MmsRcbAvailabilityConfidence.Exact,
            Attributes = ["DatSet", "RptID", "RptEna", "Resv", "TrgOps", "OptFlds", "GI"]
        };
}
