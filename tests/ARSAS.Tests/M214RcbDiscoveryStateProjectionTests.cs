using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214RcbDiscoveryStateProjectionTests
{
    private static readonly RcbExportRow Row = new()
    {
        Name = "urcb01", Reference = "LD/LLN0.RP.urcb01",
        DataSetReference = "LD/LLN0.Signals"
    };

    private static LiveIedReportControlModel Discovery(
        string enabled = "true", string reservation = "false",
        string reserveTime = "0") => new()
    {
        Reference = Row.Reference,
        Name = Row.Name,
        EnabledState = enabled, ReservationState = reservation,
        ReservationTimeSeconds = reserveTime
    };

    private static RcbInspectionProperty Property(
        RcbInspectionEvidence evidence, bool connected, string field)
        => Assert.Single(evidence.Properties(Row, connected),
            property => property.Property == field);

    [Fact]
    public void DiscoveryValuesAppearAsHistorical_NotAsCurrentReadOnlyCheck()
    {
        var evidence = RcbInspectionEvidence.FromDiscovery(Discovery(), null);
        foreach (var (name, value) in new[]
        {
            ("Enabled / RptEna", "true"),
            ("Reservation", "false"),
            ("Reserve time (s)", "0")
        })
        {
            var field = Property(evidence, true, name);
            Assert.Equal(value, field.Value);
            Assert.Contains("historical", field.Source, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("no current MMS state check", evidence.Summary(true));
    }

    [Fact]
    public void NoLiveStateIsProjectedAfterDisconnect_OrFromSclOnly()
    {
        var saved = RcbInspectionEvidence.FromDiscovery(Discovery(), null);
        Assert.Equal("—", Property(saved, false, "Enabled / RptEna").Value);
        Assert.Equal("—", Property(saved, false, "Reservation").Value);
        Assert.Equal("—", Property(saved, false, "Reserve time (s)").Value);

        var fromScl = RcbInspectionEvidence.FromSource("Rpt01", null);
        Assert.Equal("—", Property(fromScl, true, "Enabled / RptEna").Value);
        Assert.Equal("—", Property(fromScl, true, "Reservation").Value);
    }

    [Fact]
    public void UnknownSentinelsNeverClaimRcbDiscoveryState()
    {
        var evidence = RcbInspectionEvidence.FromDiscovery(
            Discovery("Unknown", "Not read", "—"), null);
        Assert.Equal("—", Property(evidence, true, "Enabled / RptEna").Value);
        Assert.Equal("—", Property(evidence, true, "Reservation").Value);
        Assert.Equal("—", Property(evidence, true, "Reserve time (s)").Value);
    }

    [Fact]
    public void ExplicitSnapshotWinsOverContradictoryDiscovery()
    {
        var snapshot = new MmsRcbAvailabilitySnapshot
        {
            EnabledState = "false", ReservationState = "true",
            ReservationTimeSeconds = "12"
        };
        var evidence = RcbInspectionEvidence.FromDiscovery(
            Discovery("true", "false", "0"), snapshot);
        foreach (var (name, expected) in new[]
        {
            ("Enabled / RptEna", "false"),
            ("Reservation", "true"),
            ("Reserve time (s)", "12")
        })
        {
            var field = Property(evidence, true, name);
            Assert.Equal(expected, field.Value);
            Assert.Equal("Read-only MMS availability snapshot", field.Source);
        }
    }
}
