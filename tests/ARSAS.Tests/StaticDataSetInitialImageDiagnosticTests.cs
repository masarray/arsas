using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticDataSetInitialImageDiagnosticTests
{
    [Fact]
    public void PartialImage_SeparatesMissingValuesFromMissingQuality()
    {
        var image = StaticDataSetInitialImageDiagnostic.Evaluate(new[]
        {
            Point("IED/LLN0.Binary1.stVal", "IED/LLN0.Digital", "IED/LLN0.BR.01", "-", "Unknown"),
            Point("IED/LLN0.Binary2.stVal", "IED/LLN0.Digital", "IED/LLN0.BR.01", "Open [01]", "Pending / q not supplied"),
            Point("IED/MMXU1.A.phsA.cVal.mag.f", "IED/LLN0.Analog", "IED/LLN0.RP.01", "0", "questionable")
        });

        Assert.Equal(3, image.Selected);
        Assert.Equal(2, image.ValueVisible);
        Assert.Equal(1, image.ValuePending);
        Assert.Equal(2, image.QualityNotSupplied);
        Assert.Equal(1, image.QuestionableQuality);
        Assert.Equal(2, image.Groups.Count);
        Assert.Equal("IED/LLN0.Binary1.stVal", image.PendingPoints.Single().Reference);
        Assert.Contains("displayed=2/3", image.Summary);
    }

    [Fact]
    public void NewReportImage_MonotonicallyResolvesPendingValueWithoutPretendingQualityIsGood()
    {
        var point = Point("IED/LLN0.Switch.stVal", "IED/LLN0.Digital", "IED/LLN0.BR.01", "Unknown", "Unknown");
        var before = StaticDataSetInitialImageDiagnostic.Evaluate([point]);
        Assert.Equal(1, before.ValuePending);
        Assert.Equal(1, before.QualityNotSupplied);

        point.Value = "Closed [10]";
        var afterValue = StaticDataSetInitialImageDiagnostic.Evaluate([point]);
        Assert.Equal(0, afterValue.ValuePending);
        Assert.Equal(1, afterValue.QualityNotSupplied);

        point.Quality = "questionable";
        var afterQuality = StaticDataSetInitialImageDiagnostic.Evaluate([point]);
        Assert.Equal(0, afterQuality.QualityNotSupplied);
        Assert.Equal(1, afterQuality.QuestionableQuality);
    }

    [Fact]
    public void GroupingPreservesExactDataSetAndConcreteRcbSeparation()
    {
        var image = StaticDataSetInitialImageDiagnostic.Evaluate(new[]
        {
            Point("IED/LLN0.One.stVal", "IED/LLN0$Events", "IED/LLN0$BR$Rpt01", "1", "Good"),
            Point("IED/LLN0.Two.stVal", "IED/LLN0$Events", "IED/LLN0$BR$Rpt02", "-", "Unknown")
        });
        Assert.Equal(2, image.Groups.Count);
        Assert.Equal(1, image.ValueVisible);
        Assert.Equal(1, image.ValuePending);
        Assert.Contains(image.Groups, group => group.RcbReference == "IED/LLN0.BR.Rpt01" && group.ValueVisible == 1);
        Assert.Contains(image.Groups, group => group.RcbReference == "IED/LLN0.BR.Rpt02" && group.ValuePending == 1);
    }

    [Fact]
    public void CopyDiagnostic_ShowsSilentRcbWithoutBorrowingTheReceivingRcbsProof()
    {
        const string rcbA = "SG7_BCUMaster/LLN0.BR.BrcbA01";
        const string rcbI = "SG7_BCUMaster/LLN0.BR.BrcbI01";
        const string dataSetA = "SG7_BCUMaster/LLN0.TT6DataSet3";
        const string dataSetI = "SG7_BCUMaster/LLN0.TT6DataSet4";
        var points = new[]
        {
            Point("SG7_BCUMeter/MMXU1.Hz.mag.f", dataSetA, rcbA, "50", "Good"),
            Point("SG7_BCUMeter/MMXU1.PhV.phsA.cVal.mag.f", dataSetI, rcbI, "-", "Unknown")
        };
        var continuity = new Iec61850ReportContinuitySnapshot
        {
            Streams = new[]
            {
                new Iec61850ReportContinuityStreamSnapshot
                {
                    Rcb = rcbA, DataSet = dataSetA, Frames = 1,
                    LastFrameReceivedAtUtc = DateTimeOffset.UtcNow,
                    RptEnaWriteAccepted = true, GiWriteAccepted = true
                }
            }
        };

        var builder = new System.Text.StringBuilder();
        DiagnosticReportBuilder.AppendStaticRcbImageTriage(builder, points, continuity);
        var report = builder.ToString();

        Assert.Contains("routed=1, noRoutedFrames=1", report, StringComparison.Ordinal);
        Assert.Contains("initial image 1/1", report, StringComparison.Ordinal);
        Assert.Contains("initial image 0/1", report, StringComparison.Ordinal);
        Assert.Contains("actualFrames=1, RptEnaWrite=accepted, GiWrite=accepted", report, StringComparison.Ordinal);
        Assert.Contains("actualFrames=0, RptEnaWrite=unverified, GiWrite=unverified", report, StringComparison.Ordinal);
        Assert.Contains("ingress=NO ROUTED FRAMES", report, StringComparison.Ordinal);

        builder.Clear();
        DiagnosticReportBuilder.AppendStaticRcbImageTriage(builder, points, null);
        Assert.Contains("routed=0, noRoutedFrames=2", builder.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("RptEnaWrite=accepted", builder.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AuditIsReadOnlyAndDiagnosticDoesNotInjectMmsAcquisition()
    {
        var path = FindRepoFile("Services/StaticDataSetInitialImageDiagnostic.cs");
        var source = File.ReadAllText(path);
        var diagnostic = File.ReadAllText(FindRepoFile("Services/DiagnosticReportBuilder.cs"));
        Assert.Contains("Static initial image: ", diagnostic, StringComparison.Ordinal);
        Assert.Contains("AppendStaticInitialImage(builder, device.Points)", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device)", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartReport", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsClient", source, StringComparison.Ordinal);
    }

    private static Iec61850MonitorPoint Point(
        string reference, string dataset, string rcb, string value, string quality)
        => new()
        {
            DeviceId = "device-a",
            IecReference = reference,
            DataSetReference = dataset,
            ReportControlReference = rcb,
            Value = value,
            Quality = quality,
            Status = value == "-" || value == "Unknown" ? "Awaiting first static report" : "Live"
        };

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(path))
                return path;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
