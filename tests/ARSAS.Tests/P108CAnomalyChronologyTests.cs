using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P108CAnomalyChronologyTests
{
    private static NativeReportFrameMetadata Frame(int sq, int ordinal, bool overflow = false) => new()
    {
        SequenceNumber = (ulong)sq,
        EntryIdHex = ordinal.ToString("X8"),
        ReceivedAt = DateTimeOffset.Parse("2026-10-11T06:25:51Z").AddMilliseconds(ordinal),
        OptFldsRawHex = "7F80",
        OptFldsSequenceNumber = true,
        OptFldsEntryId = true,
        OptFldsBufferOverflow = true,
        BufferOverflow = overflow
    };

    private static Iec61850ReportContinuityStreamSnapshot Snapshot(Iec61850ReportContinuityState state)
        => Assert.Single(Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["r"] = state }, 0, 0).Streams);

    [Fact]
    public void AnomalyInOmittedMiddleSurvivesHeadTailTruncation()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        for (var i = 0; i < 70; i++)
            Iec61850ReportContinuityInspector.Observe(state, Frame(i, i + 1, overflow: i == 45), true);

        var s = Snapshot(state);
        Assert.Equal(32, s.ChronologyInitialCount);
        Assert.Equal(8, s.ChronologyRecentCount);
        Assert.Equal(30, s.ChronologyOmittedFrames);
        Assert.DoesNotContain(s.Chronology, f => f.FrameOrdinal == 46);
        Assert.Single(s.RecentAnomalies);
        Assert.Equal(46, s.RecentAnomalies[0].FrameOrdinal);
        Assert.True(s.RecentAnomalies[0].BufferOverflow);
        Assert.Equal(1, s.Overflow);
        Assert.Equal(1, s.AnomalousFrames);
        var report = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(report,
            Iec61850ReportContinuityInspector.Snapshot(
                new Dictionary<string, Iec61850ReportContinuityState> { ["r"] = state }, 0, 0));
        Assert.Contains("anomalyFrame#46:", report.ToString(), StringComparison.Ordinal);
        Assert.Contains("BRCB anomaly history", report.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LastEightAnomaliesStayInReceiveOrderAcrossRingWrap()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        for (var i = 0; i < 52; i++)
            Iec61850ReportContinuityInspector.Observe(state, Frame(i, i + 1, overflow: i is >= 35 and <= 46), true);
        var s = Snapshot(state);
        Assert.Equal(12, s.AnomalousFrames);
        Assert.Equal(8, s.RecentAnomalies.Count);
        Assert.Equal(Enumerable.Range(40, 8).Select(x => (long)x), s.RecentAnomalies.Select(x => x.FrameOrdinal));
        Assert.Equal(12, s.Overflow);
    }

    [Fact]
    public void SnapshotDoesNotMutateWhenLaterFramesOverwriteAnomalyRing()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        for (var i = 0; i < 45; i++)
            Iec61850ReportContinuityInspector.Observe(state, Frame(i, i + 1, overflow: i is >= 34 and <= 41), true);
        var snap = Snapshot(state);
        var original = snap.RecentAnomalies.Select(x => x.FrameOrdinal).ToArray();
        for (var i = 45; i < 75; i++)
            Iec61850ReportContinuityInspector.Observe(state, Frame(i, i + 1, overflow: true), true);
        Assert.Equal(original, snap.RecentAnomalies.Select(x => x.FrameOrdinal));
        Assert.Equal(8, snap.RecentAnomalies.Count);
        Assert.Equal(75, Snapshot(state).RecentAnomalies[^1].FrameOrdinal);
    }

    [Fact]
    public void NoWarningsOrUnbufferedReportsDoNotAllocateAnomalyHistory()
    {
        var healthy = new Iec61850ReportContinuityState { Buffered = true };
        var unbuffered = new Iec61850ReportContinuityState();
        for (var i = 0; i < 50; i++)
        {
            Iec61850ReportContinuityInspector.Observe(healthy, Frame(i, i + 1), true);
            Iec61850ReportContinuityInspector.Observe(unbuffered, Frame(i, i + 1, overflow: true), false);
        }
        Assert.Null(healthy.AnomalyChronology);
        Assert.Null(unbuffered.AnomalyChronology);
        Assert.Empty(Snapshot(healthy).RecentAnomalies);
        Assert.Empty(Snapshot(unbuffered).RecentAnomalies);
    }

    [Fact]
    public void OnlyBoundedMetadataIsSavedNeverRawEntryIdOrProcessValues()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        var frame = Frame(0, 1, true) with { EntryIdHex = "DEADBEEF12345678" };
        Iec61850ReportContinuityInspector.Observe(state, frame, true);
        var sb = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(sb,
            Iec61850ReportContinuityInspector.Snapshot(
                new Dictionary<string, Iec61850ReportContinuityState> { ["r"] = state }, 0, 0));
        Assert.DoesNotContain("DEADBEEF12345678", sb.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anomalyFrame#1:", sb.ToString(), StringComparison.Ordinal);
    }
}
