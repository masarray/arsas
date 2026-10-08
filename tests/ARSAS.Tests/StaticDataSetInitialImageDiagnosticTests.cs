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
