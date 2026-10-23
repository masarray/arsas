using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P0EReportStreamIdentityTests
{
    private static readonly ReportControlPlan Plan = new()
    {
        ReportControlReference = "SIEBCUCTRL/LLN0.BR.Buffer01",
        DataSetReference = "SIEBCUCTRL/LLN0.DataSet_1",
        Buffered = true
    };

    [Fact]
    public void SameBrcbAndDataSet_AliasesAndMissingRptIdNeverCreateSecondStream()
    {
        var unsequencedMetadata = new NativeReportFrameMetadata
        {
            ReportControlReference = "SIEBCUCTRL/LLN0.BR.Buffer01",
            DataSetReference = "SIEBCUCTRL/LLN0.DataSet_1",
            ReportId = ""
        };
        var sequencedWireReport = new NativeReportFrameMetadata
        {
            ReportControlReference = "SIEBCUCTRL/LLN0$BR$Buffer01",
            DataSetReference = "SIEBCUCTRL/LLN0$DataSet_1",
            ReportId = "SIEBCU/CTRL/LLN0$BR$Buffer",
            SequenceNumber = 25
        };
        Assert.Equal(
            Iec61850MonitorRuntime.BuildReportStreamKey(Plan, unsequencedMetadata),
            Iec61850MonitorRuntime.BuildReportStreamKey(Plan, sequencedWireReport));
        Assert.Equal(
            Iec61850MonitorRuntime.BuildReportStreamKey(Plan, unsequencedMetadata),
            Iec61850MonitorRuntime.BuildReportStreamKey(Plan,
                new NativeReportFrameMetadata()));
    }

    [Fact]
    public void DifferentActualRcbOrDataSet_RemainsAnIndependentContinuityEra()
    {
        var first = Iec61850MonitorRuntime.BuildReportStreamKey(
            Plan, new NativeReportFrameMetadata());
        var otherRcb = Iec61850MonitorRuntime.BuildReportStreamKey(
            Plan, new NativeReportFrameMetadata
            {
                ReportControlReference = "SIEBCUCTRL/LLN0.BR.Buffer02"
            });
        var changedDataSet = Iec61850MonitorRuntime.BuildReportStreamKey(
            Plan, new NativeReportFrameMetadata
            {
                DataSetReference = "SIEBCUCTRL/LLN0.DataSet_2"
            });
        Assert.NotEqual(first, otherRcb);
        Assert.NotEqual(first, changedDataSet);
    }
}
