using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticDataSetRcbImageTriageTests
{
    private static Iec61850MonitorPoint Point(
        string suffix, string dataSet, string rcb, string value = "-") => new()
    {
        DeviceId = "sg7", IecReference = "SG7_BCUMeter/MMXU1." + suffix,
        FunctionalConstraint = "MX", Value = value,
        DataSetReference = dataSet, ReportControlReference = rcb,
        Quality = "Unknown"
    };

    [Fact]
    public void OneActiveRcbNeverClaimsFramesGiOrImageForAnotherRcb()
    {
        const string datasetA = "SG7_BCUMaster/LLN0.TT6DataSet1";
        const string datasetI = "SG7_BCUMaster/LLN0.TT6DataSet4";
        const string rcbA = "SG7_BCUMaster/LLN0.BR.BrcbA01";
        const string rcbI = "SG7_BCUMaster/LLN0.BR.BrcbI01";
        var points = new[]
        {
            Point("A.phsA.cVal.mag.f", datasetA, rcbA, "120.0"),
            Point("A.phsB.cVal.mag.f", datasetI, rcbI),
            Point("A.phsC.cVal.mag.f", datasetI, rcbI),
        };
        var traffic = new Iec61850ReportContinuitySnapshot
        {
            Streams = new[]
            {
                new Iec61850ReportContinuityStreamSnapshot
                {
                    Rcb = rcbA, DataSet = datasetA, Frames = 4,
                    LastFrameReceivedAtUtc = new DateTimeOffset(
                        2026, 10, 23, 13, 0, 0, TimeSpan.Zero),
                    RptEnaWriteAccepted = true, GiWriteAccepted = true,
                    Overflow = 1
                }
            }
        };
        var rows = StaticDataSetRcbImageTriage.Evaluate(points, traffic);
        Assert.Equal(2, rows.Count);
        var a = Assert.Single(rows, row => row.Rcb == rcbA);
        var i = Assert.Single(rows, row => row.Rcb == rcbI);
        Assert.Equal(4, a.Frames);
        Assert.Equal(1, a.Visible);
        Assert.True(a.GiWriteAccepted);
        Assert.Equal(1, a.Overflow);
        Assert.Equal(0, i.Frames);
        Assert.Null(i.GiWriteAccepted);
        Assert.Null(i.RptEnaWriteAccepted);
        Assert.Equal(2, i.Pending);
        Assert.False(i.HasActualReportFrames);
        Assert.Contains("pending=2", i.Summary);
        Assert.Contains("GiWrite=unverified", i.Summary);
    }

    [Fact]
    public void WrongDataSetOrAmbiguousReportStreamsCannotSupplyProof()
    {
        const string rcb = "SG7_BCUMaster/LLN0.BR.BrcbI01";
        const string dataSet = "SG7_BCUMaster/LLN0.TT6DataSet4";
        var points = new[] { Point("A.phsA.cVal.mag.f", dataSet, rcb) };
        var wrong = new Iec61850ReportContinuitySnapshot
        {
            Streams = new[]
            {
                new Iec61850ReportContinuityStreamSnapshot
                {
                    Rcb = rcb, DataSet = "OtherDataSet",
                    Frames = 8, GiWriteAccepted = true
                }
            }
        };
        Assert.Equal(0, Assert.Single(StaticDataSetRcbImageTriage.Evaluate(
            points, wrong)).Frames);
        var ambiguous = new Iec61850ReportContinuitySnapshot
        {
            Streams = new[]
            {
                new Iec61850ReportContinuityStreamSnapshot
                {
                    Rcb = rcb, DataSet = dataSet, Frames = 1,
                    LastFrameReceivedAtUtc = new DateTimeOffset(
                        2026, 10, 23, 13, 1, 0, TimeSpan.Zero)
                },
                new Iec61850ReportContinuityStreamSnapshot
                {
                    Rcb = rcb, DataSet = dataSet, Frames = 2,
                    LastFrameReceivedAtUtc = new DateTimeOffset(
                        2026, 10, 23, 13, 2, 0, TimeSpan.Zero)
                }
            }
        };
        Assert.Equal(0, Assert.Single(StaticDataSetRcbImageTriage.Evaluate(
            points, ambiguous)).Frames);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StaticDataSetRcbImageTriage.Evaluate(points, null, maxGroups: 1000));
    }
}
