using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M2IedObservationLedgerTests
{
    private const string St = "ST";
    private const string Position = "LD/CSWI1.Pos.stVal";

    [Fact]
    public void MonitoringPrefersRealReportEvenWhenManualReadFinishesLater()
    {
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(association, Position, St,
            "Open [01]", "Good", "2026-10-14T01:00:00Z", "Report"));
        Assert.True(values.PublishForAssociation(association, Position, St,
            "Close [10]", "Invalid", "2026-10-14T01:00:01Z", "MMS Read"));
        Assert.True(values.TryGet(Position, St, out var monitoring));
        Assert.Equal("Open [01]", monitoring.Value);
        Assert.Equal("Report", monitoring.Source);
        Assert.Equal("Good", monitoring.Quality);

        // After the monitor stops, the later manually requested read may be
        // shown as a read snapshot; it never rewrites the report channel.
        Assert.True(values.TryGet(Position, St, out var idle, preferReport: false));
        Assert.Equal("Close [10]", idle.Value);
        Assert.Equal("MMS Read", idle.Source);
        Assert.True(values.PublishForAssociation(association, Position, St,
            "Bad state [11]", "Questionable", "", "Report"));
        Assert.True(values.TryGet(Position, St, out var latest, preferReport: false));
        Assert.Equal("Bad state [11]", latest.Value);
    }

    [Fact]
    public void EarlierReadCannotOutrankLaterReportAndNoValueIsFabricated()
    {
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        Assert.False(values.TryGet(Position, St, out _));
        Assert.True(values.PublishForAssociation(association, Position, St,
            "Intermediate [00]", "", "", "MMS Read"));
        Assert.True(values.TryGet(Position, St, out var read));
        Assert.Equal("MMS Read", read.Source);
        Assert.Null(read.QualityBits);
        Assert.Null(read.TimeBits);
        Assert.True(values.PublishForAssociation(association, Position, St,
            "Open [01]", "", "", "Report"));
        Assert.True(values.TryGet(Position, St, out var report));
        Assert.Equal("Open [01]", report.Value);
        Assert.True(report.Revision > read.Revision);
        Assert.True(values.Version >= report.Revision);
    }

    [Fact]
    public void DisconnectMarksSnapshotStaleAndRejectsQueuedNetworkCallbacks()
    {
        var values = new IedBrowserLiveValues();
        var first = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(first, Position, St,
            "Open [01]", "", "", "Report"));
        values.MarkOffline();
        Assert.True(values.IsOffline);
        Assert.True(values.TryGet(Position, St, out var stale));
        Assert.True(stale.IsStale);
        Assert.Equal(first, stale.Association);
        Assert.False(values.PublishForAssociation(first, Position, St,
            "Close [10]", "", "", "Report"));
        Assert.False(values.PublishForAssociation(first, Position, St,
            "Close [10]", "", "", "MMS Read"));
        Assert.True(values.TryGet(Position, St, out var unchanged));
        Assert.Equal("Open [01]", unchanged.Value);

        var second = values.BeginAssociation();
        Assert.NotEqual(first, second);
        Assert.False(values.TryGet(Position, St, out _));
        Assert.False(values.PublishForAssociation(first, Position, St,
            "Bad state [11]", "", "", "Report"));
        Assert.True(values.PublishForAssociation(second, Position, St,
            "Close [10]", "", "", "Report"));
        Assert.True(values.TryGet(Position, St, out var fresh));
        Assert.False(fresh.IsStale);
        Assert.Equal(second, fresh.Association);
        Assert.Equal("Close [10]", fresh.Value);
    }

    [Fact]
    public void FccaseAndReferenceCaseDoNotCollapseDistinctSchemaMembers()
    {
        var values = new IedBrowserLiveValues();
        var assoc = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(assoc, Position, "st",
            "Open [01]", "", "", "Report"));
        Assert.True(values.PublishForAssociation(assoc, "LD/CSWI1.pos.stVal", "ST",
            "Close [10]", "", "", "Report"));
        Assert.True(values.PublishForAssociation(assoc, Position, "MX",
            "77.2", "", "", "MMS Read"));
        Assert.True(values.TryGet(Position, "ST", out var proper));
        Assert.Equal("Open [01]", proper.Value);
        Assert.True(values.TryGet("LD/CSWI1.pos.stVal", "st", out var distinct));
        Assert.Equal("Close [10]", distinct.Value);
        Assert.True(values.TryGet(Position, "MX", out var differentFc));
        Assert.Equal("77.2", differentFc.Value);
        Assert.False(values.TryGet("LD/cswi1.Pos.stVal", "ST", out _));
        Assert.False(values.TryGet(Position, "CF", out _));
        Assert.Equal("LD/CSWI1.Pos.stVal",
            IedBrowserLiveValues.CanonicalReference("LD/CSWI1$ST$Pos$stVal"));
    }

    [Fact]
    public void OnlySchemaNamedQualityLeavesCarryDecodedFlags()
    {
        var values = new IedBrowserLiveValues();
        var assoc = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(assoc, "LD/CSWI1.Pos.q", St,
            "bits(20A8,unused=3)", "", "", "Report quality"));
        Assert.True(values.TryGet("LD/CSWI1.Pos.q", St, out var quality));
        Assert.NotNull(quality.QualityBits);
        Assert.Equal("Good", quality.QualityBits.Validity);
        Assert.Equal("true", quality.QualityBits.FormatField("Overflow"));
        Assert.True(values.PublishForAssociation(assoc, "LD/GGIO1.Raw", St,
            "bits(20A8,unused=3)", "", "", "MMS Read"));
        Assert.True(values.TryGet("LD/GGIO1.Raw", St, out var raw));
        Assert.Null(raw.QualityBits);
        Assert.True(values.PublishForAssociation(assoc, "LD/CSWI1.Pos.q", St,
            "bits(40,unused=6)", "", "", "Report quality"));
        Assert.True(values.TryGet("LD/CSWI1.Pos.q", St, out var invalid));
        Assert.Null(invalid.QualityBits);
    }

    [Fact]
    public void QualityTimestampAndFcAreNotInheritedFromUnrelatedReferences()
    {
        var values = new IedBrowserLiveValues();
        var assoc = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(assoc, "LD/CSWI1.Pos.q", "ST",
            "Good", "", "", "Report quality"));
        Assert.True(values.PublishForAssociation(assoc, "LD/CSWI1.Pos.t", "ST",
            "2026-10-14T01:00:00Z", "", "", "Report timestamp"));
        Assert.False(values.TryGet("LD/CSWI2.Pos.q", "ST", out _));
        Assert.False(values.TryGet("LD/CSWI1.Pos.q", "MX", out _));
        Assert.False(values.TryGet("LD/CSWI1.Pos.t", "MX", out _));
    }

    [Fact]
    public void ParallelReportsAndReadsRetainIndependentChannels()
    {
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        Parallel.For(0, 512, i =>
        {
            var path = "LD/GGIO1.Ind" + i + ".stVal";
            values.PublishForAssociation(association, path, St, "True [1]", "", "", "Report");
            values.PublishForAssociation(association, path, St, "False [0]", "", "", "MMS Read");
        });
        for (var i = 0; i < 512; i++)
        {
            var path = "LD/GGIO1.Ind" + i + ".stVal";
            Assert.True(values.TryGet(path, St, out var monitoring));
            Assert.Equal("True [1]", monitoring.Value);
            Assert.True(values.TryGet(path, St, out var idle, preferReport: false));
            Assert.Equal("False [0]", idle.Value);
        }
    }

    [Fact]
    public void ClearingSessionDoesNotAllowOldAssociationToRepopulateStore()
    {
        var values = new IedBrowserLiveValues();
        var assoc = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(assoc, Position, St, "Open [01]", "", "", "Report"));
        values.Clear();
        Assert.False(values.TryGet(Position, St, out _));
        Assert.False(values.PublishForAssociation(assoc, Position, St, "Close [10]", "", "", "Report"));
        Assert.True(values.IsOffline);
    }
}
