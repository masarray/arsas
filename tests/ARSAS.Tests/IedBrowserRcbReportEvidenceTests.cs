using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class IedBrowserRcbReportEvidenceTests
{
    private static Iec61850ReportContinuityStreamSnapshot Stream(
        string rcb = "LD/LLN0$RP$urcb01",
        string set = "LD/LLN0$Signals", string id = "Rpt_01",
        long frames = 2, DateTimeOffset? time = null)
        => new()
        {
            Rcb = rcb, DataSet = set, ReportId = id, Frames = frames,
            LastSqNum = 8,
            LastFrameReceivedAtUtc = time ?? new DateTimeOffset(
                2026, 10, 15, 8, 0, 0, TimeSpan.Zero)
        };

    [Fact]
    public void OneVerifiedRcbStream_UsesActualReceivedMetadata()
    {
        var stream = Stream();
        var evidence = new Iec61850ReportContinuitySnapshot { Streams = [stream] };
        var selected = IedBrowserRcbReportEvidence.Select(evidence,
            "LD/LLN0.RP.urcb01", "LD/LLN0.Signals", "Rpt_01");
        Assert.Same(stream, selected);
        Assert.Equal(2, selected!.Frames);
        Assert.Equal((ulong)8, selected.LastSqNum);
        Assert.NotNull(selected.LastFrameReceivedAtUtc);
    }

    [Fact]
    public void DifferentOrAmbiguousStreamNeverBecomesLastReport()
    {
        var one = Stream();
        var evidence = new Iec61850ReportContinuitySnapshot { Streams = [one] };
        Assert.Null(IedBrowserRcbReportEvidence.Select(evidence,
            "LD/LLN0.RP.urcb02", "LD/LLN0.Signals", "Rpt_01"));
        Assert.Null(IedBrowserRcbReportEvidence.Select(evidence,
            "LD/LLN0.RP.urcb01", "LD/LLN0.Other", "Rpt_01"));
        Assert.Null(IedBrowserRcbReportEvidence.Select(evidence,
            "LD/LLN0.RP.urcb01", "LD/LLN0.Signals", "Wrong"));
        Assert.Null(IedBrowserRcbReportEvidence.Select(
            new Iec61850ReportContinuitySnapshot { Streams = [one, Stream()] },
            "LD/LLN0.RP.urcb01", "LD/LLN0.Signals", "Rpt_01"));
        Assert.Null(IedBrowserRcbReportEvidence.Select(
            new Iec61850ReportContinuitySnapshot { Streams = [Stream(frames: 0)] },
            "LD/LLN0.RP.urcb01", "LD/LLN0.Signals", "Rpt_01"));
        Assert.Null(IedBrowserRcbReportEvidence.Select(null,
            "LD/LLN0.RP.urcb01", "LD/LLN0.Signals", "Rpt_01"));
    }
}
