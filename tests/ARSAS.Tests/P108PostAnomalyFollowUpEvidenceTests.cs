using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// P10.8 bounded receive-order evidence. A clean suffix does not clear historical
/// BufOvfl or prove lossless SOE. No network operation is performed here.
/// </summary>
public sealed class P108PostAnomalyFollowUpEvidenceTests
{
    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-10-11T06:25:51.000+00:00");

    private static NativeReportFrameMetadata Frame(
        ulong sequence, int elapsedMilliseconds, bool overflow = false,
        bool missingTime = false) => new()
    {
        ReceivedAt = missingTime ? default : Start.AddMilliseconds(elapsedMilliseconds),
        SequenceNumber = sequence,
        EntryIdHex = sequence.ToString("X4"),
        OptFldsRawHex = "7F80",
        OptFldsSequenceNumber = true,
        OptFldsEntryId = true,
        OptFldsBufferOverflow = true,
        OptFldsConfRev = true,
        GeneralInterrogationReasonSeen = false,
        BufferOverflow = overflow,
        ConfRev = 1
    };

    [Fact]
    public void StartupFindingsRemainVisibleWhileCleanFollowUpFramesAccumulate()
    {
        var state = new Iec61850ReportContinuityState
        {
            ReportControlReference = "BCUGEF650/LLN0.BR.BRB_CSWI_GW010101",
            Buffered = true
        };
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(state, Frame(4, 0), true));
        var anomaly = Iec61850ReportContinuityInspector.Observe(
            state, Frame(1, 120, overflow: true), true);
        Assert.Equal(2, anomaly.Count);

        foreach (var (sq, elapsed) in new[] { (2UL, 2000), (3UL, 3000), (4UL, 4000) })
            Assert.Empty(Iec61850ReportContinuityInspector.Observe(
                state, Frame(sq, elapsed), true));

        Assert.Equal(5, state.FramesSeen);
        Assert.Equal(2, state.AnomalyFindings);
        Assert.Equal(1, state.AnomalousFrames);
        Assert.Equal(1, state.BufferOverflows);
        Assert.Equal(3, state.FramesAfterLastFinding);
        Assert.Equal(Start, state.FirstFrameReceivedAtUtc);
        Assert.Equal(Start.AddMilliseconds(120), state.LastFindingReceivedAtUtc);
        Assert.Equal(Start.AddMilliseconds(4000), state.LastFrameReceivedAtUtc);

        var snap = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["CSWI"] = state }, 5, 0);
        Assert.Equal(3, Assert.Single(snap.Streams).FramesAfterLastFinding);

        var sb = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(sb, snap);
        Assert.Contains("receivedAfterLastFinding=3", sb.ToString(), StringComparison.Ordinal);
        Assert.Contains("later clean frames", sb.ToString(), StringComparison.Ordinal);
        Assert.Contains("BufOvfl=1", sb.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NewLateFindingResetsFollowUpCounterButKeepsAllHistoricalWarnings()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        Iec61850ReportContinuityInspector.Observe(state, Frame(4, 0), true);
        Iec61850ReportContinuityInspector.Observe(state, Frame(1, 200, overflow: true), true);
        Iec61850ReportContinuityInspector.Observe(state, Frame(2, 2000), true);
        Assert.Equal(1, state.FramesAfterLastFinding);

        var late = Iec61850ReportContinuityInspector.Observe(state, Frame(1, 5000), true);
        Assert.Single(late);
        Assert.Equal(0, state.FramesAfterLastFinding);
        Assert.Equal(Start.AddMilliseconds(5000), state.LastFindingReceivedAtUtc);
        Assert.Equal(2, state.AnomalousFrames);
        Assert.Equal(3, state.AnomalyFindings);
        Assert.Equal(1, state.BufferOverflows);
    }

    [Fact]
    public void NoFindingNeverClaimsAFollowUpRecovery()
    {
        var state = new Iec61850ReportContinuityState();
        Iec61850ReportContinuityInspector.Observe(state, Frame(0, 0), false);
        Iec61850ReportContinuityInspector.Observe(state, Frame(1, 60000), false);
        Assert.Equal(0, state.FramesAfterLastFinding);
        Assert.Null(state.LastFindingReceivedAtUtc);
        Assert.Equal(Start.AddMinutes(1), state.LastFrameReceivedAtUtc);
        var snap = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["URCB"] = state }, 2, 0);
        var sb = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(sb, snap);
        Assert.DoesNotContain("RCB follow-up", sb.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDecoderReceiveTimestampRemainsUnknownAndNeverInventsAge()
    {
        var state = new Iec61850ReportContinuityState();
        Iec61850ReportContinuityInspector.Observe(state, Frame(10, 0), true);
        Iec61850ReportContinuityInspector.Observe(
            state, Frame(0, 100, missingTime: true), true);
        Assert.Equal(0, state.FramesAfterLastFinding);
        Assert.Null(state.LastFindingReceivedAtUtc);
        Assert.Equal(Start, state.LastFrameReceivedAtUtc);
        Iec61850ReportContinuityInspector.Observe(state, Frame(1, 5000), true);
        Assert.Equal(1, state.FramesAfterLastFinding);
        Assert.Null(state.LastFindingReceivedAtUtc);
    }

    [Fact]
    public void PostFindingCounterSaturatesAndSnapshotDoesNotFollowMutableState()
    {
        var state = new Iec61850ReportContinuityState
        {
            AnomalousFrames = 1,
            FramesAfterLastFinding = long.MaxValue - 1
        };
        Iec61850ReportContinuityInspector.Observe(state, Frame(2, 100), true);
        Iec61850ReportContinuityInspector.Observe(state, Frame(3, 200), true);
        Assert.Equal(long.MaxValue, state.FramesAfterLastFinding);
        var snap = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["R"] = state }, 1, 0);
        Assert.Equal(long.MaxValue, snap.Streams[0].FramesAfterLastFinding);
        Iec61850ReportContinuityInspector.Observe(state, Frame(1, 300), true);
        Assert.Equal(0, state.FramesAfterLastFinding);
        Assert.Equal(long.MaxValue, snap.Streams[0].FramesAfterLastFinding);
    }

    [Fact]
    public void MultipleStreamsHaveIndependentFollowUpEvidenceAndBoundedSnapshots()
    {
        var br = new Iec61850ReportContinuityState
        {
            Buffered = true,
            ReportControlReference = "BCUGEF650/LLN0.BR.CSWI"
        };
        var ur = new Iec61850ReportContinuityState
        {
            ReportControlReference = "BCUGEF650/LLN0.RP.MMXU"
        };
        Iec61850ReportContinuityInspector.Observe(br, Frame(1, 0, overflow: true), true);
        Iec61850ReportContinuityInspector.Observe(br, Frame(2, 5000), true);
        Iec61850ReportContinuityInspector.Observe(ur, Frame(0, 0), false);
        Iec61850ReportContinuityInspector.Observe(ur, Frame(1, 5000), false);
        Assert.Equal(1, br.FramesAfterLastFinding);
        Assert.Equal(0, ur.FramesAfterLastFinding);
        var snapshot = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["a"] = br, ["b"] = ur },
            5, 0, maxShown: 1);
        Assert.Equal(2, snapshot.StreamCount);
        Assert.Single(snapshot.Streams);
        Assert.Equal("BCUGEF650/LLN0.BR.CSWI", snapshot.Streams[0].Rcb);
    }
}
