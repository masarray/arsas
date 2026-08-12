using System.IO.Compression;
using System.Security;
using System.Text;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoListExcelImportServiceTests
{
    private readonly IoListExcelImportService _importer = new();

    [Fact]
    public void GeneratedContractWorkbook_ImportsAndGroupsSignalsPerIed()
    {
        var headers = RequiredHeaders();
        var rows = new[]
        {
            Row(headers,
                ("ProjectId", "CCPP-260728"), ("SchemaVersion", "ARSAS-FAT-IO-1.0"),
                ("TestPointId", "TP-001"), ("TestEnabled", "true"), ("ImportReady", "true"),
                ("BindingStatus", "CID_DATASET_EXACT"), ("BindingEvidence", "FCDA DataSet Digital"),
                ("SourceSheet", "SoftTags_IEC61850"), ("SourceRow", "12"),
                ("IEDName", "AA1C1F03R4"), ("IPAddress", "192.168.81.70"), ("IEDRole", "BCU - 6MD85"),
                ("Location", "ELECT-CCPP"), ("VoltageLevel", "66kV"), ("Switchgear", "261-SWG-51001"),
                ("SignalName", "CB closed"), ("SignalAddress", "CBClsd"), ("DataType", "SDI"),
                ("LDInst", "ADD"), ("LN", "GGIO6"), ("FC", "ST"), ("DO", "CBClsd"), ("DA", "stVal"),
                ("ObjectReference", "AA1C1F03R4ADD/GGIO6.CBClsd.stVal"), ("DataSetName", "Digital"),
                ("ExpectedONRaw", "1"), ("ExpectedONText", "Active"),
                ("ExpectedOFFRaw", "0"), ("ExpectedOFFText", "InActive")),
            Row(headers,
                ("ProjectId", "CCPP-260728"), ("SchemaVersion", "ARSAS-FAT-IO-1.0"),
                ("TestPointId", "TP-002"), ("TestEnabled", "true"), ("ImportReady", "true"),
                ("BindingStatus", "CID_DATASET_EXACT"), ("SourceSheet", "SoftTags_IEC61850"), ("SourceRow", "237"),
                ("IEDName", "AA1C1F03R3"), ("IPAddress", "192.168.81.69"), ("IEDRole", "IED - 7SX80"),
                ("SignalName", "Protection operated (51)"), ("SignalAddress", "Op.general"), ("DataType", "SDI"),
                ("LDInst", "VI3p1_5051OC3phase1"), ("LN", "II_PTOC1"), ("FC", "ST"), ("DO", "Op"), ("DA", "general"),
                ("ObjectReference", "AA1C1F03R3VI3p1_5051OC3phase1/II_PTOC1.Op.general"),
                ("ExpectedONRaw", "1"), ("ExpectedONText", "Operated"),
                ("ExpectedOFFRaw", "0"), ("ExpectedOFFText", "Normal")),
            Row(headers,
                ("ProjectId", "CCPP-260728"), ("SchemaVersion", "ARSAS-FAT-IO-1.0"),
                ("TestPointId", "ANALOG-001"), ("TestEnabled", "true"), ("ImportReady", "true"),
                ("BindingStatus", "CID_MODEL_EXACT"), ("SourceSheet", "SoftTags_IEC61850"), ("SourceRow", "262"),
                ("IEDName", "AA1C1F03R4"), ("IPAddress", "192.168.81.70"),
                ("SignalName", "Phase L1-L2 Voltage"), ("DataType", "SAI"), ("FC", "MX"))
        };
        var workbook = BuildWorkbook(headers, rows);

        var result = _importer.Import(workbook, "CCPP-IO-List.xlsx");

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Project.Ieds.Count);
        Assert.Equal(2, result.Project.SignalCount);
        Assert.Equal(2, result.Project.ReadySignalCount);
        Assert.Equal(2, result.ParsedRowCount);
        Assert.Equal(64, result.Project.SourceWorkbookSha256.Length);
        Assert.Contains(result.ParserFindings, finding => finding.Code == "XLSX_NON_SDI_SKIPPED");

        var bcu = Assert.Single(result.Project.Ieds.Where(ied => ied.IedName == "AA1C1F03R4"));
        var signal = Assert.Single(bcu.TestPoints);
        Assert.Equal("CB closed", signal.SignalName);
        Assert.Equal("Active", signal.ExpectedOnText);
        Assert.Equal("InActive", signal.ExpectedOffText);
        Assert.Equal(12, signal.SourceRow);
    }

    [Fact]
    public void MissingRequiredHeader_IsRejectedWithoutGuessing()
    {
        var headers = RequiredHeaders().Where(header => header != "ObjectReference").ToArray();
        var workbook = BuildWorkbook(headers, new[] { Row(headers, ("TestPointId", "TP-001")) });

        var result = _importer.Import(workbook, "broken.xlsx");

        Assert.False(result.IsValid);
        Assert.Contains(result.ParserFindings, finding =>
            finding.Code == "XLSX_HEADER_MISSING" && finding.Message.Contains("ObjectReference"));
    }

    [Fact]
    public void Rev4ScopeMetadata_IsReadByIedIpAndRetainedForReports()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoListExcelImportService.cs"));
        var model = File.ReadAllText(FindRepoFile("Models/IoTesting/IoTestModels.cs"));
        var persistence = File.ReadAllText(FindRepoFile("Services/IoTesting/IoTestProjectPersistenceService.cs"));

        Assert.Contains("IED_Scope", source, StringComparison.Ordinal);
        Assert.Contains("Primary SNTP", source, StringComparison.Ordinal);
        Assert.Contains("Redundant SNTP", source, StringComparison.Ordinal);
        Assert.Contains("ApplyScopeMetadata", source, StringComparison.Ordinal);
        Assert.Contains("PrimarySntpServer", model, StringComparison.Ordinal);
        Assert.Contains("ComtradeApplicability", model, StringComparison.Ordinal);
        Assert.Contains("PrimarySntpServer", persistence, StringComparison.Ordinal);
    }

    private static string[] RequiredHeaders() =>
    [
        "ProjectId", "SchemaVersion", "TestPointId", "TestEnabled", "ImportReady",
        "BindingStatus", "BindingEvidence", "SourceSheet", "SourceRow", "IEDName", "IPAddress",
        "IEDRole", "Location", "VoltageLevel", "Switchgear", "SignalName", "SignalAddress",
        "DataType", "LDInst", "LN", "FC", "DO", "DA", "ObjectReference", "DataSetName",
        "ExpectedONRaw", "ExpectedONText", "ExpectedOFFRaw", "ExpectedOFFText"
    ];

    private static string[] Row(string[] headers, params (string Header, string Value)[] values)
    {
        var lookup = values.ToDictionary(item => item.Header, item => item.Value, StringComparer.OrdinalIgnoreCase);
        return headers.Select(header => lookup.TryGetValue(header, out var value) ? value : string.Empty).ToArray();
    }

    private static byte[] BuildWorkbook(string[] headers, IEnumerable<string[]> rows)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"ARSAS_SIGNAL_IMPORT\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Write(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "</Relationships>");

            var allRows = new List<string[]> { headers };
            allRows.AddRange(rows);
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
            Write(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return memory.ToArray();
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
        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
