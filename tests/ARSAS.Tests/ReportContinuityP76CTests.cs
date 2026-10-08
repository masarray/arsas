using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ReportContinuityP76CTests
{
    [Fact]
    public void StandardIncrementAndUint16Rollover_AreNotFalseDiscontinuities()
    {
        var s = new Iec61850ReportContinuityState();
        Assert.Empty(Observe(s, ushort.MaxValue));
        Assert.Empty(Observe(s, 0));
        Assert.Empty(Observe(s, 1));
        Assert.Equal(1UL, s.LastSequenceNumber);
    }

    [Fact]
    public void ArbitraryResetToZero_IsNotMistakenForCounterRollover()
    {
        var s = new Iec61850ReportContinuityState();
        Assert.Empty(Observe(s, 4));
        var warnings = Observe(s, 0);
        Assert.Contains(warnings, w => w.Contains("backward/reset/replay", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("no event-loss conclusion", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(4, 1, "backward/reset/replay")]
    [InlineData(3, 1, "backward/reset/replay")]
    [InlineData(2, 5, "forward gap")]
    [InlineData(8, 8, "duplicate/replay")]
    public void RealFieldDiscontinuityShapes_ArePreservedAsUnverifiedEvidence(
        ulong previous, ulong current, string expected)
    {
        var s = new Iec61850ReportContinuityState();
        Observe(s, previous, buffered: true, entryId: "aabb");
        var warnings = Observe(s, current, buffered: true, entryId: "ccdd");
        Assert.Contains(warnings, w => w.Contains(expected, StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("previousPresent=True", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("changed=True", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Contains("events were lost", StringComparison.Ordinal));
    }

    [Fact]
    public void FirstObservedNonzeroReport_IsNotAssumedMissing()
    {
        var s = new Iec61850ReportContinuityState();
        Assert.Empty(Observe(s, 203, buffered: true, entryId: "010203"));
        Assert.Equal(203UL, s.LastSequenceNumber);
    }

    [Fact]
    public void FullSegmentedReport_SharesSqNumAndStartsAtSubSqNumZero()
    {
        var s = new Iec61850ReportContinuityState();
        Assert.Empty(Observe(s, 4, sub: 0, more: true));
        Assert.Empty(Observe(s, 4, sub: 1, more: true));
        Assert.Empty(Observe(s, 4, sub: 2, more: false));
        Assert.Empty(Observe(s, 5));
        Assert.Equal(5UL, s.LastSequenceNumber);
    }

    [Fact]
    public void MissingFirstSegmentAndMissingContinuation_AreBothVisible()
    {
        var first = new Iec61850ReportContinuityState();
        Assert.Contains(Observe(first, 5, sub: 1, more: true),
            w => w.Contains("starts at SubSqNum=1", StringComparison.Ordinal));

        var s = new Iec61850ReportContinuityState();
        Assert.Empty(Observe(s, 6, sub: 0, more: true));
        Assert.Contains(Observe(s, 6, sub: 2, more: false),
            w => w.Contains("Segmented report discontinuity", StringComparison.Ordinal));
    }

    [Fact]
    public void UnexpectedUnsegmentedFrame_ReportsInterruptedSegment()
    {
        var s = new Iec61850ReportContinuityState();
        Assert.Empty(Observe(s, 7, sub: 0, more: true));
        Assert.Contains(Observe(s, 8), w =>
            w.Contains("Segmented report was interrupted", StringComparison.Ordinal));
    }

    [Fact]
    public void BufferOverflowAndConfRevChange_AreNotErasedBySequentialSqNum()
    {
        var s = new Iec61850ReportContinuityState();
        var initial = new NativeReportFrameMetadata
        {
            SequenceNumber = 0, ConfRev = 1, EntryIdHex = "aa"
        };
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(s, initial, true));
        var next = new NativeReportFrameMetadata
        {
            SequenceNumber = 1, ConfRev = 2, BufferOverflow = true, EntryIdHex = "bb"
        };
        var warnings = Iec61850ReportContinuityInspector.Observe(s, next, true);
        Assert.Contains(warnings, w => w.Contains("BufOvfl=true", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("ConfRev changed", StringComparison.Ordinal));
        Assert.Equal("bb", s.LastEntryIdHex);
    }

    [Fact]
    public void OutOfRangeSqNum_DoesNotPoisonValidBaseline()
    {
        var s = new Iec61850ReportContinuityState();
        Observe(s, 11);
        var warnings = Observe(s, 65536);
        Assert.Contains(warnings, w => w.Contains("exceeds IEC 61850 INT16U", StringComparison.Ordinal));
        Assert.Equal(11UL, s.LastSequenceNumber);
        Assert.Empty(Observe(s, 12));
    }

    [Fact]
    public void MissingOptionalSqNum_DoesNotManufactureSequenceEvidence()
    {
        var s = new Iec61850ReportContinuityState();
        Assert.Empty(Iec61850ReportContinuityInspector.Observe(s,
            new NativeReportFrameMetadata { EntryIdHex = "0102" }, true));
        Assert.Null(s.LastSequenceNumber);
        Assert.Equal("0102", s.LastEntryIdHex);
    }

    [Fact]
    public void SeparateRcbAndAssociationStates_DoNotBorrowContinuity()
    {
        var a = new Iec61850ReportContinuityState();
        var b = new Iec61850ReportContinuityState();
        Observe(a, 32);
        Assert.Empty(Observe(b, 1));
        Assert.Null(new Iec61850ReportContinuityState().LastSequenceNumber);
        Assert.Equal(32UL, a.LastSequenceNumber);
        Assert.Equal(1UL, b.LastSequenceNumber);
    }

    [Fact]
    public void ReadOnlyInspector_DoesNotChangeReportingOrPollingContracts()
    {
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("Iec61850ReportContinuityInspector.Observe", runtime, StringComparison.Ordinal);
        Assert.Contains("session.ReportStreams.Clear()", runtime, StringComparison.Ordinal);
        Assert.Contains("StaticAcquisitionParityTracker.TryRecordRoutedReport", runtime, StringComparison.Ordinal);
        var helper = Read("Services/Iec61850ReportContinuityInspector.cs");
        Assert.DoesNotContain("ReadMms", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteMms", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("StartReport", helper, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Observe(
        Iec61850ReportContinuityState s,
        ulong sequence,
        bool buffered = false,
        ulong? sub = null,
        bool? more = null,
        string entryId = "")
        => Iec61850ReportContinuityInspector.Observe(s, new NativeReportFrameMetadata
        {
            SequenceNumber = sequence,
            SubSequenceNumber = sub,
            MoreSegmentsFollow = more,
            EntryIdHex = entryId
        }, buffered);

    private static string Read(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var found = Path.Combine(dir.FullName, path);
            if (File.Exists(found))
                return File.ReadAllText(found);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
