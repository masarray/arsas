using System.Text;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatRemoveReportScopeRegressionTests
{
    [Theory]
    [InlineData("ARSAS-FAT-SCL-2.0")]
    [InlineData("ARSAS-FAT-IO-1.0")]
    public void RemoveFromFat_IsHardExclusionForNativeReport_WithoutMutatingSharedOrTestSelection(string schemaVersion)
    {
        var removed = BuildPoint("REMOVE_ME", "IED1LD/GGIO1.Ind1.stVal");
        var kept = BuildPoint("KEEP_ME", "IED1LD/GGIO1.Ind2.stVal");
        var ied = new IoTestIedPlan
        {
            IedName = "IED1",
            IpAddress = "192.0.2.10",
            TestPoints = new List<IoTestPointPlan> { removed, kept }
        };
        var project = new IoTestProject
        {
            ProjectId = "REMOVE-REPORT-REGRESSION",
            SchemaVersion = schemaVersion,
            ProjectName = "Remove report regression",
            Ieds = new List<IoTestIedPlan> { ied }
        };

        removed.RemoveFromFat();

        Assert.True(removed.WorkspaceSelected);
        Assert.True(removed.TestEnabled);
        Assert.False(removed.IsIncludedInFat);

        var pdf = IoFatPdfReportService.Generate(
            project,
            new DateTimeOffset(2026, 9, 3, 4, 30, 0, TimeSpan.Zero));
        var reportText = Encoding.ASCII.GetString(pdf);

        Assert.DoesNotContain("REMOVE_ME", reportText, StringComparison.Ordinal);
        Assert.Contains("KEEP_ME", reportText, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreToFat_ReturnsSameSharedSignalToReport_WithTestPreferencePreserved()
    {
        var point = BuildPoint("RESTORE_ME", "IED1LD/GGIO1.Ind1.stVal");
        var ied = new IoTestIedPlan
        {
            IedName = "IED1",
            IpAddress = "192.0.2.10",
            TestPoints = new List<IoTestPointPlan> { point }
        };
        var project = new IoTestProject
        {
            ProjectId = "RESTORE-REPORT-REGRESSION",
            SchemaVersion = "ARSAS-FAT-SCL-2.0",
            ProjectName = "Restore report regression",
            Ieds = new List<IoTestIedPlan> { ied }
        };

        point.TestEnabled = false;
        point.RemoveFromFat();
        point.RestoreToFat();

        Assert.True(point.WorkspaceSelected);
        Assert.False(point.TestEnabled);
        Assert.True(point.IsIncludedInFat);

        var pdf = IoFatPdfReportService.Generate(
            project,
            new DateTimeOffset(2026, 9, 3, 4, 31, 0, TimeSpan.Zero));
        var reportText = Encoding.ASCII.GetString(pdf);
        Assert.Contains("RESTORE_ME", reportText, StringComparison.Ordinal);
    }

    [Fact]
    public void PrintPreview_UsesSameScopedLayoutContractAsNativePdf()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatReportPreviewDocumentBuilder.cs"));
        var reportService = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatPdfReportService.cs"));
        var scope = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatReportScope.cs"));

        Assert.Contains("IoFatPdfReportService.BuildLayout", source, StringComparison.Ordinal);
        Assert.Contains("IoFatReportScope.Create(project)", reportService, StringComparison.Ordinal);
        Assert.Contains("point.WorkspaceSelected && point.IsIncludedInFat", scope, StringComparison.Ordinal);
    }

    private static IoTestPointPlan BuildPoint(string signalName, string reference)
        => new()
        {
            TestPointId = signalName,
            IedName = "IED1",
            IpAddress = "192.0.2.10",
            SignalName = signalName,
            ObjectReference = reference,
            FunctionalConstraint = "ST",
            ExpectedOnText = "TRUE",
            ExpectedOffText = "FALSE",
            WorkspaceSelected = true,
            TestEnabled = true,
            ImportReady = true
        };

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
