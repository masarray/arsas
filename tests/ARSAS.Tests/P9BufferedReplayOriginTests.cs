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

    [Fact]
    public void AnUrcbCannotClaimReplayEvenWithAnOldOfflineCursor()
    {
        const string urcb = "SG7_BCU/LLN0$RP$UrcbA01";
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(600, Start);
        tracker.RecordAccepted(Frame(10, 11, "0101", rcb: urcb), 600);
        tracker.OnDisconnected(Start.AddSeconds(30));
        tracker.OnAssociated(601, Start.AddSeconds(70));
        Assert.Equal("Unknown", tracker.Classify(
            Frame(40, 73, "0102", rcb: urcb), 601).Label);
    }

    [Theory]
    [InlineData("data-change", "Data change", "Buffer event")]
    [InlineData("quality-change", "Quality", "Buffer event")]
    [InlineData("data-update", "Data update", "Buffer event")]
    [InlineData("general-interrogation", "GI", "GI")]
    [InlineData("integrity", "Integrity", "Integrity")]
    public void DisplayEventPreservesIntentionalReportCauses(
        string reason, string expectedCause, string expectedLabel)
    {
        var e = new Iec61850EventEntry
        {
            Reason = reason,
            SourceMode = "Static DataSet: StaticBrcb",
            DeliveryEvidence = new Iec61850ReportDeliveryEvidence("Buffer", "Offline interval"),
            ReportFrame = Frame(40, 73, "0102")
        };
        Assert.Equal(expectedCause, e.EventCause);
        Assert.Equal(expectedLabel, e.EventLabel);
        Assert.Equal("0102", e.ReportFrame?.EntryIdHex);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Live")]
    public void UncertainOriginNeverLeaksToUserFacingEvent(string internalLabel)
    {
        var entry = new Iec61850EventEntry
        {
            Reason = "data-change",
            SourceMode = "Static DataSet: StaticBrcb",
            DeliveryEvidence = new Iec61850ReportDeliveryEvidence(internalLabel, "Diagnostics only")
        };
        Assert.Equal("Data change", entry.EventLabel);
        Assert.DoesNotContain("Unknown", entry.EventLabel, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Origin", entry.EventLabel, StringComparison.OrdinalIgnoreCase);
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
    public void ManualPurgeRevokesOnlyTargetBrcbOldCursorAndLaterCanRecover()
    {
        const string other = "SG7_BCU/LLN0$BR$BrcbB01";
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(101, Start);
        tracker.RecordAccepted(Frame(10, 11, "0101"), 101);
        tracker.RecordAccepted(Frame(10, 11, "0201", rcb: other), 101);
        tracker.OnDisconnected(Start.AddSeconds(30));
        tracker.OnAssociated(102, Start.AddSeconds(70));
        Assert.Equal("Buffer", tracker.Classify(Frame(40, 73, "0102"), 102).Label);
        Assert.Equal("Buffer", tracker.Classify(Frame(40, 73, "0202", rcb: other), 102).Label);

        tracker.InvalidateForManualPurge("SG7_BCU/LLN0.BR.BrcbA01");
        Assert.Equal("Unknown", tracker.Classify(Frame(40, 73, "0102"), 102).Label);
        Assert.Equal("Buffer", tracker.Classify(Frame(40, 73, "0202", rcb: other), 102).Label);

        tracker.RecordAccepted(Frame(85, 86, "0301"), 102);
        tracker.OnDisconnected(Start.AddSeconds(100));
        tracker.OnAssociated(103, Start.AddSeconds(140));
        Assert.Equal("Buffer", tracker.Classify(Frame(120, 144, "0302"), 103).Label);
    }

    [Fact]
    public void UnknownManualPurgeTargetFailsClosed()
    {
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(11, Start);
        tracker.RecordAccepted(Frame(10, 11, "01"), 11);
        tracker.OnDisconnected(Start.AddSeconds(30));
        tracker.OnAssociated(12, Start.AddSeconds(70));
        Assert.Equal("Buffer", tracker.Classify(Frame(40, 73, "02"), 12).Label);
        tracker.InvalidateForManualPurge("not-an-exact-RCB");
        Assert.Equal("Unknown", tracker.Classify(Frame(40, 73, "02"), 12).Label);
        tracker.InvalidateForManualPurge(null);
    }
    [Fact]
    public void ExplicitReconnectReusesBoundedTrackerForSameDeviceAndFencesEndpointChanges()
    {
        using var _ = new System.IO.MemoryStream(); // no native MMS session needed
        var runtime = new Iec61850MonitorRuntime();
        var device = new Iec61850MonitorDevice {
            IpAddress = "192.16.1.73", Port = 102
        };
        var method = typeof(Iec61850MonitorRuntime).GetMethod(
            "ReplayTrackerForDevice",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        Iec61850ReportDeliveryTracker Read() =>
            Assert.IsType<Iec61850ReportDeliveryTracker>(
                method!.Invoke(runtime, new object[] { device }));

        var before = Read();
        before.OnAssociated(91, Start);
        before.RecordAccepted(Frame(10, 11, "01"), 91);
        before.OnDisconnected(Start.AddSeconds(30));
        var after = Read();
        Assert.Same(before, after);
        after.OnAssociated(92, Start.AddSeconds(70));
        Assert.Equal("Buffer", after.Classify(Frame(40, 73, "02"), 92).Label);

        device.IpAddress = "192.16.1.74";
        var changedEndpoint = Read();
        Assert.NotSame(after, changedEndpoint);
        changedEndpoint.OnAssociated(93, Start.AddSeconds(140));
        Assert.Equal("Unknown", changedEndpoint.Classify(Frame(120, 143, "03"), 93).Label);
    }

    [Theory]
    [InlineData("data-change", true)]
    [InlineData("dchg", true)]
    [InlineData("integrity", false)]
    [InlineData("general-interrogation", false)]
    [InlineData("data-change, integrity", false)]
    [InlineData("quality-change", false)]
    [InlineData("", false)]
    public void FirstWireChangeMustNotBeDiscardedAsInitialImage(string reason, bool expected)
    {
        Assert.Equal(expected, Iec61850EventOriginPolicy.FirstDataChangeCanStartSoe(
            hadValue: false, hasProcessValue: true, routedReport: true,
            hasWireFrame: true, reason, "Static DataSet: StaticBrcb"));
        Assert.False(Iec61850EventOriginPolicy.FirstDataChangeCanStartSoe(
            hadValue: false, hasProcessValue: true, routedReport: false,
            hasWireFrame: true, reason, "Static DataSet: StaticBrcb"));
        Assert.False(Iec61850EventOriginPolicy.FirstDataChangeCanStartSoe(
            hadValue: false, hasProcessValue: true, routedReport: true,
            hasWireFrame: false, reason, "Static DataSet: StaticBrcb"));
        Assert.False(Iec61850EventOriginPolicy.FirstDataChangeCanStartSoe(
            hadValue: true, hasProcessValue: true, routedReport: true,
            hasWireFrame: true, reason, "Static DataSet: StaticBrcb"));
    }

    [Fact]
    public void BothEngineeringAndFatStartsHavePerDeviceAdmissionAndRelease()
    {
        string Source(string file)
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var path = System.IO.Path.Combine(dir.FullName, file);
                if (System.IO.File.Exists(path))
                    return System.IO.File.ReadAllText(path);
                dir = dir.Parent;
            }
            throw new System.IO.FileNotFoundException(file);
        }
        var ui = Source("MainWindow.xaml.cs");
        var fat = Source("MainWindow.IoTesting.MultiIedMonitor.cs");
        Assert.Contains("_monitorStartFlights.TryAdd(device.DeviceId, 0)", ui);
        Assert.Contains("_monitorStartFlights.TryRemove(device.DeviceId, out _)", ui);
        Assert.Contains("_monitorStartFlights.TryAdd(device.DeviceId, 0)", fat);
        Assert.Contains("_monitorStartFlights.TryRemove(device.DeviceId, out _)", fat);
        Assert.Contains("!ReferenceEquals(activePoint, point)", ui);
    }
    private static NativeReportFrameMetadata HistoricalBrcb(
        string rcb = Rcb, string quality = "80", string entryId = "00010002",
        bool? hasEntryId = true, bool? hasReason = true,
        bool? giReason = false, bool? integrityReason = false,
        int timeSeconds = -60) => new()
    {
        ReportControlReference = rcb,
        DataSetReference = "SG7_BCU/LLN0$dsEvents",
        ReportId = "Rpt_0",
        EntryIdHex = entryId,
        ConfRev = 3,
        OptFldsRawHex = "7F",
        OptFldsEntryId = hasEntryId,
        OptFldsReasonForInclusion = hasReason,
        GeneralInterrogationReasonSeen = giReason,
        IntegrityReasonSeen = integrityReason,
        ReportTimestamp = Start.AddSeconds(timeSeconds).ToString(
            "yyyy-MM-dd HH:mm:ss.fffffff",
            System.Globalization.CultureInfo.InvariantCulture) + $" UTC (q=0x{quality})",
        ReceivedAt = Start.AddSeconds(3)
    };

    [Fact]
    public void SynchronizedHistoricalBrcbBacklogIsDetectedOnColdAttach()
    {
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(501, Start);
        // This event was generated BEFORE ARSAS ever connected. It is still
        // buffered delivery when wire EntryID/TimeOfEntry/OptFlds prove it.
        var historical = HistoricalBrcb(timeSeconds: -86400);
        Assert.Equal("Buffer", tracker.Classify(historical, 501).Label);
        var eventEntry = new Iec61850EventEntry
        {
            Reason = "data-change",
            SourceMode = "Static DataSet: StaticBrcb",
            ReportFrame = historical,
            DeliveryEvidence = tracker.Classify(historical, 501)
        };
        Assert.Equal("Buffer event", eventEntry.EventLabel);
        tracker.RecordAccepted(historical, 501);
        // An identical EntryID is retransmission, not another replay event.
        Assert.Equal("Unknown", tracker.Classify(historical, 501).Label);
    }

    [Theory]
    [InlineData("20")] // clockNotSynchronized
    [InlineData("40")] // clockFailure
    [InlineData("60")]
    public void UnsynchronizedHistoricalTimeIsNotReplayProof(string quality)
    {
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(502, Start);
        Assert.Equal("Unknown", tracker.Classify(HistoricalBrcb(quality: quality), 502).Label);
    }

    [Fact]
    public void HistoricalBrcbRequiresExactWireEntryIdAndChangeReasonEvidence()
    {
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(503, Start);
        Assert.Equal("Unknown", tracker.Classify(HistoricalBrcb(
            rcb: "SG7_BCU/LLN0$RP$UrcbA01"), 503).Label);
        Assert.Equal("Unknown", tracker.Classify(HistoricalBrcb(entryId: ""), 503).Label);
        Assert.Equal("Unknown", tracker.Classify(HistoricalBrcb(entryId: "XZY"), 503).Label);
        Assert.Equal("Unknown", tracker.Classify(HistoricalBrcb(hasEntryId: false), 503).Label);
        Assert.Equal("Unknown", tracker.Classify(HistoricalBrcb(hasReason: false), 503).Label);
        Assert.Equal("Unknown", tracker.Classify(HistoricalBrcb(giReason: true), 503).Label);
        Assert.Equal("Unknown", tracker.Classify(HistoricalBrcb(integrityReason: true), 503).Label);
        Assert.Equal("Unknown", tracker.Classify(HistoricalBrcb(giReason: null), 503).Label);
        // A timely TimeOfEntry after the new association is a live report,
        // not a historical replay. No old-cursor prerequisite is needed for Live.
        Assert.Equal("Live", tracker.Classify(HistoricalBrcb(timeSeconds: 2), 503).Label);
    }

    // Real byte-for-byte TimeOfEntry evidence from SG7_BCU FAT diagnostics,
    // not a synthetic IEC UTC-Time with quality bits the IED never supplied.
    [Theory]
    [InlineData("SG7_BCU/LLN0$BR$BrcbA01",
        "2026-11-20 12:40:03.582 UTC (binary-time=02B7DAFE3D30)",
        "0000000000000002", "3300")]
    [InlineData("SG7_BCU/LLN0$BR$BrcbB01",
        "2026-11-21 03:48:49.933 UTC (binary-time=00D1808D3D31)",
        "000000000000007F", "7F00")]
    public void Sg7ActualSixByteBinaryTimeDetectsHistoricalBrcbReplay(
        string rcb, string wireTime, string entryId, string optFlds)
    {
        var associated = new DateTimeOffset(2026, 11, 21, 4, 16, 53, TimeSpan.Zero);
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(700, associated);
        var frame = new NativeReportFrameMetadata
        {
            ReportControlReference = rcb,
            ReportId = "Rpt_0",
            DataSetReference = "SG7_BCUMaster/LLN0$TT6DataSet3",
            EntryIdHex = entryId,
            OptFldsRawHex = optFlds,
            OptFldsEntryId = true,
            OptFldsReasonForInclusion = true,
            GeneralInterrogationReasonSeen = false,
            IntegrityReasonSeen = false,
            ReportTimestamp = wireTime,
            ReceivedAt = associated.AddSeconds(5)
        };
        var evidence = tracker.Classify(frame, 700);
        Assert.Equal("Buffer", evidence.Label);
        Assert.Contains("BinaryTime", evidence.Basis);
        var entry = new Iec61850EventEntry
        {
            Reason = "data-change",
            SourceMode = "Static DataSet: StaticBrcb",
            ReportFrame = frame,
            DeliveryEvidence = evidence
        };
        Assert.Equal("Buffer event", entry.EventLabel);
    }

    [Fact]
    public void BinaryTimeRejectsMalformedBytesAbsentAbsoluteDateAndClockSkew()
    {
        var start = new DateTimeOffset(2026, 11, 21, 4, 16, 53, TimeSpan.Zero);
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(701, start);
        string Label(string wire)
        {
            var frame = new NativeReportFrameMetadata
            {
                ReportControlReference = Rcb,
                EntryIdHex = "00000001",
                OptFldsEntryId = true,
                OptFldsReasonForInclusion = true,
                GeneralInterrogationReasonSeen = false,
                IntegrityReasonSeen = false,
                ReportTimestamp = wire,
                ReceivedAt = start.AddSeconds(6)
            };
            return tracker.Classify(frame, 701).Label;
        }
        Assert.Equal("Unknown", Label(
            "2026-11-20 12:40:03.582 UTC (binary-time=02B7DAFE3D3F)"));
        Assert.Equal("Unknown", Label(
            "2026-11-20 12:40:03.582 UTC (binary-time=02B7DAFE)"));
        Assert.Equal("Unknown", Label(
            "time-of-day=12:40:03.582 (binary-time=02B7DAFE)"));
        Assert.Equal("Unknown", Label(
            "2026-11-21 04:16:00.000 UTC (binary-time=00F9F8C73D31)"));
    }

    [Fact]
    public void BinaryTimeHistoricalIntegrityIsNotReclassifiedAsBuffer()
    {
        var start = new DateTimeOffset(2026, 11, 21, 4, 16, 53, TimeSpan.Zero);
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(702, start);
        var frame = new NativeReportFrameMetadata
        {
            ReportControlReference = Rcb,
            EntryIdHex = "00000002",
            OptFldsEntryId = true,
            OptFldsReasonForInclusion = true,
            GeneralInterrogationReasonSeen = false,
            IntegrityReasonSeen = true,
            ReportTimestamp = "2026-11-20 12:40:03.582 UTC (binary-time=02B7DAFE3D30)",
            ReceivedAt = start.AddSeconds(6)
        };
        Assert.Equal("Unknown", tracker.Classify(frame, 702).Label);
    }

    [Fact]
    public void RepeatedAcceptedMembersOfOneFramePreserveOfflineReplayCursor()
    {
        // Real SG7_BCU reporting fans out a single InformationReport to
        // dozens of selected values. Only the first schema-accepted member
        // should need an EntryID/TimeOfEntry cursor update.
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(811, Start);
        var frame = Frame(10, 11, "0101");
        for (var member = 0; member < 56; member++)
            tracker.RecordAccepted(frame, 811);

        tracker.OnDisconnected(Start.AddSeconds(30));
        tracker.OnAssociated(812, Start.AddSeconds(70));
        var replay = Frame(40, 73, "0102");
        Assert.Equal("Buffer", tracker.Classify(replay, 812).Label);

        for (var member = 0; member < 56; member++)
            tracker.RecordAccepted(replay, 812);
        Assert.Equal("Unknown", tracker.Classify(replay, 812).Label);
        Assert.Equal("Live", tracker.Classify(Frame(85, 86, "0103"), 812).Label);
    }

    [Fact]
    public void RepeatedFrameFastPathIsExplicitAndAssociationScoped()
    {
        // A lightweight source contract prevents accidentally reintroducing
        // one BinaryTime decode per DataSet member into the hot path.
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(
                   System.IO.Path.Combine(dir.FullName,
                       "Services/Iec61850ReportDeliveryTracker.cs")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = System.IO.File.ReadAllText(
            System.IO.Path.Combine(dir!.FullName,
                "Services/Iec61850ReportDeliveryTracker.cs"));
        Assert.Contains("ReferenceEquals(state.LastAcceptedFrame, frame)", source);
        Assert.Contains("stream.LastAcceptedFrame = null;", source);
        Assert.Contains("state.LastAcceptedFrame = frame;", source);
    }

    [Fact]
    public void ColdStartCannotCertifyHistoricalReplayEvenWithEntryId()
    {
        var tracker = new Iec61850ReportDeliveryTracker();
        tracker.OnAssociated(5, Start.AddSeconds(70));
        Assert.Equal("Unknown", tracker.Classify(Frame(40, 73, "01"), 5).Label);
    }
}
