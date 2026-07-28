using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Net;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed class IoTestWorkbookImportException : Exception
{
    public IoTestWorkbookImportException(string message, IoTestImportValidationResult? validation = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Validation = validation;
    }

    public IoTestImportValidationResult? Validation { get; }
}

public sealed class IoTestWorkbookImporter
{
    public const string SignalSheetName = "ARSAS_SIGNAL_IMPORT";
    public const string IedSheetName = "IED_LIST";

    private readonly IoTestImportValidator _validator;

    public IoTestWorkbookImporter(IoTestImportValidator? validator = null)
    {
        _validator = validator ?? new IoTestImportValidator();
    }

    public IoTestProject Import(string workbookPath)
    {
        if (string.IsNullOrWhiteSpace(workbookPath))
            throw new ArgumentException("Workbook path is required.", nameof(workbookPath));
        if (!File.Exists(workbookPath))
            throw new FileNotFoundException("The IO List workbook was not found.", workbookPath);
        if (!Path.GetExtension(workbookPath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new IoTestWorkbookImportException("ARSAS IO List Testing currently accepts .xlsx workbooks only.");

        try
        {
            using var document = SpreadsheetDocument.Open(workbookPath, false);
            var workbookPart = document.WorkbookPart
                ?? throw new IoTestWorkbookImportException("The workbook does not contain a valid workbook part.");

            var sharedStrings = ReadSharedStrings(workbookPart);
            var signalRows = ReadSheet(workbookPart, SignalSheetName, sharedStrings, required: true);
            var iedRows = ReadSheet(workbookPart, IedSheetName, sharedStrings, required: false);

            if (signalRows.Count == 0)
                throw new IoTestWorkbookImportException($"Sheet '{SignalSheetName}' contains no signal rows.");

            var iedMetadata = iedRows
                .Where(row => HasValue(row, "IEDName"))
                .GroupBy(row => Value(row, "IEDName"), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var pointRows = signalRows
                .Where(row => HasValue(row, "TestPointId") || HasValue(row, "SignalName") || HasValue(row, "IEDName"))
                .ToList();

            var first = pointRows.First();
            var projectId = Value(first, "ProjectId");
            var schemaVersion = Value(first, "SchemaVersion");
            var points = pointRows.Select(MapPoint).ToList();

            var ieds = points
                .GroupBy(point => point.IedName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    iedMetadata.TryGetValue(group.Key, out var metadata);
                    var firstPointRow = pointRows.First(row => Value(row, "IEDName").Equals(group.Key, StringComparison.OrdinalIgnoreCase));
                    var ip = FirstNonEmpty(
                        metadata == null ? string.Empty : Value(metadata, "IPAddress"),
                        group.Select(point => point.IpAddress).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty);

                    return new IoTestIedPlan
                    {
                        IedName = group.Key,
                        IpAddress = ip,
                        IedRole = FirstNonEmpty(
                            metadata == null ? string.Empty : Value(metadata, "IOListIEDRole", "IEDRole"),
                            Value(firstPointRow, "IEDRole")),
                        Location = FirstNonEmpty(
                            metadata == null ? string.Empty : Value(metadata, "Location"),
                            Value(firstPointRow, "Location")),
                        VoltageLevel = FirstNonEmpty(
                            metadata == null ? string.Empty : Value(metadata, "VoltageLevel"),
                            Value(firstPointRow, "VoltageLevel")),
                        Switchgear = FirstNonEmpty(
                            metadata == null ? string.Empty : Value(metadata, "Switchgear"),
                            Value(firstPointRow, "Switchgear")),
                        TestPoints = group.OrderBy(point => point.SourceRow).ThenBy(point => point.TestPointId, StringComparer.OrdinalIgnoreCase).ToList()
                    };
                })
                .ToList();

            var project = new IoTestProject
            {
                ProjectId = projectId,
                SchemaVersion = schemaVersion,
                ProjectName = Path.GetFileNameWithoutExtension(workbookPath),
                SourceWorkbookName = Path.GetFileName(workbookPath),
                SourceWorkbookSha256 = ComputeSha256(workbookPath),
                ImportedAt = DateTimeOffset.UtcNow,
                Ieds = ieds
            };

            project.InitializeRuntimeNotifications();
            var validation = _validator.Validate(project);
            if (!validation.CanImport)
            {
                var summary = string.Join(Environment.NewLine, validation.Errors.Take(8).Select(issue => $"• {issue.Message}"));
                throw new IoTestWorkbookImportException(
                    $"The workbook cannot be imported because {validation.ErrorCount} validation error(s) were found.{Environment.NewLine}{summary}",
                    validation);
            }

            return project;
        }
        catch (IoTestWorkbookImportException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OpenXmlPackageException)
        {
            throw new IoTestWorkbookImportException("ARSAS could not read the selected IO List workbook.", innerException: ex);
        }
    }

    private static IoTestPointPlan MapPoint(IReadOnlyDictionary<string, string> row)
    {
        var sourceRow = ParseInt(Value(row, "SourceRow"));
        return new IoTestPointPlan
        {
            TestPointId = Value(row, "TestPointId"),
            IedName = Value(row, "IEDName"),
            IpAddress = Value(row, "IPAddress"),
            SignalName = Value(row, "SignalName"),
            SignalAddress = Value(row, "SignalAddress"),
            ObjectReference = Value(row, "ObjectReference"),
            FunctionalConstraint = Value(row, "FunctionalConstraint", "FC"),
            ExpectedOnRaw = ParseInt(Value(row, "ExpectedONRaw"), 1),
            ExpectedOnText = Value(row, "ExpectedONText"),
            ExpectedOffRaw = ParseInt(Value(row, "ExpectedOFFRaw"), 0),
            ExpectedOffText = Value(row, "ExpectedOFFText"),
            DataType = FirstNonEmpty(Value(row, "DataType"), "SDI"),
            DataSetName = Value(row, "DataSetName"),
            LogicalDevice = Value(row, "LDInst", "LD"),
            LogicalNode = Value(row, "LN"),
            DataObject = Value(row, "DO"),
            DataAttribute = Value(row, "DA"),
            SourceSheet = Value(row, "SourceSheet"),
            SourceRow = sourceRow,
            TestEnabled = ParseBoolean(Value(row, "TestEnabled"), true),
            ImportReady = ParseBoolean(Value(row, "ImportReady"), false),
            BindingStatus = Value(row, "BindingStatus"),
            BindingEvidence = Value(row, "BindingEvidence")
        };
    }

    private static List<Dictionary<string, string>> ReadSheet(
        WorkbookPart workbookPart,
        string sheetName,
        IReadOnlyList<string> sharedStrings,
        bool required)
    {
        var sheet = workbookPart.Workbook.Sheets?.Elements<Sheet>()
            .FirstOrDefault(candidate => string.Equals(candidate.Name?.Value, sheetName, StringComparison.OrdinalIgnoreCase));
        if (sheet == null)
        {
            if (required)
                throw new IoTestWorkbookImportException($"Required sheet '{sheetName}' was not found.");
            return new List<Dictionary<string, string>>();
        }

        var worksheetPart = (WorksheetPart?)workbookPart.GetPartById(sheet.Id!);
        var rows = worksheetPart?.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>().ToList()
            ?? new List<Row>();
        if (rows.Count == 0)
            return new List<Dictionary<string, string>>();

        var headerValues = ReadRow(rows[0], sharedStrings);
        var headers = headerValues
            .Select((value, index) => string.IsNullOrWhiteSpace(value) ? $"Column_{index + 1}" : value.Trim())
            .ToList();

        var result = new List<Dictionary<string, string>>();
        foreach (var row in rows.Skip(1))
        {
            var values = ReadRow(row, sharedStrings);
            if (values.All(string.IsNullOrWhiteSpace))
                continue;

            var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++)
                record[headers[i]] = i < values.Count ? values[i].Trim() : string.Empty;
            result.Add(record);
        }

        return result;
    }

    private static List<string> ReadRow(Row row, IReadOnlyList<string> sharedStrings)
    {
        var cells = row.Elements<Cell>().ToList();
        if (cells.Count == 0)
            return new List<string>();

        var maxIndex = cells.Max(cell => ColumnIndex(cell.CellReference?.Value));
        var values = Enumerable.Repeat(string.Empty, maxIndex + 1).ToList();
        foreach (var cell in cells)
        {
            var index = ColumnIndex(cell.CellReference?.Value);
            values[index] = ReadCell(cell, sharedStrings);
        }
        return values;
    }

    private static string ReadCell(Cell cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.Text?.Text ?? cell.InlineString?.InnerText ?? string.Empty;

        var raw = cell.CellValue?.Text ?? cell.InnerText ?? string.Empty;
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex))
            return sharedIndex >= 0 && sharedIndex < sharedStrings.Count ? sharedStrings[sharedIndex] : string.Empty;
        if (cell.DataType?.Value == CellValues.Boolean)
            return raw == "1" ? "TRUE" : "FALSE";
        return raw;
    }

    private static List<string> ReadSharedStrings(WorkbookPart workbookPart)
    {
        return workbookPart.SharedStringTablePart?.SharedStringTable
            .Elements<SharedStringItem>()
            .Select(item => item.InnerText ?? string.Empty)
            .ToList() ?? new List<string>();
    }

    private static int ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
            return 0;

        var index = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
                break;
            index = (index * 26) + (char.ToUpperInvariant(character) - 'A' + 1);
        }
        return Math.Max(0, index - 1);
    }

    private static string Value(IReadOnlyDictionary<string, string> row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }
        return string.Empty;
    }

    private static bool HasValue(IReadOnlyDictionary<string, string> row, string name) => !string.IsNullOrWhiteSpace(Value(row, name));

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool ParseBoolean(string value, bool fallback)
    {
        if (bool.TryParse(value, out var parsed))
            return parsed;
        if (value == "1" || value.Equals("YES", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value == "0" || value.Equals("NO", StringComparison.OrdinalIgnoreCase))
            return false;
        return fallback;
    }

    private static int ParseInt(string value, int fallback = 0) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
