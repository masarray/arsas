using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public static class FatVerificationReportService
{
    public static byte[] GeneratePdf(FatSclWorkspaceLaunchResult launch, DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var at = generatedAt ?? DateTimeOffset.Now;
        var lines = new List<string>
        {
            "ARSAS FAT v2 - Static DataSet Verification",
            $"Generated: {at:yyyy-MM-dd HH:mm:ss zzz}",
            $"Source set: {launch.SourceSetSha256}",
            $"Signals: {launch.Project.Signals.Count} | Included: {launch.Project.IncludedSignals.Count} | Removed: {launch.Project.RemovedSignals.Count}",
            string.Empty,
            "IED | DataSet | # | Signal | Kind | Disposition | Value 1 | Value 2"
        };
        foreach (var signal in launch.Project.Signals
                     .OrderBy(signal => signal.IedName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(signal => signal.DataSetReference, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(signal => signal.DataSetMemberIndex))
        {
            lines.Add(string.Join(" | ", new[]
            {
                signal.IedName,
                signal.DataSetReference,
                signal.DataSetMemberIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                signal.StaticMemberReference,
                signal.SignalKind.ToString(),
                signal.Disposition.ToString(),
                signal.Value1Evidence?.RawValue ?? string.Empty,
                signal.Value2Evidence?.RawValue ?? string.Empty
            }));
        }
        return BuildSimplePdf(lines);
    }

    public static byte[] GenerateXlsx(FatSclWorkspaceLaunchResult launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteText(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            WriteText(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            WriteText(archive, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"FAT v2 Results\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            WriteText(archive, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            WriteText(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(launch));
        }
        return memory.ToArray();
    }

    private static string BuildWorksheetXml(FatSclWorkspaceLaunchResult launch)
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var headers = new[]
        {
            "IED", "AccessPoint", "DataSet", "MemberIndex", "StaticReference", "RuntimeReference", "FC", "DataType", "Kind", "Disposition",
            "Value1", "Value1IEDTimestamp", "Value1ARSASTimestamp", "Value1Quality", "Value1Source",
            "Value2", "Value2IEDTimestamp", "Value2ARSASTimestamp", "Value2Quality", "Value2Source", "Complete"
        };
        var sheetData = new XElement(ns + "sheetData");
        var rowNumber = 1;
        sheetData.Add(Row(ns, rowNumber++, headers));
        foreach (var signal in launch.Project.Signals
                     .OrderBy(signal => signal.IedName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(signal => signal.DataSetReference, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(signal => signal.DataSetMemberIndex))
        {
            var v1 = signal.Value1Evidence;
            var v2 = signal.Value2Evidence;
            sheetData.Add(Row(ns, rowNumber++, new[]
            {
                signal.IedName,
                signal.AccessPointName,
                signal.DataSetReference,
                signal.DataSetMemberIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                signal.StaticMemberReference,
                signal.RuntimeReference,
                signal.FunctionalConstraint,
                signal.DataType,
                signal.SignalKind.ToString(),
                signal.Disposition.ToString(),
                v1?.RawValue ?? string.Empty,
                Timestamp(v1?.IedTimestamp),
                Timestamp(v1?.CapturedAt),
                v1?.Quality ?? string.Empty,
                v1?.AcquisitionSource ?? string.Empty,
                v2?.RawValue ?? string.Empty,
                Timestamp(v2?.IedTimestamp),
                Timestamp(v2?.CapturedAt),
                v2?.Quality ?? string.Empty,
                v2?.AcquisitionSource ?? string.Empty,
                signal.HasCompleteEvidence ? "YES" : "NO"
            }));
        }
        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(ns + "worksheet", sheetData)).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement Row(XNamespace ns, int rowNumber, IReadOnlyList<string> values)
    {
        var row = new XElement(ns + "row", new XAttribute("r", rowNumber));
        for (var index = 0; index < values.Count; index++)
        {
            var text = new XElement(ns + "t", values[index] ?? string.Empty);
            if (!string.IsNullOrEmpty(values[index]) && (char.IsWhiteSpace(values[index][0]) || char.IsWhiteSpace(values[index][^1])))
                text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            row.Add(new XElement(ns + "c",
                new XAttribute("r", ColumnName(index) + rowNumber),
                new XAttribute("t", "inlineStr"),
                new XElement(ns + "is", text)));
        }
        return row;
    }

    private static string ColumnName(int zeroBased)
    {
        var value = zeroBased + 1;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private static string Timestamp(DateTimeOffset? value)
        => value?.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private static void WriteText(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static byte[] BuildSimplePdf(IReadOnlyList<string> lines)
    {
        const int linesPerPage = 48;
        var pages = lines.Chunk(linesPerPage).ToArray();
        var objects = new List<byte[]>();
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        var pageObjectIds = Enumerable.Range(0, pages.Length).Select(i => 4 + i * 2).ToArray();
        objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [{string.Join(' ', pageObjectIds.Select(id => $"{id} 0 R"))}] /Count {pages.Length} >>"));
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));

        foreach (var page in pages)
        {
            var pageId = objects.Count + 1;
            var contentId = pageId + 1;
            objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentId} 0 R >>"));
            var content = new StringBuilder("BT /F1 7 Tf 28 565 Td 0 -11 Td ");
            foreach (var line in page)
            {
                content.Append('(').Append(EscapePdf(line.Length > 150 ? line[..150] : line)).Append(") Tj T* ");
            }
            content.Append("ET");
            var stream = Encoding.ASCII.GetBytes(content.ToString());
            var header = Encoding.ASCII.GetBytes($"<< /Length {stream.Length} >>\nstream\n");
            var footer = Encoding.ASCII.GetBytes("\nendstream");
            objects.Add(header.Concat(stream).Concat(footer).ToArray());
        }

        using var output = new MemoryStream();
        void Write(string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            output.Write(bytes);
        }
        Write("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write($"{i + 1} 0 obj\n");
            output.Write(objects[i]);
            Write("\nendobj\n");
        }
        var xref = output.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++)
            Write($"{offsets[i]:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    private static string EscapePdf(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal)
            .Select(ch => ch is >= ' ' and <= '~' ? ch : '?')
            .Aggregate(new StringBuilder(), (builder, ch) => builder.Append(ch))
            .ToString();
}
