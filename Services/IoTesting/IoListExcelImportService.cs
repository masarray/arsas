using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record IoListExcelImportResult(
    IoTestProject Project,
    IoTestImportValidationResult Validation,
    IReadOnlyList<IoTestImportFinding> ParserFindings,
    int ParsedRowCount)
{
    public IReadOnlyList<IoTestImportFinding> AllFindings => ParserFindings.Concat(Validation.Findings).ToList();
    public bool IsValid => ParserFindings.All(finding => finding.Severity != IoTestImportFindingSeverity.Error) && Validation.IsValid;
}

public sealed class IoListExcelImportService
{
    private const string SignalSheetName = "ARSAS_SIGNAL_IMPORT";
    private const int MaxWorkbookBytes = 50 * 1024 * 1024;
    private const int MaxSignalRows = 20_000;
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly string[] RequiredHeaders =
    {
        "ProjectId", "SchemaVersion", "TestPointId", "TestEnabled", "ImportReady",
        "BindingStatus", "SourceSheet", "SourceRow", "IEDName", "IPAddress",
        "SignalName", "DataType", "FC", "ObjectReference", "ExpectedONRaw",
        "ExpectedONText", "ExpectedOFFRaw", "ExpectedOFFText"
    };

    private readonly IoTestImportValidator _validator;

    public IoListExcelImportService(IoTestImportValidator? validator = null)
    {
        _validator = validator ?? new IoTestImportValidator();
    }

    public Task<IoListExcelImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("An Excel file path is required.", nameof(filePath));

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(filePath);
            if (!info.Exists)
                throw new FileNotFoundException("The IO List workbook was not found.", filePath);
            if (info.Length > MaxWorkbookBytes)
                throw new InvalidDataException($"The IO List workbook exceeds the {MaxWorkbookBytes / 1024 / 1024} MB safety limit.");

            var bytes = File.ReadAllBytes(filePath);
            cancellationToken.ThrowIfCancellationRequested();
            return Import(bytes, Path.GetFileName(filePath), cancellationToken);
        }, cancellationToken);
    }

    public IoListExcelImportResult Import(byte[] workbookBytes, string sourceFileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbookBytes);
        if (workbookBytes.Length == 0)
            throw new InvalidDataException("The IO List workbook is empty.");
        if (workbookBytes.Length > MaxWorkbookBytes)
            throw new InvalidDataException($"The IO List workbook exceeds the {MaxWorkbookBytes / 1024 / 1024} MB safety limit.");

        var parserFindings = new List<IoTestImportFinding>();
        using var memory = new MemoryStream(workbookBytes, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
        var rows = ReadSheetRows(archive, SignalSheetName, cancellationToken);
        if (rows.Count == 0)
            throw new InvalidDataException($"Sheet '{SignalSheetName}' contains no rows.");

        var headerRow = rows[0];
        var headers = headerRow.Cells
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(item => item.Key, item => item.Value.Trim(), EqualityComparer<int>.Default);
        var duplicateHeaders = headers.Values
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        foreach (var duplicate in duplicateHeaders)
        {
            parserFindings.Add(new IoTestImportFinding(
                IoTestImportFindingSeverity.Error,
                "XLSX_HEADER_DUPLICATE",
                $"Column header '{duplicate}' occurs more than once in sheet '{SignalSheetName}'.",
                SourceSheet: SignalSheetName,
                SourceRow: headerRow.RowNumber));
        }
        var headerLookup = headers
            .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.OrdinalIgnoreCase);

        foreach (var required in RequiredHeaders)
        {
            if (!headerLookup.ContainsKey(required))
            {
                parserFindings.Add(new IoTestImportFinding(
                    IoTestImportFindingSeverity.Error,
                    "XLSX_HEADER_MISSING",
                    $"Required column '{required}' is missing from sheet '{SignalSheetName}'.",
                    SourceSheet: SignalSheetName,
                    SourceRow: headerRow.RowNumber));
            }
        }

        if (parserFindings.Any(finding => finding.Severity == IoTestImportFindingSeverity.Error))
            return BuildEmptyResult(sourceFileName, workbookBytes, parserFindings);

        var pointRows = new List<ImportedPointRow>();
        foreach (var row in rows.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pointRows.Count >= MaxSignalRows)
                throw new InvalidDataException($"The IO List exceeds the {MaxSignalRows:N0} signal-row safety limit.");

            var values = BuildRowValues(row, headerLookup);
            var testPointId = Get(values, "TestPointId");
            if (string.IsNullOrWhiteSpace(testPointId))
                continue;

            var dataType = Get(values, "DataType");
            if (!string.Equals(dataType, "SDI", StringComparison.OrdinalIgnoreCase))
            {
                parserFindings.Add(new IoTestImportFinding(
                    IoTestImportFindingSeverity.Warning,
                    "XLSX_NON_SDI_SKIPPED",
                    $"Test point '{testPointId}' uses DataType '{dataType}' and was skipped by the SDI-only first release.",
                    testPointId,
                    SignalSheetName,
                    row.RowNumber));
                continue;
            }

            pointRows.Add(new ImportedPointRow(row.RowNumber, values));
        }

        if (pointRows.Count == 0)
        {
            parserFindings.Add(new IoTestImportFinding(
                IoTestImportFindingSeverity.Error,
                "XLSX_SIGNAL_ROWS_EMPTY",
                "No SDI signal rows with TestPointId were found.",
                SourceSheet: SignalSheetName));
            return BuildEmptyResult(sourceFileName, workbookBytes, parserFindings);
        }

        var projectIds = DistinctNonEmpty(pointRows, "ProjectId");
        var schemas = DistinctNonEmpty(pointRows, "SchemaVersion");
        if (projectIds.Count != 1)
            parserFindings.Add(Error("XLSX_PROJECT_ID_INCONSISTENT", "The workbook must contain exactly one non-empty ProjectId."));
        if (schemas.Count != 1)
            parserFindings.Add(Error("XLSX_SCHEMA_INCONSISTENT", "The workbook must contain exactly one non-empty SchemaVersion."));

        var iedPlans = pointRows
            .GroupBy(row => new IedKey(Get(row.Values, "IEDName"), Get(row.Values, "IPAddress")), IedKeyComparer.Instance)
            .OrderBy(group => group.Key.IedName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key.IpAddress, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildIedPlan(group.Key, group, parserFindings))
            .ToList();

        var project = new IoTestProject
        {
            ProjectId = projectIds.Count == 1 ? projectIds[0] : string.Empty,
            SchemaVersion = schemas.Count == 1 ? schemas[0] : string.Empty,
            ProjectName = Path.GetFileNameWithoutExtension(sourceFileName),
            SourceWorkbookName = sourceFileName,
            SourceWorkbookSha256 = Convert.ToHexString(SHA256.HashData(workbookBytes)).ToLowerInvariant(),
            ImportedAt = DateTimeOffset.UtcNow,
            Ieds = iedPlans
        };
        project.InitializeRuntimeNotifications();

        return new IoListExcelImportResult(
            project,
            _validator.Validate(project),
            parserFindings,
            pointRows.Count);
    }

    private static IoListExcelImportResult BuildEmptyResult(
        string sourceFileName,
        byte[] workbookBytes,
        IReadOnlyList<IoTestImportFinding> parserFindings)
    {
        var project = new IoTestProject
        {
            ProjectId = string.Empty,
            SchemaVersion = string.Empty,
            ProjectName = Path.GetFileNameWithoutExtension(sourceFileName),
            SourceWorkbookName = sourceFileName,
            SourceWorkbookSha256 = Convert.ToHexString(SHA256.HashData(workbookBytes)).ToLowerInvariant()
        };
        var validation = new IoTestImportValidator().Validate(project);
        return new IoListExcelImportResult(project, validation, parserFindings, 0);
    }

    private static IoTestIedPlan BuildIedPlan(
        IedKey key,
        IEnumerable<ImportedPointRow> sourceRows,
        ICollection<IoTestImportFinding> findings)
    {
        var rows = sourceRows.ToList();
        var points = rows.Select(row => BuildPoint(key, row, findings)).ToList();
        return new IoTestIedPlan
        {
            IedName = key.IedName,
            IpAddress = key.IpAddress,
            IedRole = FirstNonEmpty(rows, "IEDRole"),
            Location = JoinDistinct(rows, "Location"),
            VoltageLevel = JoinDistinct(rows, "VoltageLevel"),
            Switchgear = JoinDistinct(rows, "Switchgear"),
            TestPoints = points
        };
    }

    private static IoTestPointPlan BuildPoint(
        IedKey key,
        ImportedPointRow row,
        ICollection<IoTestImportFinding> findings)
    {
        var values = row.Values;
        var sourceRow = ParseInt(Get(values, "SourceRow"), row.RowNumber);
        var onRaw = ParseInt(Get(values, "ExpectedONRaw"), 1);
        var offRaw = ParseInt(Get(values, "ExpectedOFFRaw"), 0);
        if (onRaw == offRaw)
        {
            findings.Add(new IoTestImportFinding(
                IoTestImportFindingSeverity.Error,
                "XLSX_EXPECTED_STATE_INVALID",
                $"Expected ON and OFF raw values are identical for '{Get(values, "TestPointId")}'.",
                Get(values, "TestPointId"),
                Get(values, "SourceSheet"),
                sourceRow));
        }

        return new IoTestPointPlan
        {
            TestPointId = Get(values, "TestPointId"),
            IedName = key.IedName,
            IpAddress = key.IpAddress,
            SignalName = Get(values, "SignalName"),
            SignalAddress = Get(values, "SignalAddress"),
            DataType = Get(values, "DataType"),
            ObjectReference = Get(values, "ObjectReference"),
            FunctionalConstraint = Get(values, "FC"),
            LogicalDevice = Get(values, "LDInst"),
            LogicalNode = Get(values, "LN"),
            DataObject = Get(values, "DO"),
            DataAttribute = Get(values, "DA"),
            DataSetName = Get(values, "DataSetName"),
            ExpectedOnRaw = onRaw,
            ExpectedOnText = Get(values, "ExpectedONText"),
            ExpectedOffRaw = offRaw,
            ExpectedOffText = Get(values, "ExpectedOFFText"),
            SourceSheet = Get(values, "SourceSheet"),
            SourceRow = sourceRow,
            TestEnabled = ParseBool(Get(values, "TestEnabled"), defaultValue: true),
            ImportReady = ParseBool(Get(values, "ImportReady"), defaultValue: false),
            BindingStatus = Get(values, "BindingStatus"),
            BindingEvidence = Get(values, "BindingEvidence")
        };
    }

    private static Dictionary<string, string> BuildRowValues(
        WorksheetRow row,
        IReadOnlyDictionary<string, int> headerLookup)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headerLookup)
            values[header.Key] = row.Cells.TryGetValue(header.Value, out var value) ? value.Trim() : string.Empty;
        return values;
    }

    private static List<string> DistinctNonEmpty(IEnumerable<ImportedPointRow> rows, string key)
        => rows.Select(row => Get(row.Values, key))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string FirstNonEmpty(IEnumerable<ImportedPointRow> rows, string key)
        => rows.Select(row => Get(row.Values, key)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string JoinDistinct(IEnumerable<ImportedPointRow> rows, string key)
        => string.Join(", ", rows.Select(row => Get(row.Values, key))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string Get(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

    private static bool ParseBool(string value, bool defaultValue)
    {
        if (bool.TryParse(value, out var parsed))
            return parsed;
        if (value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("x", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value == "0" || value.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;
        return defaultValue;
    }

    private static int ParseInt(string value, int defaultValue)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : defaultValue;

    private static IoTestImportFinding Error(string code, string message)
        => new(IoTestImportFindingSeverity.Error, code, message, SourceSheet: SignalSheetName);

    private static IReadOnlyList<WorksheetRow> ReadSheetRows(
        ZipArchive archive,
        string sheetName,
        CancellationToken cancellationToken)
    {
        var workbook = LoadXml(archive, "xl/workbook.xml");
        var relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var sheet = workbook.Root?
            .Element(SpreadsheetNs + "sheets")?
            .Elements(SpreadsheetNs + "sheet")
            .FirstOrDefault(item => string.Equals((string?)item.Attribute("name"), sheetName, StringComparison.OrdinalIgnoreCase));
        if (sheet == null)
            throw new InvalidDataException($"Required sheet '{sheetName}' was not found.");

        var relationshipId = (string?)sheet.Attribute(RelationshipsNs + "id");
        if (string.IsNullOrWhiteSpace(relationshipId))
            throw new InvalidDataException($"Sheet '{sheetName}' has no workbook relationship.");

        var relationship = relationships.Root?
            .Elements(PackageRelationshipsNs + "Relationship")
            .FirstOrDefault(item => string.Equals((string?)item.Attribute("Id"), relationshipId, StringComparison.Ordinal));
        var target = (string?)relationship?.Attribute("Target");
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidDataException($"Sheet '{sheetName}' target could not be resolved.");

        var sheetPath = NormalizeWorkbookTarget(target);
        var sharedStrings = ReadSharedStrings(archive);
        var sheetDocument = LoadXml(archive, sheetPath);
        var rows = new List<WorksheetRow>();
        foreach (var row in sheetDocument.Descendants(SpreadsheetNs + "row"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowNumber = (int?)row.Attribute("r") ?? rows.Count + 1;
            var cells = new Dictionary<int, string>();
            foreach (var cell in row.Elements(SpreadsheetNs + "c"))
            {
                var reference = (string?)cell.Attribute("r");
                var columnIndex = ColumnIndex(reference);
                if (columnIndex < 0)
                    continue;
                cells[columnIndex] = ReadCellValue(cell, sharedStrings);
            }
            if (cells.Count > 0)
                rows.Add(new WorksheetRow(rowNumber, cells));
        }
        return rows;
    }

    private static XDocument LoadXml(ZipArchive archive, string entryPath)
    {
        var entry = archive.GetEntry(entryPath) ?? throw new InvalidDataException($"XLSX entry '{entryPath}' is missing.");
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.None);
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null)
            return Array.Empty<string>();
        using var stream = entry.Open();
        var document = XDocument.Load(stream, LoadOptions.None);
        return document.Descendants(SpreadsheetNs + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNs + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = ((string?)cell.Attribute("t") ?? string.Empty).Trim();
        if (type == "inlineStr")
            return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(text => text.Value));

        var raw = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < sharedStrings.Count)
            return sharedStrings[index];
        if (type == "b")
            return raw == "1" ? "true" : "false";
        return raw;
    }

    private static int ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
            return -1;
        var index = 0;
        var found = false;
        foreach (var ch in cellReference)
        {
            if (!char.IsLetter(ch))
                break;
            found = true;
            index = checked(index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1));
        }
        return found ? index - 1 : -1;
    }

    private static string NormalizeWorkbookTarget(string target)
    {
        var normalized = target.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
            return normalized;
        while (normalized.StartsWith("../", StringComparison.Ordinal))
            normalized = normalized[3..];
        return "xl/" + normalized.TrimStart('/');
    }

    private sealed record WorksheetRow(int RowNumber, IReadOnlyDictionary<int, string> Cells);
    private sealed record ImportedPointRow(int RowNumber, IReadOnlyDictionary<string, string> Values);
    private sealed record IedKey(string IedName, string IpAddress);

    private sealed class IedKeyComparer : IEqualityComparer<IedKey>
    {
        public static IedKeyComparer Instance { get; } = new();
        public bool Equals(IedKey? x, IedKey? y)
            => x != null && y != null &&
               string.Equals(x.IedName, y.IedName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(x.IpAddress, y.IpAddress, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(IedKey obj)
            => HashCode.Combine(obj.IedName.ToUpperInvariant(), obj.IpAddress.ToUpperInvariant());
    }
}
