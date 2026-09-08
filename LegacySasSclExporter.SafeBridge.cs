using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using AR.Iec61850.Scl.Export;

namespace ArIED61850Tester;

/// <summary>
/// ARSAS compatibility bridge for the pinned ARIEC61850 legacy-SAS exporter.
///
/// The pinned engine's WriteFiles implementation serializes LegacySasSclExportResult
/// directly. That result intentionally carries an XDocument for XML output, and
/// System.Text.Json walks the LINQ-to-XML parent/sibling graph until it reports an object
/// cycle (for example $.Document.Root.FirstAttribute.NextAttribute...).
///
/// Keep the engine immutable: use its Build method for all IEC 61850 conversion,
/// filtering and validation, write Document only as XML, and serialize an ARSAS-owned
/// scalar evidence DTO that cannot contain XDocument/XElement/XAttribute objects.
/// </summary>
internal static class LegacySasSclExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static LegacySasSclExportResult Build(
        XDocument source,
        string sourceName,
        LegacySasSclExportOptions options)
        => AR.Iec61850.Scl.Export.LegacySasSclExporter.Build(source, sourceName, options);

    public static LegacySasSclExportResult WriteFiles(
        string inputPath,
        string outputPath,
        LegacySasSclExportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);

        using var input = File.OpenRead(inputPath);
        var source = XDocument.Load(input, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var built = AR.Iec61850.Scl.Export.LegacySasSclExporter.Build(
            source,
            Path.GetFileName(inputPath),
            options);

        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using (var stream = File.Create(fullOutputPath))
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false
        }))
        {
            // XML is the only valid persistence representation for the XDocument.
            built.Document.Save(writer);
        }

        var reportPath = Path.ChangeExtension(fullOutputPath, ".legacy-sas-rcb-report.json");
        var summaryPath = Path.ChangeExtension(fullOutputPath, ".legacy-sas-rcb-summary.md");
        var written = built with
        {
            InputPath = Path.GetFullPath(inputPath),
            OutputPath = fullOutputPath,
            ReportPath = reportPath,
            SummaryPath = summaryPath
        };

        File.WriteAllText(
            reportPath,
            SerializeSafeReportForTest(written),
            new UTF8Encoding(false));
        File.WriteAllText(
            summaryPath,
            BuildMarkdown(written),
            new UTF8Encoding(false));

        return written;
    }

    internal static string SerializeSafeReportForTest(LegacySasSclExportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var report = new SafeLegacySasReport
        {
            GeneratedAtUtc = result.GeneratedAtUtc,
            InputPath = result.InputPath,
            OutputPath = result.OutputPath,
            ReportPath = result.ReportPath,
            SummaryPath = result.SummaryPath,
            IedName = result.IedName,
            AccessPointName = result.AccessPointName,
            SclSchema = result.SclSchema,
            RetainedReportControlReference = result.RetainedReportControlReference,
            RetainedDataSetName = result.RetainedDataSetName,
            RetainedDataSetMemberCount = result.RetainedDataSetMemberCount,
            RemovedReportControlCount = result.RemovedReportControlCount,
            RemovedDataSetCount = result.RemovedDataSetCount,
            Findings = result.Findings
                .Select(finding => new SafeLegacySasFinding
                {
                    Severity = finding.Severity,
                    Code = finding.Code,
                    Reference = finding.Reference,
                    Message = finding.Message
                })
                .ToArray()
        };

        return JsonSerializer.Serialize(report, JsonOptions);
    }

    private static string BuildMarkdown(LegacySasSclExportResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Legacy SAS Selected-RCB Export");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {result.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC");
        builder.AppendLine($"- Input: `{result.InputPath}`");
        builder.AppendLine($"- Output: `{result.OutputPath}`");
        builder.AppendLine($"- IED / AccessPoint: `{result.IedName}` / `{result.AccessPointName}`");
        builder.AppendLine($"- Schema: `{result.SclSchema}`");
        builder.AppendLine($"- Retained RCB: `{result.RetainedReportControlReference}`");
        builder.AppendLine($"- DataSet: `{result.RetainedDataSetName}` ({result.RetainedDataSetMemberCount} FCDA)");
        builder.AppendLine($"- Removed RCBs: {result.RemovedReportControlCount}");
        builder.AppendLine($"- Removed unreferenced DataSets: {result.RemovedDataSetCount}");
        builder.AppendLine();
        builder.AppendLine("The original source file was not modified. Validate the generated CID with the target SAS import workflow before operational use.");

        if (result.Findings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Findings");
            builder.AppendLine();
            foreach (var finding in result.Findings)
            {
                builder.AppendLine(
                    $"- **{finding.Severity} / {finding.Code}**" +
                    $"{(string.IsNullOrWhiteSpace(finding.Reference) ? string.Empty : $" `{finding.Reference}`")}: {finding.Message}");
            }
        }

        return builder.ToString();
    }

    private sealed class SafeLegacySasReport
    {
        public DateTimeOffset GeneratedAtUtc { get; init; }
        public string InputPath { get; init; } = string.Empty;
        public string OutputPath { get; init; } = string.Empty;
        public string ReportPath { get; init; } = string.Empty;
        public string SummaryPath { get; init; } = string.Empty;
        public string IedName { get; init; } = string.Empty;
        public string AccessPointName { get; init; } = string.Empty;
        public string SclSchema { get; init; } = string.Empty;
        public string RetainedReportControlReference { get; init; } = string.Empty;
        public string RetainedDataSetName { get; init; } = string.Empty;
        public int RetainedDataSetMemberCount { get; init; }
        public int RemovedReportControlCount { get; init; }
        public int RemovedDataSetCount { get; init; }
        public IReadOnlyList<SafeLegacySasFinding> Findings { get; init; } = Array.Empty<SafeLegacySasFinding>();
    }

    private sealed class SafeLegacySasFinding
    {
        public string Severity { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public string Reference { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }
}
