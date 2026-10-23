using System.Text;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatExecutiveReportLayoutTests
{
    [Fact]
    public void ExecutivePdf_ContainsDocumentControlExactTelegramAndHandoverSignoff()
    {
        var project = BuildProject("REVIEW REQUIRED", "Operated", "Normal");

        var bytes = IoFatPdfReportService.Generate(
            project,
            new DateTimeOffset(2026, 7, 30, 14, 0, 0, TimeSpan.FromHours(7)));
        var text = Encoding.ASCII.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.Contains("V-2181-801-A-EIC-154", text, StringComparison.Ordinal);
        Assert.Contains("REV 02", text, StringComparison.Ordinal);
        Assert.Contains("AS TESTED", text, StringComparison.Ordinal);
        Assert.DoesNotContain("REVIEW REQUIRED", text, StringComparison.Ordinal);
        Assert.Contains("ADD/GGIO6.CBOpnd", text, StringComparison.Ordinal);
        Assert.Contains("EVENT-LOG CORRELATION", text, StringComparison.Ordinal);
        Assert.Contains("CLIENT WITNESS", text, StringComparison.Ordinal);
        Assert.Contains("IED timestamp format yyyy-MM-dd HH:mm:ss.fff", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutivePdf_ReplacesUnknownSourceStateLabelsWithBooleanStates()
    {
        var project = BuildProject("REVIEW REQUIRED", "TBA", "N/A");

        var bytes = IoFatPdfReportService.Generate(project);
        var text = Encoding.ASCII.GetString(bytes);

        Assert.Contains("TRUE", text, StringComparison.Ordinal);
        Assert.Contains("FALSE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TBA (True)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("N/A (False)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutiveLayoutContract_IsCleanButAuditDefensible()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatExecutiveReportLayoutEngine.cs"));
        var preview = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatReportPreviewDocumentBuilder.cs"));
        var typography = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatReportTypography.cs"));

        Assert.Contains("IEC 61850 / event-log reference", source, StringComparison.Ordinal);
        Assert.Contains("point.ReportIecReference", source, StringComparison.Ordinal);
        Assert.Contains("DOCUMENT CONTROL", source, StringComparison.Ordinal);
        Assert.Contains("FALSE > TRUE > FALSE", source, StringComparison.Ordinal);
        Assert.Contains("TESTED BY", source, StringComparison.Ordinal);
        Assert.Contains("CHECKED BY", source, StringComparison.Ordinal);
        Assert.Contains("CLIENT WITNESS", source, StringComparison.Ordinal);
        Assert.Contains("APPROVED BY", source, StringComparison.Ordinal);
        Assert.Contains("yyyy-MM-dd\\nHH:mm:ss.fff", source, StringComparison.Ordinal);
        Assert.Contains("IoFatReportFontKind.Bold, 16.8d", source, StringComparison.Ordinal);
        Assert.Contains("var issueStatus = _draft ? \"PREVIEW\" : \"AS TESTED\"", source, StringComparison.Ordinal);
        Assert.Contains("IoFatReportTypography.PreviewFontFamily", preview, StringComparison.Ordinal);
        Assert.Contains("PreferredFamilyName = \"Inter\"", typography, StringComparison.Ordinal);
        Assert.Contains("FallbackFamilyName = \"Segoe UI\"", typography, StringComparison.Ordinal);
        Assert.DoesNotContain("Aptos", typography, StringComparison.Ordinal);
        Assert.DoesNotContain("Arial", typography, StringComparison.Ordinal);
        Assert.DoesNotContain("DEVICE RESULT", source, StringComparison.Ordinal);
        Assert.DoesNotContain("reviewIssue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterEllipsis", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" + \"...\"", source, StringComparison.Ordinal);
    }

    private static IoTestProject BuildProject(string issueStatus, string expectedOn, string expectedOff) => new()
    {
        ProjectId = "UCC-2181801A-E99-0001",
        SchemaVersion = IoTestImportValidator.SupportedSchemaVersion,
        ProjectName = "Tangguh UCC Project - Onshore EPCI",
        SourceWorkbookName = "IEC61850_FAT_Import_Rev3_EventLogReady.xlsx",
        SourceWorkbookSha256 = new string('a', 64),
        DocumentControl = new IoFatDocumentControl
        {
            ClientProject = "Tangguh UCC Project - Onshore EPCI",
            SupplierName = "PT ABB Sakti Industri",
            PurchaseOrderTitle = "Power Management System (PMS)",
            PurchaserDocumentNumber = "V-2181-801-A-EIC-154",
            CompanyProjectDocumentNumber = "UCC-2181801A-E99-0001",
            Revision = "02",
            IssueStatus = issueStatus
        },
        Ieds =
        [
            new IoTestIedPlan
            {
                IedName = "AA1C1F03R4",
                IpAddress = "192.168.81.70",
                IedRole = "BCU - 6MD85",
                Location = "ELECT-CCPP",
                VoltageLevel = "66kV",
                Switchgear = "261-SWG-51001",
                TestPoints =
                [
                    new IoTestPointPlan
                    {
                        TestPointId = "UCC-IEC-0001",
                        IedName = "AA1C1F03R4",
                        IpAddress = "192.168.81.70",
                        SignalName = "CB opened",
                        ObjectReference = "AA1C1F03R4Application/ADD/GGIO6.CBOpnd.stVal",
                        FunctionalConstraint = "ST",
                        SourceIecReference = "ADD/GGIO6.CBOpnd",
                        EventLogSearchReference = "ADD/GGIO6.CBOpnd",
                        ExpectedOnText = expectedOn,
                        ExpectedOffText = expectedOff,
                        TestEnabled = true,
                        ImportReady = true
                    }
                ]
            }
        ]
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
