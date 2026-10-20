using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Synthetic metadata only. This is not a physical SOE lossless claim.
/// </summary>
public sealed class P0FReportStreamIdentityTests
{
    private const string Rcb = "LABCTRL/LLN0.BR.Buffer01";
    private const string Dataset = "LABCTRL/LLN0.DataSet_1";

    [Fact]
    public void SameConcreteRcb_DoesNotSplitWhenRptIdIsMissingOrDataSetUsesMmsSeparator()
    {
        var metadataOnly = new NativeReportFrameMetadata
        {
            ReportControlReference = Rcb,
            DataSetReference = Dataset,
            ReportId = "",
            SequenceNumber = null
        };
        var decoded = new NativeReportFrameMetadata
        {
            ReportControlReference = "LABCTRL/LLN0$BR$Buffer01",
            DataSetReference = "LABCTRL/LLN0$DataSet_1",
            ReportId = "LAB/CTRL/LLN0$BR$Buffer",
            SequenceNumber = 1
        };

        var key = Iec61850ReportContinuityInspector.StreamKey(Rcb, metadataOnly);
        Assert.Equal(key, Iec61850ReportContinuityInspector.StreamKey(Rcb, decoded));

        var state = new Iec61850ReportContinuityState
        {
            ReportControlReference = Rcb,
            DataSetReference = Dataset,
            Buffered = true
        };
        Iec61850ReportContinuityInspector.Observe(
            state, new NativeReportFrameMetadata
            {
                SequenceNumber = 0, BufferOverflow = true,
                GeneralInterrogationReasonSeen = true
            }, buffered: true);
        for (var i = 0; i < 4; i++)
            Iec61850ReportContinuityInspector.Observe(state, metadataOnly, buffered: true);
        for (ulong sq = 1; sq <= 12; sq++)
            Iec61850ReportContinuityInspector.Observe(
                state, new NativeReportFrameMetadata
                {
                    ReportControlReference = decoded.ReportControlReference,
                    DataSetReference = decoded.DataSetReference,
                    ReportId = decoded.ReportId,
                    SequenceNumber = sq
                }, buffered: true);

        var snapshot = Iec61850ReportContinuityInspector.Snapshot(
            new Dictionary<string, Iec61850ReportContinuityState> { [key] = state },
            processUpdatesSeen: 126, untrackedStreamCount: 0);
        Assert.Equal(1, snapshot.StreamCount);
        Assert.Equal(17, snapshot.Frames);
        Assert.Equal(13, snapshot.Sequenced);
        Assert.Equal(1, snapshot.Overflows);
        Assert.Equal(4, Assert.Single(snapshot.Streams).SequenceMissing);
        Assert.Equal(decoded.ReportId, state.ReportId);
        Assert.Equal(12UL, state.LastSequenceNumber);
        Assert.Contains("DataSet_1", state.DataSetReference);
    }

    [Fact]
    public void DistinctRcbsStayIndependentEvenWhenRptIdAndDataSetMatch()
    {
        var first = new NativeReportFrameMetadata
        {
            ReportControlReference = Rcb,
            DataSetReference = Dataset,
            ReportId = "same"
        };
        var second = new NativeReportFrameMetadata
        {
            ReportControlReference = "LABCTRL/LLN0.BR.Buffer02",
            DataSetReference = Dataset,
            ReportId = "same"
        };
        Assert.NotEqual(Iec61850ReportContinuityInspector.StreamKey(Rcb, first),
            Iec61850ReportContinuityInspector.StreamKey(Rcb, second));
        Assert.Equal(string.Empty, Iec61850ReportContinuityInspector.StreamKey(
            null, new NativeReportFrameMetadata { DataSetReference = Dataset, ReportId = "rpt" }));
    }

    [Fact]
    public void ActualRptIdAndDataSetDriftAreReportedAsContinuityFindings()
    {
        var state = new Iec61850ReportContinuityState
        {
            ReportControlReference = Rcb,
            DataSetReference = Dataset,
            ReportId = "initial",
            Buffered = true
        };
        var warnings = Iec61850ReportContinuityInspector.Observe(
            state, new NativeReportFrameMetadata
            {
                ReportId = "changed",
                DataSetReference = "LABCTRL/LLN0.OtherDataSet",
                SequenceNumber = 0
            }, buffered: true);
        Assert.Contains(warnings, msg => msg.Contains("RptID changed", StringComparison.Ordinal));
        Assert.Contains(warnings, msg => msg.Contains("DataSet changed", StringComparison.Ordinal));
        Assert.True(state.AnomalousFrames >= 1);
        Assert.Equal("changed", state.ReportId);
    }
}
