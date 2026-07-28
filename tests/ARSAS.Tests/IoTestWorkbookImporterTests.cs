using ArIED61850Tester.Services.IoTesting;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ARSAS.Tests;

public sealed class IoTestWorkbookImporterTests
{
    [Fact]
    public void Import_ValidWorkbook_GroupsSignalsByIedAndPreservesReviewRows()
    {
        var path = CreateWorkbook(includeSignalSheet: true);
        try
        {
            var project = new IoTestWorkbookImporter().Import(path);

            Assert.Equal("CCPP-260728", project.ProjectId);
            Assert.Equal("ARSAS-FAT-IO-1.0", project.SchemaVersion);
            Assert.Equal(2, project.Ieds.Count);
            Assert.Equal(3, project.SignalCount);
            Assert.Equal(2, project.ReadySignalCount);
            Assert.Equal(64, project.SourceWorkbookSha256.Length);

            var firstIed = Assert.Single(project.Ieds.Where(ied => ied.IedName == "AA1C1F03R4"));
            Assert.Equal("192.168.81.70", firstIed.IpAddress);
            Assert.Equal("BCU - 6MD85", firstIed.IedRole);
            Assert.Equal(2, firstIed.TestPoints.Count);
            Assert.Contains(firstIed.TestPoints, point => point.SignalName == "CB closed" && point.ImportReady);
            Assert.Contains(firstIed.TestPoints, point => point.SignalName == "Unresolved indication" && !point.ImportReady);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Import_MissingSignalSheet_IsRejected()
    {
        var path = CreateWorkbook(includeSignalSheet: false);
        try
        {
            var exception = Assert.Throws<IoTestWorkbookImportException>(() => new IoTestWorkbookImporter().Import(path));
            Assert.Contains(IoTestWorkbookImporter.SignalSheetName, exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Import_DuplicateTestPointId_IsRejectedWithValidationEvidence()
    {
        var path = CreateWorkbook(includeSignalSheet: true, duplicateId: true);
        try
        {
            var exception = Assert.Throws<IoTestWorkbookImportException>(() => new IoTestWorkbookImporter().Import(path));
            Assert.NotNull(exception.Validation);
            Assert.Contains(exception.Validation!.Errors, finding => finding.Code == "TEST_POINT_DUPLICATE");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateWorkbook(bool includeSignalSheet, bool duplicateId = false)
    {
        var path = Path.Combine(Path.GetTempPath(), $"arsas-io-test-{Guid.NewGuid():N}.xlsx");
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        uint sheetId = 1;

        AddSheet(
            workbookPart,
            sheets,
            "IED_LIST",
            sheetId++,
            new[]
            {
                new[] { "ProjectId", "IEDName", "IPAddress", "IOListIEDRole", "Location", "VoltageLevel", "Switchgear" },
                new[] { "CCPP-260728", "AA1C1F03R4", "192.168.81.70", "BCU - 6MD85", "ELECT-CCPP", "66kV", "261-SWG-51001" },
                new[] { "CCPP-260728", "AA1C1F03R3", "192.168.81.69", "IED - 7SX80", "ELECT-CCPP", "66kV", "261-SWG-51001" }
            });

        if (includeSignalSheet)
        {
            var secondId = duplicateId ? "CCPP-AA1C1F03R4-0001" : "CCPP-AA1C1F03R4-0002";
            AddSheet(
                workbookPart,
                sheets,
                IoTestWorkbookImporter.SignalSheetName,
                sheetId,
                new[]
                {
                    new[]
                    {
                        "ProjectId", "SchemaVersion", "TestPointId", "TestEnabled", "ImportReady", "BindingStatus",
                        "BindingEvidence", "SourceSheet", "SourceRow", "IEDName", "IPAddress", "IEDRole", "Location",
                        "VoltageLevel", "Switchgear", "SignalName", "SignalAddress", "DataType", "LDInst", "LN", "FC",
                        "DO", "DA", "ObjectReference", "DataSetName", "ExpectedONRaw", "ExpectedONText", "ExpectedOFFRaw", "ExpectedOFFText"
                    },
                    new[]
                    {
                        "CCPP-260728", "ARSAS-FAT-IO-1.0", "CCPP-AA1C1F03R4-0001", "TRUE", "TRUE", "CID_DATASET_EXACT",
                        "FCDA DataSet Digital", "SoftTags_IEC61850", "13", "AA1C1F03R4", "192.168.81.70", "BCU - 6MD85", "ELECT-CCPP",
                        "66kV", "261-SWG-51001", "CB closed", "CBClsd", "SDI", "ADD", "GGIO6", "ST", "CBClsd", "stVal",
                        "AA1C1F03R4ADD/GGIO6.CBClsd.stVal", "Digital", "1", "Active", "0", "InActive"
                    },
                    new[]
                    {
                        "CCPP-260728", "ARSAS-FAT-IO-1.0", secondId, "FALSE", "FALSE", "REFERENCE_MISSING",
                        "DO source is missing", "SoftTags_IEC61850", "100", "AA1C1F03R4", "192.168.81.70", "BCU - 6MD85", "ELECT-CCPP",
                        "66kV", "261-SWG-51001", "Unresolved indication", "Unknown", "SDI", "", "", "", "", "", "", "", "1", "Active", "0", "InActive"
                    },
                    new[]
                    {
                        "CCPP-260728", "ARSAS-FAT-IO-1.0", "CCPP-AA1C1F03R3-0001", "TRUE", "TRUE", "CID_DATASET_EXACT",
                        "FCDA DataSet Digital", "SoftTags_IEC61850", "239", "AA1C1F03R3", "192.168.81.69", "IED - 7SX80", "ELECT-CCPP",
                        "66kV", "261-SWG-51001", "Protection operated (67)", "Op.general", "SDI", "VI3p1_67DirOC3phB1", "PTRC1", "ST", "Op", "general",
                        "AA1C1F03R3VI3p1_67DirOC3phB1/PTRC1.Op.general", "Digital", "1", "Operated", "0", "Normal"
                    }
                });
        }

        workbookPart.Workbook.Save();
        return path;
    }

    private static void AddSheet(
        WorkbookPart workbookPart,
        Sheets sheets,
        string name,
        uint sheetId,
        IEnumerable<string[]> rows)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);

        uint rowIndex = 1;
        foreach (var values in rows)
        {
            var row = new Row { RowIndex = rowIndex };
            for (var column = 0; column < values.Length; column++)
            {
                row.Append(new Cell
                {
                    CellReference = $"{ColumnName(column)}{rowIndex}",
                    DataType = CellValues.String,
                    CellValue = new CellValue(values[column])
                });
            }
            sheetData.Append(row);
            rowIndex++;
        }

        var relationshipId = workbookPart.GetIdOfPart(worksheetPart);
        sheets.Append(new Sheet { Id = relationshipId, SheetId = sheetId, Name = name });
    }

    private static string ColumnName(int index)
    {
        var value = index + 1;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }
}
