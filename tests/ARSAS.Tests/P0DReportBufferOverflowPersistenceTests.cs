using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Independently synthesized analogue of observed BRCB startup behavior:
/// first GI report advertises BufOvfl; subsequent SqNum values are contiguous.
/// </summary>
public sealed class P0DReportBufferOverflowPersistenceTests
{
    [Fact]
    public void FirstGiOverflowRemainsUnresolvedAfterTwelveHealthySequenceFrames()
    {
        var state = new Iec61850ReportContinuityState
        {
            ReportControlReference = "TESTLD/LLN0.BR.Buffer01",
            DataSetReference = "TESTLD/LLN0.DataSet_1",
            Buffered = true
        };
        var first = Iec61850ReportContinuityInspector.Observe(
            state, new NativeReportFrameMetadata
            {
                SequenceNumber = 0,
                BufferOverflow = true,
                GeneralInterrogationReasonSeen = true
            }, buffered: true);
        Assert.Contains(first, w => w.Contains("BufOvfl=true", StringComparison.Ordinal));
        for (ulong sq = 1; sq <= 12; sq++)
        {
            Assert.Empty(Iec61850ReportContinuityInspector.Observe(
                state, new NativeReportFrameMetadata
                {
                    SequenceNumber = sq,
                    BufferOverflow = false,
                    GeneralInterrogationReasonSeen = false
                }, buffered: true));
        }
        Assert.Equal(13L, state.FramesSeen);
        Assert.Equal(13L, state.SequencedFrames);
        Assert.Equal(0L, state.SequenceDiscontinuities);
        Assert.Equal(1L, state.BufferOverflows);
        Assert.Equal(12L, state.FramesAfterLastFinding);
        Assert.True(state.AnomalousFrames >= 1);
    }
}
