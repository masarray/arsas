using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Writes the current IO FAT evidence back into a copy of the approved ARSAS
/// import workbook. The source workbook is never modified in place.
/// </summary>
public static class IoFatExcelResultExportService
{
    private const string SignalSheetName = "ARSAS_SIGNAL_IMPORT";
    private const int MaxWorkbookBytes = 50 * 1024 * 1024;
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly string[] ResultHeaders =
    {
        "ONObservedValue",
        "ONIEDTimestamp",
        "ONARSASTimestamp",
        "ONQuality",
        "ONAcquisitionSource",
        "ONResult",
        "OFFObservedValue",
        "OFFIEDTimestamp",
        "OFFARSASTimestamp",
        "OFFQuality",
        "OFFAcquisitionSource",
        "OFFResult",
        "OverallResult",
        "TestNotes"
    };

    public static Task ExportAsync(
        string sourceWorkbookPath,
        string destinationPath,
        IoTestProject project,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorkbookPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(project);

        return Task.Run(() => ExportCore(sourceWorkbookPath, destinationPath, project, cancellationToken), cancellationToken);
    }

    private static void ExportCore(
        string sourceWorkbookPath,
        string destinationPath,
        IoTestProject project,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = new FileInfo(sourceWorkbookPath);
        if (!source.Exists)
            throw new FileNotFoundException("The approved IO FAT source workbook was not found.", sourceWorkbookPath);
        if (source.Length > MaxWorkbookBytes)
            throw new InvalidDataException($"The IO FAT workbook exceeds the {MaxWorkbookBytes / 1024 / 1024} MB safety limit.");

        var fullDestination = Path.GetFullPath(destinationPath);
        if (!fullDestination.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            fullDestination += ".xlsx";
        if (Path.GetFullPath(sourceWorkbookPath).Equals(fullDestination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a different output file. ARSAS will not overwrite the approved source workbook.");

        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var temporary = fullDestination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(sourceWorkbookPath, temporary, overwrite: false);
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Update))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sheetPath = ResolveSheetPath(archive, SignalSheetName);
                var sharedStrings = ReadSharedStrings(archive);
                var sheetDocument = LoadXml(archive, sheetPath);
                var sheetData = sheetDocument.Root?.Element(SpreadsheetNs + "sheetData")
                    ?? throw new InvalidDataException($"Sheet '{SignalSheetName}' has no sheetData element.");
                var rows = sheetData.Elements(SpreadsheetNs + "row").ToList();
                if (rows.Count == 0)
                    throw new InvalidDataException($"Sheet '{SignalSheetName}' contains no rows.");

                var headerRow = rows.OrderBy(RowNumber).First();
                var headers = ReadHeaders(headerRow, sharedStrings);
                if (!headers.TryGetValue("TestPointId", out var testPointColumn))
                    throw new InvalidDataException($"Sheet '{SignalSheetName}' is missing the TestPointId column.");

                var missing = ResultHeaders.Where(header => !headers.ContainsKey(header)).ToList();
                if (missing.Count > 0)
                {
                    throw new InvalidDataException(
                        $"The workbook is missing result column(s): {string.Join(", ", missing)}. Use the current ARSAS FAT workbook schema before exporting evidence.");
                }

                var points = project.Ieds
                    .SelectMany(ied => ied.TestPoints)
                    .ToDictionary(point => point.TestPointId, StringComparer.OrdinalIgnoreCase);
                var matched = 0;
                foreach (var row in rows.Where(row => !ReferenceEquals(row, headerRow)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rowNumber = RowNumber(row);
                    var idCell = FindCell(row, testPointColumn, rowNumber);
                    var testPointId = idCell == null ? string.Empty : ReadCellValue(idCell, sharedStrings).Trim();
                    if (string.IsNullOrWhiteSpace(testPointId) || !points.TryGetValue(testPointId, out var point))
                        continue;

                    matched++;
                    var values = ResultValues(point);
                    foreach (var value in values)
                        WriteInlineString(row, headers[value.Key], rowNumber, value.Value);
                }

                if (matched != points.Count)
                {
                    var missingCount = points.Count - matched;
                    throw new InvalidDataException(
                        $"The output workbook matched {matched} of {points.Count} test points. {missingCount} project point(s) were not found by TestPointId, so no partial result workbook was produced.");
                }

                UpdateDimension(sheetDocument, rows, headers.Values.Max());
                ReplaceXmlEntry(archive, sheetPath, sheetDocument);
            }

            File.Move(temporary, fullDestination, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static IReadOnlyDictionary<string, string> ResultValues(IoTestPointPlan point)
    {
        var on = point.Runtime.OnEvidence;
        var off = point.Runtime.OffEvidence;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ONObservedValue"] = on?.RawValue ?? string.Empty,
            ["ONIEDTimestamp"] = Timestamp(on?.IedTimestamp),
            ["ONARSASTimestamp"] = Timestamp(on?.CapturedAt),
            ["ONQuality"] = on?.Quality ?? string.Empty,
            ["ONAcquisitionSource"] = on?.AcquisitionSource ?? string.Empty,
            ["ONResult"] = EvidenceResult(on),
            ["OFFObservedValue"] = off?.RawValue ?? string.Empty,
            ["OFFIEDTimestamp"] = Timestamp(off?.IedTimestamp),
            ["OFFARSASTimestamp"] = Timestamp(off?.CapturedAt),
            ["OFFQuality"] = off?.Quality ?? string.Empty,
            ["OFFAcquisitionSource"] = off?.AcquisitionSource ?? string.Empty,
            ["OFFResult"] = EvidenceResult(off),
            ["OverallResult"] = OverallResult(point.Runtime.State),
            ["TestNotes"] = point.Runtime.StatusReason
        };
    }

    private static string Timestamp(DateTimeOffset? value)
        => global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(
            value,
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            string.Empty);

    private static string EvidenceResult(IoTestTransitionEvidence? evidence) => evidence?.Verdict switch
    {
        IoEvidenceVerdict.Accepted => "PASS",
        IoEvidenceVerdict.Rejected => "FAIL",
        IoEvidenceVerdict.Review => "REVIEW",
        _ => string.Empty
    };

    private static string OverallResult(IoTestPointState state) => state switch
    {
        IoTestPointState.Passed => "PASS",
        IoTestPointState.Failed => "FAIL",
        IoTestPointState.Review => "REVIEW",
        _ => "PENDING"
    };

    private static string ResolveSheetPath(ZipArchive archive, string sheetName)
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
        return NormalizeWorkbookTarget(target);
    }

    private static XDocument LoadXml(ZipArchive archive, string entryPath)
    {
        var entry = archive.GetEntry(entryPath)
            ?? throw new InvalidDataException($"XLSX entry '{entryPath}' is missing.");
        using var stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
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

    private static Dictionary<string, int> ReadHeaders(XElement row, IReadOnlyList<string> sharedStrings)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in row.Elements(SpreadsheetNs + "c"))
        {
            var column = ColumnIndex((string?)cell.Attribute("r"));
            var value = ReadCellValue(cell, sharedStrings).Trim();
            if (column >= 0 && !string.IsNullOrWhiteSpace(value) && !result.ContainsKey(value))
                result[value] = column;
        }
        return result;
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        var type = ((string?)cell.Attribute("t") ?? string.Empty).Trim();
        if (type == "inlineStr")
            return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(text => text.Value));
        var raw = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
        if (type == "s" && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
            index >= 0 && index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }
        if (type == "b")
            return raw == "1" ? "true" : "false";
        return raw;
    }

    private static XElement? FindCell(XElement row, int columnIndex, int rowNumber)
    {
        var reference = CellReference(columnIndex, rowNumber);
        return row.Elements(SpreadsheetNs + "c")
            .FirstOrDefault(cell => string.Equals((string?)cell.Attribute("r"), reference, StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteInlineString(XElement row, int columnIndex, int rowNumber, string? value)
    {
        var reference = CellReference(columnIndex, rowNumber);
        var cell = FindCell(row, columnIndex, rowNumber);
        if (cell == null)
        {
            cell = new XElement(SpreadsheetNs + "c", new XAttribute("r", reference));
            var next = row.Elements(SpreadsheetNs + "c")
                .FirstOrDefault(existing => ColumnIndex((string?)existing.Attribute("r")) > columnIndex);
            if (next == null)
                row.Add(cell);
            else
                next.AddBeforeSelf(cell);
        }

        cell.Elements().Remove();
        cell.SetAttributeValue("t", "inlineStr");
        var text = new XElement(SpreadsheetNs + "t", value ?? string.Empty);
        if (!string.IsNullOrEmpty(value) && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
            text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        cell.Add(new XElement(SpreadsheetNs + "is", text));
    }

    private static void UpdateDimension(XDocument document, IReadOnlyList<XElement> rows, int maximumColumn)
    {
        var maximumRow = rows.Count == 0 ? 1 : rows.Max(RowNumber);
        var reference = $"A1:{ColumnName(maximumColumn)}{maximumRow}";
        var dimension = document.Root?.Element(SpreadsheetNs + "dimension");
        if (dimension == null)
        {
            dimension = new XElement(SpreadsheetNs + "dimension", new XAttribute("ref", reference));
            document.Root?.AddFirst(dimension);
        }
        else
        {
            dimension.SetAttributeValue("ref", reference);
        }
    }

    private static void ReplaceXmlEntry(ZipArchive archive, string path, XDocument document)
    {
        var old = archive.GetEntry(path) ?? throw new InvalidDataException($"XLSX entry '{path}' is missing.");
        old.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, SaveOptions.DisableFormatting);
    }

    private static int RowNumber(XElement row)
        => (int?)row.Attribute("r") ?? 1;

    private static int ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
            return -1;
        var index = 0;
        var found = false;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character))
                break;
            found = true;
            index = checked(index * 26 + (char.ToUpperInvariant(character) - 'A' + 1));
        }
        return found ? index - 1 : -1;
    }

    private static string CellReference(int columnIndex, int rowNumber)
        => ColumnName(columnIndex) + rowNumber.ToString(CultureInfo.InvariantCulture);

    private static string ColumnName(int zeroBasedIndex)
    {
        var value = checked(zeroBasedIndex + 1);
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + (value % 26)) + result;
            value /= 26;
        }
        return result;
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
}
