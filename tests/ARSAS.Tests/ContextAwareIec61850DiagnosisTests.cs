using System.Net;
using System.Text;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Deterministic IEC-aware decisions: no live NIC, IED, sockets or simulator.
/// Hardware/network routing and physical SOE completeness are separate tests.
/// </summary>
public sealed class ContextAwareIec61850DiagnosisTests
{
    private static readonly IPAddress Bcuge = IPAddress.Parse("192.16.1.33");

    [Fact]
    public void SamePhysicalInterfaceWithFiveAliases_DoesNotClaimFiveCompetingNics()
    {
        var addresses = Enumerable.Range(1, 5)
            .Select(_ => (InterfaceId: "KM-TEST-GUID", AdapterName: "KM_Test")).ToArray();
        var description = DiagnosticReportBuilder.ExplainIpv4Route(Bcuge, addresses, false);
        Assert.Contains("One network interface (KM_Test)", description);
        Assert.Contains("5 IPv4 address(es)", description);
        Assert.Contains("not competing adapters", description);
        Assert.DoesNotContain("distinct network interfaces", description);
    }

    [Fact]
    public void DeviceIpIsLocalSimulatorEndpoint_DoesNotRecommendDisablingInterfaces()
    {
        var description = DiagnosticReportBuilder.ExplainIpv4Route(
            Bcuge,
            new[] { (InterfaceId: "KM-TEST-GUID", AdapterName: "KM_Test") },
            targetAssignedLocally: true);
        Assert.Contains("assigned to this PC", description);
        Assert.Contains("same-host IED simulator", description);
        Assert.DoesNotContain("Disable", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("physically connected", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DifferentInterfacesWithSameFriendlyName_AreStillARealRoutingAmbiguity()
    {
        var description = DiagnosticReportBuilder.ExplainIpv4Route(
            Bcuge,
            new[]
            {
                (InterfaceId: "guid-1", AdapterName: "Ethernet"),
                (InterfaceId: "guid-2", AdapterName: "Ethernet"),
                (InterfaceId: "GUID-1", AdapterName: "Ethernet")
            },
            targetAssignedLocally: false);
        Assert.Contains("2 distinct network interfaces", description);
        Assert.Contains("routing table", description);
    }

    [Fact]
    public void NoDirectSubnetMatch_DoesNotClaimGatewayRoutingImpossible()
    {
        var description = DiagnosticReportBuilder.ExplainIpv4Route(
            Bcuge, Array.Empty<(string InterfaceId, string AdapterName)>(), false);
        Assert.Contains("gateway or static route can still provide connectivity", description);
        Assert.DoesNotContain("unreachable", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrcbLateRecurrence_IsNotWronglyExplainedAsStartupOnlyOrGiReset()
    {
        var activation = new DateTimeOffset(2026, 10, 12, 0, 26, 4, TimeSpan.Zero);
        var stream = new Iec61850ReportContinuityStreamSnapshot
        {
            Rcb = "BCUGEF650/LLN0.BR.BRB_CSWI_GW010101",
            Buffered = true,
            Frames = 55,
            Sequenced = 55,
            Findings = 22,
            SequenceDiscontinuities = 21,
            SequenceWithGiReason = 1,
            Overflow = 1,
            ActivationReturnedAtUtc = activation,
            LastAnomalyReceivedAtUtc = activation.AddSeconds(48.575)
        };
        var output = DiagnosticReportBuilder.ExplainBrcbContinuity(stream);
        Assert.Contains("GI-tagged=1, withoutConfirmedGI=20", output);
        Assert.Contains("BufOvflAdvertised=1", output);
        Assert.Contains("lastAnomalyAfterStartMs=48575", output);
        Assert.Contains("late recurrence observed beyond 5s", output);
        Assert.Contains("not TCP wire order", output);
        Assert.DoesNotContain("events were lost", output);

        // Must also surface in ordinary user diagnostic, not only in helpers.
        var builder = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(builder,
            new Iec61850ReportContinuitySnapshot
            {
                Frames = 55, Sequenced = 55, Findings = 22, Streams = new[] { stream }
            });
        Assert.Contains("BRCB context  :", builder.ToString());
        Assert.Contains("NOT startup-only", builder.ToString());
    }

    [Fact]
    public void StartupOnlyObservedFrames_AreNotPromotedToCausalCertainty()
    {
        var activation = DateTimeOffset.UtcNow;
        var stream = new Iec61850ReportContinuityStreamSnapshot
        {
            Buffered = true,
            Findings = 2,
            SequenceDiscontinuities = 2,
            SequenceWithGiReason = 0,
            ActivationReturnedAtUtc = activation,
            LastAnomalyReceivedAtUtc = activation.AddMilliseconds(250)
        };
        var description = DiagnosticReportBuilder.ExplainBrcbContinuity(stream);
        Assert.Contains("lastAnomalyAfterStartMs=250", description);
        Assert.DoesNotContain("late recurrence", description);
        Assert.Contains("not TCP wire order", description);
        Assert.Contains("withoutConfirmedGI=2", description);
    }

    [Fact]
    public void UnknownOrReversedTimestamps_DoNotFabricateLateRecurrence()
    {
        var now = DateTimeOffset.UtcNow;
        var noTiming = DiagnosticReportBuilder.ExplainBrcbContinuity(
            new Iec61850ReportContinuityStreamSnapshot
            {
                Buffered = true,
                SequenceDiscontinuities = 1
            });
        Assert.Contains("timing=UNKNOWN", noTiming);
        var reversed = DiagnosticReportBuilder.ExplainBrcbContinuity(
            new Iec61850ReportContinuityStreamSnapshot
            {
                Buffered = true,
                SequenceDiscontinuities = 1,
                ActivationReturnedAtUtc = now,
                LastAnomalyReceivedAtUtc = now.AddSeconds(-1)
            });
        Assert.Contains("timing=UNKNOWN", reversed);
        Assert.DoesNotContain("late recurrence", reversed);
    }
}
