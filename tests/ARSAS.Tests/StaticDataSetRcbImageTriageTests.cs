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
    public void SilentSg7RcbRetainsExactNativeWriteResultWithoutPretendingItReceivedAReport()
    {
        const string datasetA = "SG7_BCUMaster/LLN0.TT6DataSet3";
        const string datasetI = "SG7_BCUMaster/LLN0.TT6DataSet4";
        const string rcbA = "SG7_BCUMaster/LLN0.BR.BrcbA01";
        const string rcbI = "SG7_BCUMaster/LLN0.BR.BrcbI01";
        var points = new[]
        {
            Point("A.phsA.cVal.mag.f", datasetA, rcbA, "120"),
            Point("A.phsB.cVal.mag.f", datasetI, rcbI)
        };
        var traffic = new Iec61850ReportContinuitySnapshot
        {
            Streams = new[]
            {
                new Iec61850ReportContinuityStreamSnapshot
                {
                    Rcb = rcbA, DataSet = datasetA, Frames = 1,
                    LastFrameReceivedAtUtc = DateTimeOffset.UtcNow,
                    RptEnaWriteAccepted = true, GiWriteAccepted = true
                }
            }
        };
        var activation = new[]
        {
            new Iec61850RcbActivationEvidence
            {
                Rcb = rcbA, DataSet = datasetA,
                SetupSucceeded = true, RptEnaWriteAccepted = true, GiWriteAccepted = true
            },
            new Iec61850RcbActivationEvidence
            {
                Rcb = rcbI.Replace('.', '$'), DataSet = datasetI.Replace('.', '$'),
                SetupSucceeded = true, RptEnaWriteAccepted = true, GiWriteAccepted = false,
                ActivationReturnedAtUtc = DateTimeOffset.UtcNow
            }
        };
        var rows = StaticDataSetRcbImageTriage.Evaluate(
            points, traffic, activations: activation);
        var active = Assert.Single(rows, row => row.Rcb == rcbA);
        var silent = Assert.Single(rows, row => row.Rcb == rcbI);

        Assert.Equal(1, active.Frames);
        Assert.Equal(0, silent.Frames);
        Assert.False(silent.HasActualReportFrames);
        Assert.Equal(1, silent.Pending);
        Assert.True(silent.SetupSucceeded);
        Assert.True(silent.SetupRptEnaWriteAccepted);
        Assert.False(silent.SetupGiWriteAccepted);
        Assert.Null(silent.RptEnaWriteAccepted); // 0-frame continuity is not receipt proof.
        Assert.Contains("activationCall=success", silent.Summary);
        Assert.Contains("setupRptEnaWrite=accepted", silent.Summary);
        Assert.Contains("setupGiWrite=rejected", silent.Summary);

        var log = new System.Text.StringBuilder();
        DiagnosticReportBuilder.AppendStaticRcbImageTriage(log, points, traffic, activation);
        Assert.Contains("routed=1, noRoutedFrames=1", log.ToString());
        Assert.Contains("actualFrames=0", log.ToString());
        Assert.Contains("setupGiWrite=rejected", log.ToString());
    }

    [Fact]
    public void WrongDatasetAmbiguousSetupAndAbsentAssociationCannotDonateWriteProof()
    {
        const string rcb = "IED/LLN0.BR.BrcbI01";
        const string ds = "IED/LLN0.TT6DataSet4";
        var point = Point("Hz.mag.f", ds, rcb);
        var wrong = new Iec61850RcbActivationEvidence
        {
            Rcb = rcb, DataSet = "IED/LLN0.OtherDataSet",
            SetupSucceeded = true, RptEnaWriteAccepted = true, GiWriteAccepted = true
        };
        var row = Assert.Single(StaticDataSetRcbImageTriage.Evaluate(
            [point], null, activations: [wrong]));
        Assert.Null(row.SetupSucceeded);
        Assert.Null(row.SetupRptEnaWriteAccepted);
        Assert.Null(row.SetupGiWriteAccepted);
        Assert.Contains("activationCall=unknown", row.Summary);

        var first = new Iec61850RcbActivationEvidence
        {
            Rcb = rcb, DataSet = ds, SetupSucceeded = true, GiWriteAccepted = true
        };
        var second = first with { GiWriteAccepted = false };
        row = Assert.Single(StaticDataSetRcbImageTriage.Evaluate(
            [point], null, activations: [first, second]));
        Assert.Null(row.SetupGiWriteAccepted); // Ambiguous same-key evidence is rejected.

        row = Assert.Single(StaticDataSetRcbImageTriage.Evaluate([point], null));
        Assert.Null(row.SetupSucceeded); // Association reset -> empty proof.
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
