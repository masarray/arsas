using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// P10.8B: fixed-capacity BRCB receive-order metadata for offline correlation.
/// This is not a packet sniffer or proof that the IED delivered every SOE event.
/// </summary>
public sealed class P108BBoundedReportChronologyTests
{
    private static readonly DateTimeOffset Epoch =
        DateTimeOffset.Parse("2026-10-11T06:25:51.000+00:00");

    private static NativeReportFrameMetadata Frame(
        ulong sq, string entryId, int ms,
        bool? gi = false, bool? overflow = false, bool rawProven = true) => new()
    {
        SequenceNumber = sq,
        EntryIdHex = entryId,
        ReceivedAt = Epoch.AddMilliseconds(ms),
        OptFldsRawHex = rawProven ? "7F80" : "",
        OptFldsSequenceNumber = rawProven ? true : null,
        OptFldsEntryId = rawProven ? true : null,
        OptFldsBufferOverflow = rawProven ? true : null,
        OptFldsConfRev = rawProven ? true : null,
        BufferOverflow = overflow,
        GeneralInterrogationReasonSeen = gi,
        ConfRev = 5
    };

    private static Iec61850ReportContinuityStreamSnapshot Only(
        Iec61850ReportContinuityState state)
        => Assert.Single(Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["r"] = state }, 0, 0).Streams);

    [Fact]
    public void SixtyReports_PreservesFirst32AndLast8InReceiveOrderOnly()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        for (var index = 0; index < 60; index++)
            Assert.Empty(Iec61850ReportContinuityInspector.Observe(
                state, Frame((ulong)index, index.ToString("X4"), index * 100), true));
        var evidence = Only(state);
        Assert.Equal(32, evidence.ChronologyInitialCount);
        Assert.Equal(8, evidence.ChronologyRecentCount);
        Assert.Equal(20, evidence.ChronologyOmittedFrames);
        Assert.Equal(40, evidence.Chronology.Count);
        Assert.Equal(Enumerable.Range(1, 32).Concat(Enumerable.Range(53, 8))
                .Select(index => (long)index),
            evidence.Chronology.Select(entry => entry.FrameOrdinal));
        Assert.Equal("unverified", evidence.Chronology[0].EntryIdRelation);
        Assert.Equal("changed", evidence.Chronology[1].EntryIdRelation);
        Assert.Equal("changed", evidence.Chronology[^1].EntryIdRelation);
        Assert.Equal(Epoch.AddMilliseconds(5900), evidence.Chronology[^1].ReceivedAtUtc);

        var text = Format(state);
        Assert.Contains("first=32, recent=8, omittedMiddle=20", text, StringComparison.Ordinal);
        Assert.Contains("frame#60:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("frame#40:", text, StringComparison.Ordinal);
        Assert.Contains("receive-order metadata only", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstAndLastRangesDoNotDuplicateFramesAtCapacityBoundary()
    {
        foreach (var count in new[] { 1, 31, 32, 33, 39, 40, 41 })
        {
            var state = new Iec61850ReportContinuityState { Buffered = true };
            for (var index = 0; index < count; index++)
                Iec61850ReportContinuityInspector.Observe(
                    state, Frame((ulong)index, index.ToString("X4"), index), true);

            var evidence = Only(state);
            Assert.Equal(Math.Min(32, count), evidence.ChronologyInitialCount);
            Assert.Equal(Math.Min(8, Math.Max(0, count - 32)), evidence.ChronologyRecentCount);
            Assert.Equal(Math.Max(0, count - 40), evidence.ChronologyOmittedFrames);
            Assert.Equal(Math.Min(count, 40), evidence.Chronology.Count);
            Assert.Equal(evidence.Chronology.Count,
                evidence.Chronology.Select(f => f.FrameOrdinal).Distinct().Count());
        }
    }

    [Fact]
    public void DuplicateSqWithRepeatedEntryId_StillRaisesWarningAndRecordsOneFrame()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        Iec61850ReportContinuityInspector.Observe(state, Frame(4, "AABB", 0), true);
        var findings = Iec61850ReportContinuityInspector.Observe(
            state, Frame(4, "AABB", 100, gi: true, overflow: true), true);
        Assert.Equal(2, findings.Count);
        var evidence = Only(state);
        Assert.Equal(2, evidence.Chronology.Count);
        Assert.Equal("repeated", evidence.Chronology[1].EntryIdRelation);
        Assert.Equal(2, evidence.Chronology[1].FindingCount);
        Assert.True(evidence.Chronology[1].GiReason);
        Assert.True(evidence.Chronology[1].BufferOverflow);
        Assert.Equal(1, evidence.Overflow);
        Assert.Equal(1, evidence.SequenceDiscontinuities);
    }

    [Fact]
    public void MissingRawOptionFieldsCannotInventComparableEntryId()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        Iec61850ReportContinuityInspector.Observe(
            state, Frame(4, "ABCD", 0, rawProven: false), true);
        Iec61850ReportContinuityInspector.Observe(
            state, Frame(1, "1234", 100), true);
        Iec61850ReportContinuityInspector.Observe(
            state, Frame(2, "1234", 200), true);
        var entries = Only(state).Chronology;
        Assert.Equal("unverified", entries[0].EntryIdRelation);
        Assert.Equal("unverified", entries[1].EntryIdRelation);
        Assert.Equal("repeated", entries[2].EntryIdRelation);
        Assert.Equal(1, Only(state).SequenceWithoutComparableEntryId);
    }

    [Fact]
    public void ChronologyNeverContainsTheRawBrcbEntryIdOrProcessValues()
    {
        var state = new Iec61850ReportContinuityState
        {
            Buffered = true,
            ReportControlReference = "BCUGEF650/LLN0.BR.BRB_CSWI_GW010101"
        };
        Iec61850ReportContinuityInspector.Observe(
            state, Frame(4, "DEADBEEF0123456789", 0), true);
        Iec61850ReportContinuityInspector.Observe(
            state, Frame(1, "CAFED00DFEED000011", 100), true);
        var text = Format(state);
        Assert.Contains("EntryIDrelation=unverified", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DEADBEEF0123456789", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CAFED00DFEED000011", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnbufferedReportingDoesNotAllocateChronologyHistory()
    {
        var state = new Iec61850ReportContinuityState { Buffered = false };
        for (var index = 0; index < 50; index++)
            Iec61850ReportContinuityInspector.Observe(
                state, Frame((ulong)index, "", index), false);
        var snap = Only(state);
        Assert.Empty(snap.Chronology);
        Assert.Null(state.InitialChronology);
        Assert.Null(state.RecentChronology);
        Assert.DoesNotContain("BRCB chronology", Format(state), StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotIsAnImmutableCopyDespiteLaterRingWrap()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        for (var index = 0; index < 50; index++)
            Iec61850ReportContinuityInspector.Observe(
                state, Frame((ulong)index, index.ToString("X4"), index * 100), true);
        var frozen = Only(state);
        Assert.Equal(18, frozen.ChronologyOmittedFrames);
        var tail = frozen.Chronology[^1];
        for (var index = 50; index < 90; index++)
            Iec61850ReportContinuityInspector.Observe(
                state, Frame((ulong)index, index.ToString("X4"), index * 100), true);
        Assert.Equal(tail, frozen.Chronology[^1]);
        Assert.Equal(50, frozen.Chronology[^1].FrameOrdinal);
        Assert.Equal(90, Only(state).Chronology[^1].FrameOrdinal);
        Assert.Equal(50, frozen.Frames);
    }

    [Fact]
    public void UnknownDecoderTimestampIsNotSubstitutedByWallClock()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata
            {
                SequenceNumber = 10,
                EntryIdHex = "000A",
                ReceivedAt = default
            }, true);
        var evidence = Only(state);
        Assert.Null(evidence.Chronology[0].ReceivedAtUtc);
        Assert.Contains("receivedUtc=unknown", Format(state), StringComparison.Ordinal);
    }

    [Fact]
    public void DistinctBrcbStreamsRetainIndependentBoundedChronologies()
    {
        var a = new Iec61850ReportContinuityState { Buffered = true, ReportControlReference = "A" };
        var b = new Iec61850ReportContinuityState { Buffered = true, ReportControlReference = "B" };
        Iec61850ReportContinuityInspector.Observe(a, Frame(0, "00", 0), true);
        for (var index = 0; index < 60; index++)
            Iec61850ReportContinuityInspector.Observe(
                b, Frame((ulong)index, index.ToString("X4"), index), true);
        var snapshot = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["a"] = a, ["b"] = b },
            0, 0, maxShown: 1);
        Assert.Equal(2, snapshot.StreamCount);
        Assert.Single(snapshot.Streams);
        Assert.Single(snapshot.Streams[0].Chronology);
        Assert.Equal("A", snapshot.Streams[0].Rcb);
    }

    private static string Format(Iec61850ReportContinuityState state)
    {
        var sb = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(sb,
            Iec61850ReportContinuityInspector.Snapshot(
                new Dictionary<string, Iec61850ReportContinuityState> { ["one"] = state },
                0, 0));
        return sb.ToString();
    }
}
