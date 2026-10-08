using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ReportContinuitySnapshotP76CTests
{
    [Fact]
    public void Snapshot_MakesMissingMetadataDistinctFromAnomalyFreeObservation()
    {
        var noFrameMetadata = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState>(), 13, 0);
        var emptyReport = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(emptyReport, noFrameMetadata);
        Assert.Contains("NO FRAME METADATA", emptyReport.ToString(), StringComparison.Ordinal);
        Assert.Contains("processUpdates=13", emptyReport.ToString(), StringComparison.Ordinal);

        var healthy = new Iec61850ReportContinuityState
        {
            ReportControlReference = "IED/LLN0.BR.rpt01",
            DataSetReference = "IED/LLN0.Indications",
            ReportId = "IED_RPT",
            Buffered = true
        };
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(healthy,
            new NativeReportFrameMetadata { SequenceNumber = 12, EntryIdHex = "AA" }, true));
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(healthy,
            new NativeReportFrameMetadata { SequenceNumber = 13, EntryIdHex = "BB" }, true));
        var snapshot = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["stream1"] = healthy }, 13, 0);
        var text = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(text, snapshot);
        Assert.Contains("OBSERVED WITHOUT SEQUENCE ALERTS", text.ToString(), StringComparison.Ordinal);
        Assert.Contains("frames=2", text.ToString(), StringComparison.Ordinal);
        Assert.Contains("SqNum=2", text.ToString(), StringComparison.Ordinal);
        Assert.Contains("EntryIDPresent=2", text.ToString(), StringComparison.Ordinal);
        Assert.Contains("firstSqNum=12", text.ToString(), StringComparison.Ordinal);
        Assert.Contains("lastSqNum=13", text.ToString(), StringComparison.Ordinal);
        Assert.Contains("does not prove SOE/event continuity", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSqNumMetadata_IsNeverClassifiedAsFullContinuityProof()
    {
        var state = new Iec61850ReportContinuityState { ReportControlReference = "IED/LLN0.BR.rpt01" };
        Iec61850ReportContinuityInspector.Observe(state, new NativeReportFrameMetadata(), true);
        Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 3 }, true);
        var snapshot = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["k"] = state }, 2, 0);
        var text = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(text, snapshot);
        Assert.Contains("PARTIAL SqNum EVIDENCE", text.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, snapshot.Frames);
        Assert.Equal(1, snapshot.Sequenced);
        Assert.Equal(1, snapshot.Streams[0].SequenceMissing);
    }

    [Fact]
    public void RealFieldBackwardGap_IsCountedWithoutConcludingEventLoss()
    {
        var state = new Iec61850ReportContinuityState
        {
            ReportControlReference = "IED/LLN0.BR.rpt02",
            Buffered = true
        };
        Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 4 }, true);
        var findings = Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 1 }, true);
        Assert.Single(findings);
        var snapshot = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["rpt"] = state }, 7, 0);
        Assert.Equal(1, snapshot.Findings);
        var text = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(text, snapshot);
        Assert.Contains("ANOMALY OBSERVED", text.ToString(), StringComparison.Ordinal);
        Assert.Contains("findings=1", text.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfRevAndBufOvfl_CountIndependentlyEvenWithSequentialSqNum()
    {
        var state = new Iec61850ReportContinuityState { Buffered = true };
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 0, ConfRev = 1 }, true));
        var findings = Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 1, ConfRev = 2, BufferOverflow = true }, true);
        Assert.Equal(2, findings.Count);
        var snapshot = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { ["rpt"] = state }, 2, 0);
        Assert.Equal(2, snapshot.Findings);
        Assert.Equal(1, snapshot.Overflows);
        Assert.Equal(1, snapshot.Streams[0].ConfRevChanges);
    }

    [Fact]
    public void Snapshot_IsAnImmutableCopyOfCurrentAssociationEvidence()
    {
        var state = new Iec61850ReportContinuityState { ReportControlReference = "IED/LLN0.BR.rpt01" };
        Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 1 }, true);
        var streamStates = new Dictionary<string, Iec61850ReportContinuityState> { ["rpt"] = state };
        var captured = Iec61850ReportContinuityInspector.Snapshot(streamStates, 1, 0);
        Iec61850ReportContinuityInspector.Observe(state,
            new NativeReportFrameMetadata { SequenceNumber = 2 }, true);
        streamStates.Clear();
        Assert.Equal(1, captured.Frames);
        Assert.Equal(1, captured.Streams[0].Frames);
        Assert.Equal(1UL, captured.Streams[0].LastSqNum);
    }

    [Fact]
    public void MetadataCardinalityAndUntrustedLabels_AreBoundedInCopiedReport()
    {
        var dict = new Dictionary<string, Iec61850ReportContinuityState>();
        for (var i = 0; i < 20; i++)
        {
            dict["key" + i] = new Iec61850ReportContinuityState
            {
                ReportControlReference = new string('R', 550) + "\r\nStatic parity : INJECTED",
                ReportId = "report",
                Buffered = true,
                FramesSeen = 1,
                SequencedFrames = 1
            };
        }
        var snap = Iec61850ReportContinuityInspector.Snapshot(dict, 20, 2);
        var sb = new StringBuilder();
        DiagnosticReportBuilder.AppendReportContinuity(sb, snap);
        var report = sb.ToString();
        Assert.Contains("INCOMPLETE", report, StringComparison.Ordinal);
        Assert.Contains("TRUNCATED", report, StringComparison.Ordinal);
        Assert.Contains(@"\r\nStatic parity : INJECTED", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nStatic parity : INJECTED", report, StringComparison.Ordinal);
        Assert.DoesNotContain("stream[12]", report, StringComparison.Ordinal);
        Assert.True(report.Length < 10000, $"Diagnostic became unbounded: {report.Length}");
    }

    [Fact]
    public void Runtime_ClearsAssociationEvidenceOnEveryMonitoringOrReconnectReset()
    {
        var text = Read("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("session.Device.ReportContinuityEvidence = null;", text, StringComparison.Ordinal);
        Assert.Contains("Iec61850ReportContinuityInspector.Snapshot", text, StringComparison.Ordinal);
        Assert.Contains("session.ContinuityUntrackedStreams", text, StringComparison.Ordinal);
        Assert.Contains("maxTrackedStreams = 64", text, StringComparison.Ordinal);
        Assert.Equal(3, text.Split("session.Device.ReportContinuityEvidence = null;",
            StringSplitOptions.None).Length - 1);
        var copy = Read("Services/DiagnosticReportBuilder.cs");
        Assert.Contains("AppendReportContinuity(builder, device.ReportContinuityEvidence);",
            copy, StringComparison.Ordinal);
    }

    private static string Read(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var full = Path.Combine(directory.FullName, path);
            if (File.Exists(full))
                return File.ReadAllText(full);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
