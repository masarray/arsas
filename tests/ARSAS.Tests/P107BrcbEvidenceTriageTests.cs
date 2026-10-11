using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Deterministic, lab-origin protocol-fact fixtures derived from GE simulator
/// observations. Counts are evidence only; they do not certify lossless SOE.
/// </summary>
public sealed class P107BrcbEvidenceTriageTests
{
    private static NativeReportFrameMetadata Frame(
        ulong? sq, string entry = "", bool? giReason = null,
        bool? overflow = null, ulong? sub = null, bool? more = null)
        => new()
        {
            SequenceNumber = sq,
            EntryIdHex = entry,
            GeneralInterrogationReasonSeen = giReason,
            BufferOverflow = overflow,
            SubSequenceNumber = sub,
            MoreSegmentsFollow = more,
            ReceivedAt = DateTimeOffset.Parse("2026-10-11T04:45:43+00:00")
        };

    [Fact]
    public void Brcb_BackwardSqWithNewEntryIdAndGIAndOverflow_StaysTwoFindingsOneBadFrame()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(state, Frame(12, "AABB"), true));
        var findings = Iec61850ReportContinuityInspector.Observe(
            state, Frame(1, "CCDD", giReason: true, overflow: true), true);

        Assert.Equal(2, findings.Count); // BufOvfl and backward/reset/replay remain visible
        Assert.Contains(findings, f => f.Contains("BufOvfl=true", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Contains("no event-loss conclusion", StringComparison.Ordinal));
        Assert.Equal(1, state.AnomalousFrames);
        Assert.Equal(2, state.AnomalyFindings);
        Assert.Equal(1, state.SequenceDiscontinuities);
        Assert.Equal(1, state.SequenceWithChangedEntryId);
        Assert.Equal(0, state.SequenceWithRepeatedEntryId);
        Assert.Equal(0, state.SequenceWithoutComparableEntryId);
        Assert.Equal(1, state.SequenceWithGiReason);
        Assert.Equal(1, state.SequenceWithOverflow);
        Assert.Equal(1, state.BufferOverflows);
    }

    [Fact]
    public void RepeatedSqAndIdenticalOpaqueEntryID_RemainsAmbiguousNotCertifiedReplay()
    {
        var state = new Iec61850ReportContinuityState();
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(state, Frame(0, "CAFE"), true));
        var findings = Iec61850ReportContinuityInspector.Observe(state, Frame(0, "CAFE"), true);
        Assert.Single(findings);
        Assert.Contains("duplicate/replay", findings[0], StringComparison.Ordinal);
        Assert.Contains("changed=False", findings[0], StringComparison.Ordinal);
        Assert.Equal(1, state.SequenceWithRepeatedEntryId);
        Assert.Equal(0, state.SequenceWithChangedEntryId);
        Assert.Equal(0, state.SequenceWithGiReason);
    }

    [Fact]
    public void MissingEntryID_IsCountedSeparatelyWithoutInferringMissingEvents()
    {
        var state = new Iec61850ReportContinuityState();
        Iec61850ReportContinuityInspector.Observe(state, Frame(4, "CAFE"), true);
        Iec61850ReportContinuityInspector.Observe(state, Frame(1), true);
        Assert.Equal(1, state.SequenceDiscontinuities);
        Assert.Equal(1, state.SequenceWithoutComparableEntryId);
        Assert.Equal(0, state.SequenceWithChangedEntryId);
    }

    [Fact]
    public void GiWriteWithoutDecodedGiReason_DoesNotCountAsFrameGiCorrelation()
    {
        var state = new Iec61850ReportContinuityState { GiWriteAccepted = true };
        Iec61850ReportContinuityInspector.Observe(state, Frame(4, "AA"), true);
        Iec61850ReportContinuityInspector.Observe(state, Frame(0, "BB"), true);
        Assert.Equal(1, state.SequenceDiscontinuities);
        Assert.Equal(0, state.SequenceWithGiReason);
    }

    [Fact]
    public void HealthyIncrementAndRollover_AreNotManufacturedAsSequenceAnomalies()
    {
        var state = new Iec61850ReportContinuityState();
        foreach (var sq in new ulong[] { 65535, 0, 1 })
            Assert.Empty(Iec61850ReportContinuityInspector.Observe(
                state, Frame(sq, sq.ToString("X4")), true));
        Assert.Equal(0, state.AnomalousFrames);
        Assert.Equal(0, state.SequenceDiscontinuities);
    }

    [Fact]
    public void SegmentMetadataFault_IncrementsBadFrameButNotSqDiscontinuity()
    {
        var state = new Iec61850ReportContinuityState();
        var warnings = Iec61850ReportContinuityInspector.Observe(
            state, Frame(12, sub: 2, more: false), true);
        Assert.Contains(warnings, w => w.Contains("starts at SubSqNum=2", StringComparison.Ordinal));
        Assert.Equal(1, state.AnomalousFrames);
        Assert.Equal(0, state.SequenceDiscontinuities);
    }

    [Fact]
    public void SnapshotAndDiagnostics_SeparateFindingsFromFramesAndOpaqueEntryIdKinds()
    {
        var state = new Iec61850ReportContinuityState
        {
            ReportControlReference = "BCUGEF650/LLN0.BR.rpt01",
            DataSetReference = "BCUGEF650/LLN0.CSWI",
            Buffered = true
        };
        Iec61850ReportContinuityInspector.Observe(state, Frame(3, "A0"), true);
        Iec61850ReportContinuityInspector.Observe(
            state, Frame(1, "B0", giReason: true, overflow: true), true);
        var report = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["rpt"] = state }, 67, 0);
        Assert.Equal(2, report.Findings);
        Assert.Equal(1, report.AnomalousFrames);
        Assert.Equal(1, report.SequenceDiscontinuities);
        Assert.Equal(1, report.Streams[0].SequenceWithChangedEntryId);
        Assert.Equal(1, report.Streams[0].SequenceWithGiReason);
        Assert.Equal(1, report.Streams[0].SequenceWithOverflow);

        var sb = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(sb, report);
        var text = sb.ToString();
        Assert.Contains("sequenceDiscontinuities=1", text, StringComparison.Ordinal);
        Assert.Contains("sqChangedEntryID=1", text, StringComparison.Ordinal);
        Assert.Contains("sqWithGIreason=1", text, StringComparison.Ordinal);
        Assert.Contains("sqWithBufOvfl=1", text, StringComparison.Ordinal);
        Assert.Contains("not an event-loss", text, StringComparison.Ordinal);

        // A published copy must not follow later mutation of the live association.
        Iec61850ReportContinuityInspector.Observe(state, Frame(1, "B0"), true);
        Assert.Equal(1, report.SequenceDiscontinuities);
        Assert.Equal(2, report.Findings);
    }

    [Fact]
    public void DiagnosticOnly_LeavesMmsReportAndControlWireContractsUntouched()
    {
        var source = ReadRepoFile("Services/Iec61850ReportContinuityInspector.cs");
        Assert.DoesNotContain("ReadMms", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteMms", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartReport", source, StringComparison.Ordinal);
        Assert.Contains("RecordSequenceCorrelation();", source, StringComparison.Ordinal);
        var runtime = ReadRepoFile("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("session.ReportStreams.Clear()", runtime, StringComparison.Ordinal);
        Assert.Contains("maxTrackedStreams = 64", runtime, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var found = Path.Combine(dir.FullName, path);
            if (File.Exists(found)) return File.ReadAllText(found);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
