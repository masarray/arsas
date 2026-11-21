using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P9BufferedReplayOriginTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private const string Rcb = "SG7_BCU/LLN0$BR$BrcbA01";

    private static NativeReportFrameMetadata Frame(
        int timeSeconds, int receivedSeconds, string id,
        ulong? confRev = 3, string rcb = Rcb,
        bool? overflow = null, bool missingTime = false) => new()
    {
        ReportControlReference = rcb,
        DataSetReference = "SG7_BCU/LLN0$dsEvents",
        ReportId = "Rpt_0",
        ConfRev = confRev,
        EntryIdHex = id,
        ReportTimestamp = missingTime ? "" : Start.AddSeconds(timeSeconds).ToString("O"),
        ReceivedAt = Start.AddSeconds(receivedSeconds),
        BufferOverflow = overflow
    };

    [Fact]
    public void OfflineBrcbHistoryIsSeparatedFromLaterLiveEvent()
    {
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(101, Start);
        var online = Frame(10, 11, "0101");
        Assert.Equal("Live", tracker.Classify(online, 101).Label);
        tracker.RecordAccepted(online, 101);
        tracker.OnDisconnected(Start.AddSeconds(30));
        tracker.OnAssociated(102, Start.AddSeconds(70));

        var e2 = Frame(40, 73, "0102");
        var e3 = Frame(50, 74, "0103");
        var e4 = Frame(60, 75, "0104");
        Assert.Equal("Buffer", tracker.Classify(e2, 102).Label);
        tracker.RecordAccepted(e2, 102);
        Assert.Equal("Buffer", tracker.Classify(e3, 102).Label);
        tracker.RecordAccepted(e3, 102);
        Assert.Equal("Buffer", tracker.Classify(e4, 102).Label);
        tracker.RecordAccepted(e4, 102);

        Assert.Equal("Live", tracker.Classify(Frame(85, 86, "0105"), 102).Label);
    }

    [Fact]
    public void OverflowAndSequenceNumberCannotProveReplay()
    {
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(1, Start);
        // First report with BufOvfl and SqNum reset but no trusted TimeOfEntry.
        var unknown = Frame(0, 3, "01", overflow: true, missingTime: true);
        Assert.Equal("Unknown", tracker.Classify(unknown, 1).Label);
        tracker.RecordAccepted(unknown, 1);
        tracker.OnDisconnected(Start.AddSeconds(20));
        tracker.OnAssociated(2, Start.AddSeconds(60));
        Assert.Equal("Unknown", tracker.Classify(Frame(30, 63, "02", overflow: true), 2).Label);
    }

    [Fact]
    public void DifferentRcbConfRevCursorAndTimestampFailClosed()
    {
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(1, Start);
        tracker.RecordAccepted(Frame(10, 11, "01"), 1);
        tracker.OnDisconnected(Start.AddSeconds(30));
        tracker.OnAssociated(2, Start.AddSeconds(70));

        Assert.Equal("Unknown", tracker.Classify(Frame(40, 72, "02", rcb: "Other/LLN0$BR$BrcbA01"), 2).Label);
        Assert.Equal("Unknown", tracker.Classify(Frame(40, 72, "02", confRev: 4), 2).Label);
        Assert.Equal("Unknown", tracker.Classify(Frame(40, 72, "01"), 2).Label);
        Assert.Equal("Unknown", tracker.Classify(Frame(40, 72, "02", missingTime: true), 2).Label);
        Assert.Equal("Unknown", tracker.Classify(Frame(40, 72, "02"), 1).Label);
        // Report from before this session but outside the observed offline window.
        Assert.Equal("Unknown", tracker.Classify(Frame(20, 72, "02"), 2).Label);
    }

    [Theory]
    [InlineData("data-change", "Data change")]
    [InlineData("general-interrogation", "GI")]
    [InlineData("integrity", "Integrity")]
    public void CauseRemainsIndependentFromReplayOrigin(string reason, string expected)
    {
        var e = new Iec61850EventEntry
        {
            Reason = reason,
            SourceMode = "Static DataSet: StaticBrcb",
            DeliveryEvidence = new Iec61850ReportDeliveryEvidence("Buffer", "Offline interval"),
            ReportFrame = Frame(40, 73, "0102")
        };
        Assert.Equal(expected, e.EventCause);
        Assert.Equal("Buffer", e.DeliveryOrigin);
        Assert.Equal("Buffer · " + expected, e.EventLabel);
        Assert.Equal("0102", e.ReportFrame?.EntryIdHex);
    }

    [Theory]
    [InlineData("80", "Live")] // leapSecondsKnown is not a clock fault
    [InlineData("00", "Live")]
    [InlineData("20", "Unknown")] // clockNotSynchronized
    [InlineData("40", "Unknown")] // clockFailure
    [InlineData("60", "Unknown")] // both failure flags
    public void ActualAriecWireTimestampQualityControlsProof(
        string quality, string expectedOrigin)
    {
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(7, Start);
        var frame = new NativeReportFrameMetadata
        {
            ReportControlReference = Rcb,
            EntryIdHex = "0101",
            ConfRev = 3,
            ReceivedAt = Start.AddSeconds(11),
            ReportTimestamp = Start.AddSeconds(10).ToString(
                "yyyy-MM-dd HH:mm:ss.fffffff",
                System.Globalization.CultureInfo.InvariantCulture) + $" UTC (q=0x{quality})"
        };
        Assert.Equal(expectedOrigin, tracker.Classify(frame, 7).Label);
    }

    [Fact]
    public void ColdStartCannotCertifyHistoricalReplayEvenWithEntryId()
    {
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(5, Start.AddSeconds(70));
        Assert.Equal("Unknown", tracker.Classify(Frame(40, 73, "01"), 5).Label);
    }
}
