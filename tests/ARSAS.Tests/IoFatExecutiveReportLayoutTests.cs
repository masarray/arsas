using System.Text;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatExecutiveReportLayoutTests
{
    [Fact]
    public void ExecutivePdf_ContainsDocumentControlExactTelegramAndHandoverSignoff()
    {
        var project = new IoTestProject
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
                IssueStatus = "AS TESTED"
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
                            ExpectedOnText = "Operated",
                            ExpectedOffText = "Normal",
                            TestEnabled = true,
                            ImportReady = true
                        }
                    ]
                }
            ]
        };

        var bytes = IoFatPdfReportService.Generate(
            project,
            new DateTimeOffset(2026, 7, 30, 14, 0, 0, TimeSpan.FromHours(7)));
        var text = Encoding.ASCII.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.Contains("V-2181-801-A-EIC-154", text, StringComparison.Ordinal);
        Assert.Contains("REV 02", text, StringComparison.Ordinal);
        Assert.Contains("ADD/GGIO6.CBOpnd", text, StringComparison.Ordinal);
        Assert.Contains("EVENT-LOG CORRELATION", text, StringComparison.Ordinal);
        Assert.Contains("CLIENT WITNESS", text, StringComparison.Ordinal);
        Assert.Contains("IED timestamp format yyyy-MM-dd HH:mm:ss.fff", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutiveLayoutContract_IsCleanButAuditDefensible()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatExecutiveReportLayoutEngine.cs"));

        Assert.Contains("IEC 61850 / event-log reference", source, StringComparison.Ordinal);
        Assert.Contains("point.ReportIecReference", source, StringComparison.Ordinal);
        Assert.Contains("DOCUMENT CONTROL", source, StringComparison.Ordinal);
        Assert.Contains("FALSE > TRUE > FALSE", source, StringComparison.Ordinal);
        Assert.Contains("TESTED BY", source, StringComparison.Ordinal);
        Assert.Contains("CHECKED BY", source, StringComparison.Ordinal);
        Assert.Contains("CLIENT WITNESS", source, StringComparison.Ordinal);
        Assert.Contains("APPROVED BY", source, StringComparison.Ordinal);
        Assert.Contains("yyyy-MM-dd\\nHH:mm:ss.fff", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CharacterEllipsis", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" + \"...\"", source, StringComparison.Ordinal);
    }

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
