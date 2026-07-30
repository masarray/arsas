using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
    public bool IsValid => ParserFindings.All(x => x.Severity != IoTestImportFindingSeverity.Error) && Validation.IsValid;
}

public sealed class IoListExcelImportService
{
    private const string LegacySheet = "ARSAS_SIGNAL_IMPORT";
    private const string Rev3Sheet = "FAT_Points_Import";
    private const string DocumentSheet = "Document_Control";
    private const int MaxWorkbookBytes = 50 * 1024 * 1024;
    private const int MaxRows = 20_000;

    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly string[] LegacyRequired =
    [
        "ProjectId", "SchemaVersion", "TestPointId", "TestEnabled", "ImportReady",
        "BindingStatus", "SourceSheet", "SourceRow", "IEDName", "IPAddress",
        "SignalName", "DataType", "FC", "ObjectReference", "ExpectedONRaw",
        "ExpectedONText", "ExpectedOFFRaw", "ExpectedOFFText"
    ];

    private static readonly string[] Rev3Required =
    [
        "Point ID", "Include in FAT", "Source Sheet", "Source Row",
        "IED Identifier / Technical Key", "IP Address", "Signal Description",
        "Data Type", "FC", "IEC 61850 Reference (Source)",
        "State 0/01 Text (Source)", "State 1/10 Text (Source)", "Review Status"
    ];

    private readonly IoTestImportValidator _validator;

    public IoListExcelImportService(IoTestImportValidator? validator = null)
        => _validator = validator ?? new IoTestImportValidator();

    public Task<IoListExcelImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(filePath);
            if (!info.Exists) throw new FileNotFoundException("The IO List workbook was not found.", filePath);
            if (info.Length > MaxWorkbookBytes) throw new InvalidDataException("The IO List workbook exceeds the 50 MB safety limit.");
            return Import(File.ReadAllBytes(filePath), Path.GetFileName(filePath), cancellationToken);
        }, cancellationToken);
    }

    public IoListExcelImportResult Import(byte[] workbookBytes, string sourceFileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbookBytes);
        if (workbookBytes.Length == 0) throw new InvalidDataException("The IO List workbook is empty.");
        if (workbookBytes.Length > MaxWorkbookBytes) throw new InvalidDataException("The IO List workbook exceeds the 50 MB safety limit.");

        using var memory = new MemoryStream(workbookBytes, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
        var findings = new List<IoTestImportFinding>();
        var rev3 = HasSheet(archive, Rev3Sheet);
        var sheetName = rev3 ? Rev3Sheet : HasSheet(archive, LegacySheet)
            ? LegacySheet
            : throw new InvalidDataException($"Expected sheet '{Rev3Sheet}' or '{LegacySheet}'.");
        var rows = ReadRows(archive, sheetName, true, cancellationToken);
        if (rows.Count == 0) throw new InvalidDataException($"Sheet '{sheetName}' contains no rows.");

        var headers = Headers(rows[0], sheetName, findings);
        foreach (var required in rev3 ? Rev3Required : LegacyRequired)
            if (!headers.ContainsKey(required))
                findings.Add(Finding(IoTestImportFindingSeverity.Error, "XLSX_HEADER_MISSING",
                    $"Required column '{required}' is missing from sheet '{sheetName}'.", sheet: sheetName, row: rows[0].Number));
        if (findings.Any(x => x.Severity == IoTestImportFindingSeverity.Error))
            return Empty(sourceFileName, workbookBytes, findings);

        var control = rev3 ? ReadDocumentControl(archive, sourceFileName, cancellationToken) : new IoFatDocumentControl();
        var projectId = rev3
            ? First(control.CompanyProjectDocumentNumber, control.PurchaserDocumentNumber, Path.GetFileNameWithoutExtension(sourceFileName))
            : string.Empty;
        var projectName = rev3
            ? First(control.ClientProject, control.PurchaseOrderTitle, Path.GetFileNameWithoutExtension(sourceFileName))
            : Path.GetFileNameWithoutExtension(sourceFileName);

        var imported = new List<ImportedRow>();
        var nonSdi = 0;
        var blocked = 0;
        var excluded = 0;
        var endpoint = 0;
        foreach (var row in rows.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (imported.Count >= MaxRows) throw new InvalidDataException($"The IO List exceeds the {MaxRows:N0} row safety limit.");
            var source = Values(row, headers);
            var values = rev3 ? MapRev3(source, projectId) : source;
            if (string.IsNullOrWhiteSpace(Get(values, "TestPointId"))) continue;

            if (rev3)
            {
                if (Get(source, "Include in FAT").Equals("NO", StringComparison.OrdinalIgnoreCase)) { excluded++; continue; }
                if (Get(source, "Review Status").StartsWith("BLOCKED", StringComparison.OrdinalIgnoreCase)) { blocked++; continue; }
                if (string.IsNullOrWhiteSpace(Get(values, "IEDName")) || !IPAddress.TryParse(Get(values, "IPAddress"), out _))
                { endpoint++; continue; }
            }

            if (!Get(values, "DataType").Equals("SDI", StringComparison.OrdinalIgnoreCase)) { nonSdi++; continue; }
            imported.Add(new ImportedRow(row.Number, values));
        }

        AddSkip(findings, sheetName, "XLSX_NON_SDI_SKIPPED", nonSdi, "non-SDI points were skipped because automatic transition testing is SDI-only");
        AddSkip(findings, sheetName, "XLSX_BLOCKED_ROWS_SKIPPED", blocked, "blocked points were skipped because their IEC 61850 identity is incomplete");
        AddSkip(findings, sheetName, "XLSX_EXCLUDED_ROWS_SKIPPED", excluded, "points marked Include in FAT = NO were skipped");
        AddSkip(findings, sheetName, "XLSX_ENDPOINT_ROWS_SKIPPED", endpoint, "points with an invalid IED endpoint were skipped instead of guessed");
        if (rev3 && control.IssueStatus.Contains("REVIEW", StringComparison.OrdinalIgnoreCase))
            findings.Add(Finding(IoTestImportFindingSeverity.Warning, "DOCUMENT_CONTROL_REVIEW_REQUIRED",
                "Document control is marked REVIEW REQUIRED. Confirm project and supplier identity before final customer issue.", sheet: DocumentSheet));

        if (imported.Count == 0)
        {
            findings.Add(Finding(IoTestImportFindingSeverity.Error, "XLSX_SIGNAL_ROWS_EMPTY",
                "No importable SDI signal rows with a valid identity were found.", sheet: sheetName));
            return Empty(sourceFileName, workbookBytes, findings);
        }

        var projectIds = Distinct(imported, "ProjectId");
        var schemas = Distinct(imported, "SchemaVersion");
        if (projectIds.Count != 1) findings.Add(Finding(IoTestImportFindingSeverity.Error, "XLSX_PROJECT_ID_INCONSISTENT", "The workbook must contain exactly one ProjectId.", sheet: sheetName));
        if (schemas.Count != 1) findings.Add(Finding(IoTestImportFindingSeverity.Error, "XLSX_SCHEMA_INCONSISTENT", "The workbook must contain exactly one SchemaVersion.", sheet: sheetName));

        var ieds = imported
            .GroupBy(x => new IedKey(Get(x.Values, "IEDName"), Get(x.Values, "IPAddress")), IedKeyComparer.Instance)
            .OrderBy(x => x.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => BuildIed(x.Key, x, findings))
            .ToList();
        var project = new IoTestProject
        {
            ProjectId = projectIds.Count == 1 ? projectIds[0] : string.Empty,
            SchemaVersion = schemas.Count == 1 ? schemas[0] : string.Empty,
            ProjectName = projectName,
            SourceWorkbookName = sourceFileName,
            SourceWorkbookSha256 = Convert.ToHexString(SHA256.HashData(workbookBytes)).ToLowerInvariant(),
            ImportedAt = DateTimeOffset.UtcNow,
            DocumentControl = control,
            Ieds = ieds
        };
        project.InitializeRuntimeNotifications();
        return new IoListExcelImportResult(project, _validator.Validate(project), findings, imported.Count);
    }

    private static Dictionary<string, string> MapRev3(IReadOnlyDictionary<string, string> source, string projectId)
    {
        var ready = Get(source, "Include in FAT").Equals("YES", StringComparison.OrdinalIgnoreCase) &&
                    Get(source, "Review Status").Equals("READY", StringComparison.OrdinalIgnoreCase);
        var eventRef = First(Get(source, "Event Log Search Reference"), Get(source, "IEC 61850 Reference (Source)"));
        var displayRef = Get(source, "Report Display Reference");
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["ProjectId"] = projectId,
            ["SchemaVersion"] = IoTestImportValidator.SupportedSchemaVersion,
            ["TestPointId"] = Get(source, "Point ID"),
            ["TestEnabled"] = ready.ToString(CultureInfo.InvariantCulture),
            ["ImportReady"] = ready.ToString(CultureInfo.InvariantCulture),
            ["BindingStatus"] = Get(source, "Mapping Quality"),
            ["BindingEvidence"] = Get(source, "Review Reason"),
            ["SourceSheet"] = Get(source, "Source Sheet"),
            ["SourceRow"] = Get(source, "Source Row"),
            ["IEDName"] = Get(source, "IED Identifier / Technical Key"),
            ["IPAddress"] = Get(source, "IP Address"),
            ["IEDRole"] = Get(source, "IED Type"),
            ["Location"] = Get(source, "Location"),
            ["VoltageLevel"] = Get(source, "Voltage Level"),
            ["Switchgear"] = Get(source, "Switchgear"),
            ["SignalName"] = Get(source, "Signal Description"),
            ["SignalAddress"] = Get(source, "Signal Alias"),
            ["DataType"] = Get(source, "Data Type"),
            ["LDInst"] = Get(source, "LD"),
            ["LN"] = Get(source, "LN"),
            ["FC"] = Get(source, "FC"),
            ["DO"] = string.Empty,
            ["DA"] = Get(source, "Data Attribute"),
            ["CDC"] = Get(source, "CDC"),
            ["ObjectReference"] = BindingReference(displayRef, Get(source, "LD"), eventRef, Get(source, "Data Attribute"), Get(source, "FC")),
            ["SourceIecReference"] = Get(source, "IEC 61850 Reference (Source)"),
            ["ReportDisplayReference"] = displayRef,
            ["EventLogSearchReference"] = eventRef,
            ["DataSetName"] = Get(source, "Dataset"),
            ["ExpectedONRaw"] = "1",
            ["ExpectedONText"] = Get(source, "State 1/10 Text (Source)"),
            ["ExpectedOFFRaw"] = "0",
            ["ExpectedOFFText"] = Get(source, "State 0/01 Text (Source)"),
            ["EvidenceExpected"] = Get(source, "Evidence Expected"),
            ["MappingQuality"] = Get(source, "Mapping Quality"),
            ["ReviewStatus"] = Get(source, "Review Status"),
            ["ReviewReason"] = Get(source, "Review Reason"),
            ["EventLogMatch"] = Get(source, "Event Log Match"),
            ["EvidenceReference"] = Get(source, "Evidence Ref / Screenshot"),
            ["ReviewerComment"] = Get(source, "Reviewer Comment")
        };
    }

    private static string BindingReference(string display, string ld, string eventRef, string da, string fc)
    {
        if (!string.IsNullOrWhiteSpace(display))
        {
            var value = display.Trim();
            var suffix = $" [{fc.Trim()}]";
            return !string.IsNullOrWhiteSpace(fc) && value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? value[..^suffix.Length].TrimEnd()
                : value;
        }
        var value2 = eventRef.Trim();
        if (!string.IsNullOrWhiteSpace(da) && !value2.EndsWith("." + da.Trim(), StringComparison.OrdinalIgnoreCase)) value2 += "." + da.Trim();
        if (!string.IsNullOrWhiteSpace(ld) && !value2.StartsWith(ld.Trim() + "/", StringComparison.OrdinalIgnoreCase)) value2 = ld.Trim() + "/" + value2.TrimStart('/');
        return value2;
    }

    private static IoFatDocumentControl ReadDocumentControl(ZipArchive archive, string workbookName, CancellationToken token)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in ReadRows(archive, DocumentSheet, false, token).Skip(1))
        {
            var key = row.Cells.TryGetValue(0, out var k) ? k.Trim() : string.Empty;
            var value = row.Cells.TryGetValue(1, out var v) ? v.Trim() : string.Empty;
            if (!string.IsNullOrWhiteSpace(key) && !values.ContainsKey(key)) values[key] = value;
        }
        var source = First(Get(values, "Source file name"), workbookName);
        return new IoFatDocumentControl
        {
            ClientProject = Get(values, "Project shown on front sheet"),
            SupplierName = Get(values, "Supplier shown on front sheet"),
            PurchaseOrderTitle = Get(values, "Purchase Order Title"),
            PurchaserDocumentNumber = Get(values, "Purchaser Document No."),
            CompanyProjectDocumentNumber = Get(values, "Company Project Document No."),
            DocumentTitle = Get(values, "Document title"),
            Revision = First(Get(values, "Document Revision"), ExtractRevision(source), Get(values, "Import revision")),
            IssueStatus = Get(values, "Overall document-control status"),
            SourceDocumentName = source
        };
    }

    private static string ExtractRevision(string value)
    {
        var match = Regex.Match(value ?? string.Empty, @"_(?<revision>\d{2})_", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["revision"].Value : string.Empty;
    }

    private static IoTestIedPlan BuildIed(IedKey key, IEnumerable<ImportedRow> rows, ICollection<IoTestImportFinding> findings)
    {
        var list = rows.ToList();
        return new IoTestIedPlan
        {
            IedName = key.Name,
            IpAddress = key.Ip,
            IedRole = FirstValue(list, "IEDRole"),
            Location = Join(list, "Location"),
            VoltageLevel = Join(list, "VoltageLevel"),
            Switchgear = Join(list, "Switchgear"),
            TestPoints = list.Select(x => BuildPoint(key, x, findings)).ToList()
        };
    }

    private static IoTestPointPlan BuildPoint(IedKey key, ImportedRow row, ICollection<IoTestImportFinding> findings)
    {
        var v = row.Values;
        var sourceRow = Int(Get(v, "SourceRow"), row.Number);
        var on = Int(Get(v, "ExpectedONRaw"), 1);
        var off = Int(Get(v, "ExpectedOFFRaw"), 0);
        if (on == off) findings.Add(Finding(IoTestImportFindingSeverity.Error, "XLSX_EXPECTED_STATE_INVALID",
            $"Expected ON and OFF values are identical for '{Get(v, "TestPointId")}'.", Get(v, "TestPointId"), Get(v, "SourceSheet"), sourceRow));
        return new IoTestPointPlan
        {
            TestPointId = Get(v, "TestPointId"), IedName = key.Name, IpAddress = key.Ip,
            SignalName = Get(v, "SignalName"), SignalAddress = Get(v, "SignalAddress"), DataType = Get(v, "DataType"),
            ObjectReference = Get(v, "ObjectReference"), FunctionalConstraint = Get(v, "FC"),
            LogicalDevice = Get(v, "LDInst"), LogicalNode = Get(v, "LN"), DataObject = Get(v, "DO"), DataAttribute = Get(v, "DA"),
            Cdc = Get(v, "CDC"), SourceIecReference = Get(v, "SourceIecReference"), ReportDisplayReference = Get(v, "ReportDisplayReference"),
            EventLogSearchReference = Get(v, "EventLogSearchReference"), EvidenceExpected = Get(v, "EvidenceExpected"),
            MappingQuality = Get(v, "MappingQuality"), ReviewStatus = Get(v, "ReviewStatus"), ReviewReason = Get(v, "ReviewReason"),
            EventLogMatch = Get(v, "EventLogMatch"), EvidenceReference = Get(v, "EvidenceReference"), ReviewerComment = Get(v, "ReviewerComment"),
            DataSetName = Get(v, "DataSetName"), ExpectedOnRaw = on, ExpectedOnText = Get(v, "ExpectedONText"),
            ExpectedOffRaw = off, ExpectedOffText = Get(v, "ExpectedOFFText"), SourceSheet = Get(v, "SourceSheet"), SourceRow = sourceRow,
            TestEnabled = Bool(Get(v, "TestEnabled"), true), ImportReady = Bool(Get(v, "ImportReady"), false),
            BindingStatus = Get(v, "BindingStatus"), BindingEvidence = Get(v, "BindingEvidence")
        };
    }

    private static IoListExcelImportResult Empty(string file, byte[] bytes, IReadOnlyList<IoTestImportFinding> findings)
    {
        var project = new IoTestProject
        {
            ProjectId = string.Empty, SchemaVersion = string.Empty, ProjectName = Path.GetFileNameWithoutExtension(file),
            SourceWorkbookName = file, SourceWorkbookSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        };
        return new IoListExcelImportResult(project, new IoTestImportValidator().Validate(project), findings, 0);
    }

    private static Dictionary<string, int> Headers(Row row, string sheet, ICollection<IoTestImportFinding> findings)
    {
        var pairs = row.Cells.Where(x => !string.IsNullOrWhiteSpace(x.Value)).ToList();
        foreach (var duplicate in pairs.GroupBy(x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            findings.Add(Finding(IoTestImportFindingSeverity.Error, "XLSX_HEADER_DUPLICATE", $"Column header '{duplicate.Key}' occurs more than once.", sheet: sheet, row: row.Number));
        return pairs.GroupBy(x => x.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Key, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> Values(Row row, IReadOnlyDictionary<string, int> headers)
        => headers.ToDictionary(x => x.Key, x => row.Cells.TryGetValue(x.Value, out var v) ? v.Trim() : string.Empty, StringComparer.OrdinalIgnoreCase);

    private static List<Row> ReadRows(ZipArchive archive, string sheet, bool required, CancellationToken token)
    {
        if (!TrySheetPath(archive, sheet, out var path))
        {
            if (required) throw new InvalidDataException($"Required sheet '{sheet}' was not found.");
            return [];
        }
        var strings = SharedStrings(archive);
        var result = new List<Row>();
        foreach (var row in Xml(archive, path).Descendants(S + "row"))
        {
            token.ThrowIfCancellationRequested();
            var cells = new Dictionary<int, string>();
            foreach (var cell in row.Elements(S + "c"))
            {
                var column = Column((string?)cell.Attribute("r"));
                if (column >= 0) cells[column] = CellValue(cell, strings);
            }
            if (cells.Count > 0) result.Add(new Row((int?)row.Attribute("r") ?? result.Count + 1, cells));
        }
        return result;
    }

    private static bool HasSheet(ZipArchive archive, string sheet) => TrySheetPath(archive, sheet, out _);

    private static bool TrySheetPath(ZipArchive archive, string sheetName, out string path)
    {
        var workbook = Xml(archive, "xl/workbook.xml");
        var relationships = Xml(archive, "xl/_rels/workbook.xml.rels");
        var sheet = workbook.Root?.Element(S + "sheets")?.Elements(S + "sheet")
            .FirstOrDefault(x => string.Equals((string?)x.Attribute("name"), sheetName, StringComparison.OrdinalIgnoreCase));
        if (sheet == null) { path = string.Empty; return false; }
        var id = (string?)sheet.Attribute(R + "id");
        var target = (string?)relationships.Root?.Elements(P + "Relationship")
            .FirstOrDefault(x => string.Equals((string?)x.Attribute("Id"), id, StringComparison.Ordinal))?.Attribute("Target");
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidDataException($"Sheet '{sheetName}' target could not be resolved.");
        path = NormalizeTarget(target);
        return true;
    }

    private static XDocument Xml(ZipArchive archive, string path)
    {
        using var stream = (archive.GetEntry(path) ?? throw new InvalidDataException($"XLSX entry '{path}' is missing.")).Open();
        return XDocument.Load(stream, LoadOptions.None);
    }

    private static IReadOnlyList<string> SharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return [];
        using var stream = entry.Open();
        return XDocument.Load(stream).Descendants(S + "si").Select(x => string.Concat(x.Descendants(S + "t").Select(t => t.Value))).ToList();
    }

    private static string CellValue(XElement cell, IReadOnlyList<string> strings)
    {
        var type = ((string?)cell.Attribute("t") ?? string.Empty).Trim();
        if (type == "inlineStr") return string.Concat(cell.Descendants(S + "t").Select(x => x.Value));
        var raw = cell.Element(S + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(raw, out var index) && index >= 0 && index < strings.Count) return strings[index];
        return type == "b" ? raw == "1" ? "true" : "false" : raw;
    }

    private static int Column(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return -1;
        var value = 0;
        foreach (var ch in reference.TakeWhile(char.IsLetter)) value = checked(value * 26 + char.ToUpperInvariant(ch) - 'A' + 1);
        return value - 1;
    }

    private static string NormalizeTarget(string target)
    {
        var value = target.Replace('\\', '/').TrimStart('/');
        while (value.StartsWith("../", StringComparison.Ordinal)) value = value[3..];
        return value.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? value : "xl/" + value;
    }

    private static void AddSkip(ICollection<IoTestImportFinding> findings, string sheet, string code, int count, string text)
    {
        if (count > 0) findings.Add(Finding(IoTestImportFindingSeverity.Warning, code, $"{count:N0} {text}.", sheet: sheet));
    }

    private static IoTestImportFinding Finding(IoTestImportFindingSeverity severity, string code, string message,
        string? point = null, string? sheet = null, int? row = null) => new(severity, code, message, point, sheet, row);
    private static string Get(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
    private static string First(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
    private static List<string> Distinct(IEnumerable<ImportedRow> rows, string key) => rows.Select(x => Get(x.Values, key)).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    private static string FirstValue(IEnumerable<ImportedRow> rows, string key) => rows.Select(x => Get(x.Values, key)).FirstOrDefault(x => x.Length > 0) ?? string.Empty;
    private static string Join(IEnumerable<ImportedRow> rows, string key) => string.Join(", ", rows.Select(x => Get(x.Values, key)).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
    private static bool Bool(string value, bool fallback) => bool.TryParse(value, out var result) ? result : value is "1" or "yes" or "YES" or "x" or "X" ? true : value is "0" or "no" or "NO" ? false : fallback;
    private static int Int(string value, int fallback) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : fallback;

    private sealed record Row(int Number, IReadOnlyDictionary<int, string> Cells);
    private sealed record ImportedRow(int Number, IReadOnlyDictionary<string, string> Values);
    private sealed record IedKey(string Name, string Ip);
    private sealed class IedKeyComparer : IEqualityComparer<IedKey>
    {
        public static IedKeyComparer Instance { get; } = new();
        public bool Equals(IedKey? x, IedKey? y) => x != null && y != null && x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase) && x.Ip.Equals(y.Ip, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(IedKey obj) => HashCode.Combine(obj.Name.ToUpperInvariant(), obj.Ip.ToUpperInvariant());
    }
}
