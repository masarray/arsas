using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>One real Report path is not zero traffic or dual-path parity.</summary>
public sealed class P0DSingleIngressReportEvidenceTests
{
    private static StaticAcquisitionIngressEvidence Ingress(
        StaticAcquisitionIngressKind source, bool routed)
        => new()
        {
            Ingress = source,
            IedName = "TESTBCU",
            IsComparable = true,
            RuntimeTargets = ["TESTLD/LLN0.BR.Buffer01"],
            RoutedReportTargets = routed ? ["TESTLD/LLN0.BR.Buffer01"] : [],
            FirstRoutedReportAtUtc = routed ? DateTimeOffset.UtcNow : null,
            PlannedReportCount = 1,
            RequestedPointCount = 18
        };

    [Fact]
    public void Routed_Discovery_Only_Is_Real_Ingress_But_Not_Equivalent_Scl_Parity()
    {
        var snapshot = new StaticAcquisitionParitySnapshot
        {
            Discovery = Ingress(StaticAcquisitionIngressKind.LiveDiscovery, true),
            Status = StaticAcquisitionParityStatus.AwaitingOtherIngress
        };
        Assert.False(snapshot.DualIngressTrafficProven);
        Assert.Contains("DISCOVERY REPORT INGRESS PROVEN",
            snapshot.TrafficQualificationSummary);
        Assert.Contains("Open SCL parity not captured",
            snapshot.TrafficQualificationSummary);
        Assert.DoesNotContain("DUAL INGRESS TRAFFIC PROVEN",
            snapshot.TrafficQualificationSummary);
    }

    [Fact]
    public void Static_Plan_Without_Received_Ingress_Stays_Pending()
    {
        var snapshot = new StaticAcquisitionParitySnapshot
        {
            Discovery = Ingress(StaticAcquisitionIngressKind.LiveDiscovery, false),
            Status = StaticAcquisitionParityStatus.AwaitingOtherIngress
        };
        Assert.False(snapshot.DualIngressTrafficProven);
        Assert.Contains("DUAL INGRESS PARITY PENDING",
            snapshot.TrafficQualificationSummary);
        Assert.DoesNotContain("PROVEN", snapshot.TrafficQualificationSummary);
    }

    [Fact]
    public void Both_Paths_Routed_Only_Qualify_When_Semantic_Parity_Matches()
    {
        var discovery = Ingress(StaticAcquisitionIngressKind.LiveDiscovery, true);
        var scl = Ingress(StaticAcquisitionIngressKind.OpenScl, true);
        var match = new StaticAcquisitionParitySnapshot
        {
            Discovery = discovery,
            OpenScl = scl,
            Status = StaticAcquisitionParityStatus.Equivalent
        };
        Assert.True(match.DualIngressTrafficProven);
        Assert.Contains("DUAL INGRESS TRAFFIC PROVEN",
            match.TrafficQualificationSummary);

        var mismatch = new StaticAcquisitionParitySnapshot
        {
            Discovery = discovery,
            OpenScl = scl,
            Status = StaticAcquisitionParityStatus.Mismatch
        };
        Assert.False(mismatch.DualIngressTrafficProven);
        Assert.Contains("DUAL INGRESS PARITY PENDING",
            mismatch.TrafficQualificationSummary);
    }
}
