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
    public void DiscoveryOnlyValuesNeverMasqueradeAsCurrentRcbState()
    {
        var evidence = RcbInspectionEvidence.FromDiscovery(Discovery(), null);
        foreach (var name in new[]
        {
            "Enabled / RptEna", "Reservation", "Reserve time (s)"
        })
        {
            var field = Property(evidence, true, name);
            Assert.Equal("Not verified", field.Value);
            Assert.Contains("exact read-only RCB check", field.Source,
                StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal("Not verified", Property(fromScl, true, "Enabled / RptEna").Value);
        Assert.Equal("Not verified", Property(fromScl, true, "Reservation").Value);
    }

    [Fact]
    public void UnknownSentinelsNeverClaimRcbDiscoveryState()
    {
        var evidence = RcbInspectionEvidence.FromDiscovery(
            Discovery("Unknown", "Not read", "—"), null);
        Assert.Equal("Not verified", Property(evidence, true, "Enabled / RptEna").Value);
        Assert.Equal("Not verified", Property(evidence, true, "Reservation").Value);
        Assert.Equal("Not verified", Property(evidence, true, "Reserve time (s)").Value);
    }

    [Fact]
    public void PreActivationFalseCannotOverwriteRcbStateAfterSessionStarts()
    {
        var model = RcbInspectionEvidence.FromDiscovery(
            Discovery("false", "false", "0"), null);
        Assert.Equal("Not verified", Property(model, true, "Enabled / RptEna").Value);
        // The actual RptEna might have been enabled by the monitor. Only a
        // fresh exact read-only MMS inspection may make a true/false claim.
        var checkedState = RcbInspectionEvidence.FromDiscovery(
            Discovery("false", "false", "0"),
            new MmsRcbAvailabilitySnapshot { EnabledState = "true" });
        var field = Property(checkedState, true, "Enabled / RptEna");
        Assert.Equal("true", field.Value);
        Assert.Equal("Read-only MMS availability snapshot", field.Source);
        Assert.Equal("—", Property(checkedState, false, "Enabled / RptEna").Value);
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
