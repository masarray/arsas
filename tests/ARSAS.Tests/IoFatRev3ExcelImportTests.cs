using System.IO.Compression;
using System.Security;
using System.Text;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatRev3ExcelImportTests
{
    private readonly IoListExcelImportService _importer = new();

    [Fact]
    public void EventLogReadyWorkbook_ImportsReadySdiAndPreservesDocumentTraceability()
    {
        var headers = Rev3Headers();
        var points = new[]
        {
            Row(headers,
                ("Point ID", "UCC-IEC-0001"), ("Include in FAT", "YES"),
                ("Source Sheet", "SoftTags_IEC61850"), ("Source Row", "216"),
                ("Location", "ELECT-CCPP"), ("Voltage Level", "66kV"),
                ("Switchgear", "261-SWG-51001"), ("Signal Description", "CB opened"),
                ("IED Type", "BCU - 6MD85"), ("IED Identifier / Technical Key", "AA1C1F03R4"),
                ("IP Address", "192.168.81.70"), ("Signal Alias", "CBOpnd"),
                ("Data Type", "SDI"), ("CDC", "SPS"), ("LD", "AA1C1F03R4Application"),
                ("LN", "GGIO6"), ("FC", "ST"),
                ("IEC 61850 Reference (Source)", "ADD/GGIO6.CBOpnd"),
                ("Data Attribute", "stVal"),
                ("Report Display Reference", "AA1C1F03R4Application/ADD/GGIO6.CBOpnd.stVal [ST]"),
                ("Event Log Search Reference", "ADD/GGIO6.CBOpnd"),
                ("State 0/01 Text (Source)", "Normal"),
                ("State 1/10 Text (Source)", "Operated"),
                ("Evidence Expected", "Event Log"), ("Mapping Quality", "COMPLETE"),
                ("Review Status", "READY"), ("Review Reason", "No blocking issue detected")),
            Row(headers,
                ("Point ID", "UCC-IEC-0002"), ("Include in FAT", "YES"),
                ("Source Sheet", "SoftTags_IEC61850"), ("Source Row", "262"),
                ("Signal Description", "Phase L1-L2 Voltage"),
                ("IED Identifier / Technical Key", "AA1C1F03R4"),
                ("IP Address", "192.168.81.70"), ("Data Type", "SAI"),
                ("FC", "MX"), ("IEC 61850 Reference (Source)", "V_MMXU1.PhV.phsAB"),
                ("State 0/01 Text (Source)", "Normal"),
                ("State 1/10 Text (Source)", "Operated"), ("Review Status", "READY")),
            Row(headers,
                ("Point ID", "UT2-IEC-0001"), ("Include in FAT", "REVIEW"),
                ("Source Sheet", "SoftTags_IEC61850 (Utility-2)"), ("Source Row", "5"),
                ("Signal Description", "CB Open command from PMS"),
                ("IED Identifier / Technical Key", "REX615A"), ("IP Address", "NA"),
                ("Data Type", "SDI"), ("FC", "TBA"),
                ("IEC 61850 Reference (Source)", "TBA"),
                ("State 0/01 Text (Source)", "Normal"),
                ("State 1/10 Text (Source)", "Operated"),
                ("Review Status", "BLOCKED – REFERENCE MISSING"))
        };

        var documentControl = new[]
        {
            new[] { "Document Control Item", "Value", "Assessment" },
            new[] { "Source file name", "V-2181-801-A-EIC-154_02_Source.xlsx", "" },
            new[] { "Project shown on front sheet", "Tangguh UCC Project - Onshore EPCI", "" },
            new[] { "Supplier shown on front sheet", "PT ABB Sakti Industri", "" },
            new[] { "Purchase Order Title", "Power Management System (PMS)", "" },
            new[] { "Purchaser Document No.", "V-2181-801-A-EIC-154", "" },
            new[] { "Company Project Document No.", "UCC-2181801A-E99-0001", "" },
            new[] { "Document title", "PMS Hard Tag and Soft Tag List", "" },
            new[] { "Overall document-control status", "REVIEW REQUIRED", "" }
        };
        var workbook = BuildWorkbook(headers, points, documentControl);

        var result = _importer.Import(workbook, "IEC61850_FAT_Import_Rev3_EventLogReady.xlsx");

        Assert.True(result.IsValid);
        Assert.Equal(1, result.ParsedRowCount);
        Assert.Single(result.Project.Ieds);
        Assert.Equal("UCC-2181801A-E99-0001", result.Project.ProjectId);
        Assert.Equal("Tangguh UCC Project - Onshore EPCI", result.Project.ProjectName);
        Assert.Equal("02", result.Project.DocumentControl.Revision);
        Assert.Equal("PT ABB Sakti Industri", result.Project.DocumentControl.SupplierName);
        Assert.Equal("REVIEW REQUIRED", result.Project.DocumentControl.IssueStatus);

        var point = Assert.Single(result.Project.Ieds[0].TestPoints);
        Assert.Equal("ADD/GGIO6.CBOpnd", point.SourceIecReference);
        Assert.Equal("ADD/GGIO6.CBOpnd", point.EventLogSearchReference);
        Assert.Equal("ADD/GGIO6.CBOpnd", point.ReportIecReference);
        Assert.Equal("AA1C1F03R4Application/ADD/GGIO6.CBOpnd.stVal", point.ObjectReference);
        Assert.Equal("Operated", point.ExpectedOnText);
        Assert.Equal("Normal", point.ExpectedOffText);
        Assert.Equal("Event Log", point.EvidenceExpected);

        Assert.Contains(result.ParserFindings, finding => finding.Code == "XLSX_NON_SDI_SKIPPED");
        Assert.Contains(result.ParserFindings, finding => finding.Code == "XLSX_BLOCKED_ROWS_SKIPPED");
        Assert.Contains(result.ParserFindings, finding => finding.Code == "DOCUMENT_CONTROL_REVIEW_REQUIRED");
    }

    private static string[] Rev3Headers() =>
    [
        "Point ID", "Include in FAT", "Test Mode", "Source Scope", "Location",
        "Voltage Level", "Switchgear", "Panel Tag", "Object Name", "Equipment Description",
        "PMS Typical", "Signal No.", "Signal Description", "IED Type",
        "IED Identifier / Technical Key", "IP Address", "Signal Alias", "Interface",
        "Data Type", "Alarm", "Event", "Trend", "Alarm Priority", "Alarm Severity",
        "Engineering Unit", "From Equipment", "To Equipment", "State 0/01 Text (Source)",
        "State 1/10 Text (Source)", "Dataset", "RCB/GCB", "CDC", "LD", "LN", "FC",
        "IEC 61850 Reference (Source)", "Data Attribute", "Report Display Reference",
        "Event Log Search Reference", "Unique Event Point Key", "Evidence Expected",
        "Mapping Quality", "Review Status", "Review Reason", "Duplicate Key Count",
        "Source Sheet", "Source Row", "FAT Result", "Actual / Observed", "Event Timestamp",
        "Event Log Match", "Evidence Ref / Screenshot", "Reviewer Comment", "Source Remarks"
    ];

    private static string[] Row(string[] headers, params (string Header, string Value)[] values)
    {
        var lookup = values.ToDictionary(item => item.Header, item => item.Value, StringComparer.OrdinalIgnoreCase);
        return headers.Select(header => lookup.TryGetValue(header, out var value) ? value : string.Empty).ToArray();
    }

    private static byte[] BuildWorkbook(
        string[] pointHeaders,
        IEnumerable<string[]> pointRows,
        IEnumerable<string[]> documentControlRows)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets>" +
                "<sheet name=\"FAT_Points_Import\" sheetId=\"1\" r:id=\"rId1\"/>" +
                "<sheet name=\"Document_Control\" sheetId=\"2\" r:id=\"rId2\"/>" +
                "</sheets></workbook>");
            Write(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
                "</Relationships>");

            var allPointRows = new List<string[]> { pointHeaders };
            allPointRows.AddRange(pointRows);
            Write(archive, "xl/worksheets/sheet1.xml", BuildSheet(allPointRows));
            Write(archive, "xl/worksheets/sheet2.xml", BuildSheet(documentControlRows));
        }
        return memory.ToArray();
    }

    private static string BuildSheet(IEnumerable<string[]> rows)
    {
        var allRows = rows.ToList();
        var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var rowIndex = 0; rowIndex < allRows.Count; rowIndex++)
        {
            sheet.Append("<row r=\"").Append(rowIndex + 1).Append("\">");
            for (var columnIndex = 0; columnIndex < allRows[rowIndex].Length; columnIndex++)
            {
                var value = SecurityElement.Escape(allRows[rowIndex][columnIndex]) ?? string.Empty;
                sheet.Append("<c r=\"").Append(ColumnName(columnIndex)).Append(rowIndex + 1)
                    .Append("\" t=\"inlineStr\"><is><t>").Append(value).Append("</t></is></c>");
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");
        return sheet.ToString();
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ColumnName(int zeroBasedIndex)
    {
        var index = zeroBasedIndex + 1;
        var name = string.Empty;
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }
        return name;
    }
}
