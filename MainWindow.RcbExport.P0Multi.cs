using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private static readonly XNamespace P0Scl = "http://www.iec.ch/61850/2003/SCL";

    internal async Task<RcbExportCompletion> ExportLegacySasRcbsP0Async(
        string iedName,
        IReadOnlyList<RcbExportRow> rows,
        SclSchemaProfile schema,
        string outputPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iedName);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            throw new InvalidOperationException("Select at least one RCB before export.");

        var device = Devices.FirstOrDefault(candidate =>
            candidate.Name.Equals(iedName, StringComparison.OrdinalIgnoreCase));
        if (device == null)
            throw new InvalidOperationException($"IED '{iedName}' is not loaded in the Engineering workspace.");

        if (string.IsNullOrWhiteSpace(device.SclSourcePath) || !File.Exists(device.SclSourcePath))
        {
            throw new InvalidOperationException(
                "Multi-RCB Legacy SAS export requires the opened source CID/SCD/ICD so every selected ReportControl/DataSet can be retained without inventing configuration.");
        }

        var sourceBacked = rows
            .Where(row => row.IsSourceBacked && !string.IsNullOrWhiteSpace(row.SourceSelectionKey))
            .ToArray();
        if (sourceBacked.Length != rows.Count)
        {
            throw new InvalidOperationException(
                "One or more selected RCBs exist only in the live discovery model and cannot be mapped safely to the source SCL. Select source-backed RCBs for this P0 export.");
        }

        var unpopulated = sourceBacked
            .Where(row => row.MemberCount <= 0 || string.IsNullOrWhiteSpace(row.DataSetReference))
            .ToArray();
        if (unpopulated.Length > 0)
        {
            var names = string.Join(", ", unpopulated.Select(row => row.Name));
            throw new InvalidOperationException(
                $"Selected RCB(s) {names} have no populated static DataSet. They remain selectable as discovered/dynamic RCB evidence, but this P0 CID export will not invent a DataSet binding.");
        }

        var selections = sourceBacked
            .Select(row => new SclReportControlSelection(row.SourceSelectionKey, row.ExportName))
            .ToArray();

        var completion = await Task.Run(() => BuildAndWriteMultiRcbCid(
            device.SclSourcePath,
            outputPath,
            EffectiveSclIedName(device),
            device.SclAccessPointName,
            schema,
            sourceBacked,
            selections,
            cancellationToken), cancellationToken);

        AddLog("INFO", "RCB Export",
            $"{device.Name}: legacy SAS CID saved; retained RCB={sourceBacked.Length}; members={sourceBacked.Sum(row => row.MemberCount)}; removed RCB={completion.RemovedReportControlCount}; output={completion.OutputPath}");
        SetStatus($"{device.Name}: legacy SAS CID exported with {sourceBacked.Length} selected RCB(s).");
        return completion;
    }

    private static RcbExportCompletion BuildAndWriteMultiRcbCid(
        string inputPath,
        string outputPath,
        string iedName,
        string accessPointName,
        SclSchemaProfile schemaProfile,
        IReadOnlyList<RcbExportRow> rows,
        IReadOnlyList<SclReportControlSelection> selections,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var input = File.OpenRead(inputPath);
        var source = XDocument.Load(input, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);

        var normalized = InteroperableSclConverter.Convert(
            source,
            Path.GetFileName(inputPath),
            new InteroperableSclConversionOptions
            {
                IedName = iedName,
                PreserveAllIeds = false,
                RemoveExternalInputs = true,
                RemoveUnusedTypeTemplates = true,
                RemoveSubstationSection = true,
                ToolId = "ARIEC61850"
            });

        var filtered = SclReportControlFilter.Filter(
            normalized.Document,
            new SclReportControlFilterOptions
            {
                IedName = normalized.SelectedIedName,
                AccessPointName = accessPointName,
                SelectedReportControls = selections,
                RequireExactlyOneReportControl = false,
                RemoveUnreferencedDataSets = false,
                CollapseIndexedSelectionToSingleInstance = true
            },
            Path.GetFileName(inputPath));

        if (filtered.RetainedReportControls.Count != rows.Count)
            throw new InvalidDataException($"Multi-RCB export expected {rows.Count} retained ReportControl(s), found {filtered.RetainedReportControls.Count}.");
        if (filtered.RetainedReportControls.Any(item => !item.HasPopulatedDataSet))
            throw new InvalidDataException("Every selected ReportControl must reference a populated DataSet.");

        var document = new XDocument(filtered.Document);
        ApplyP0ExactRuntimeIdentities(document, rows);
        var schema = SclSchemaProfiles.Get(schemaProfile);
        ApplyP0SchemaProfile(document.Root ?? throw new InvalidDataException("Filtered SCL document has no root element."), schema);
        ValidateP0MultiRcbDocument(document, normalized.SelectedIedName, rows.Count);

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
            document.Save(writer);
        }

        var reportPath = Path.ChangeExtension(fullOutputPath, ".legacy-sas-rcb-report.json");
        var summaryPath = Path.ChangeExtension(fullOutputPath, ".legacy-sas-rcb-summary.md");
        var retained = rows.Select(row => new
        {
            row.Name,
            row.Reference,
            row.DataSetName,
            row.MemberCount
        }).ToArray();
        var report = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            inputPath = Path.GetFullPath(inputPath),
            outputPath = fullOutputPath,
            iedName = normalized.SelectedIedName,
            accessPointName,
            schema = schema.DisplayName,
            retainedReportControls = retained,
            totalMembers = rows.Sum(row => row.MemberCount),
            removedReportControls = filtered.RemovedReportControlCount,
            removedDataSets = filtered.RemovedDataSetCount
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));

        var summary = new StringBuilder()
            .AppendLine("# Legacy SAS Multi-RCB Export")
            .AppendLine()
            .AppendLine($"- IED / AccessPoint: `{normalized.SelectedIedName}` / `{accessPointName}`")
            .AppendLine($"- Schema: `{schema.DisplayName}`")
            .AppendLine($"- Retained RCBs: {rows.Count}");
        foreach (var row in rows)
            summary.AppendLine($"  - `{row.Name}` → `{row.DataSetName}` ({row.MemberCount} FCDA)");
        summary.AppendLine($"- Total retained DataSet members: {rows.Sum(row => row.MemberCount)}")
            .AppendLine($"- Removed RCBs: {filtered.RemovedReportControlCount}")
            .AppendLine()
            .AppendLine("The original source file was not modified.");
        File.WriteAllText(summaryPath, summary.ToString(), new UTF8Encoding(false));

        return new RcbExportCompletion
        {
            OutputPath = fullOutputPath,
            ReportPath = reportPath,
            SummaryPath = summaryPath,
            SchemaDisplayName = schema.DisplayName,
            RetainedReportControl = string.Join(", ", rows.Select(row => row.Name)),
            DataSetName = string.Join(", ", rows.Select(row => row.DataSetName).Distinct(StringComparer.OrdinalIgnoreCase)),
            DataSetMemberCount = rows.Sum(row => row.MemberCount),
            RemovedReportControlCount = filtered.RemovedReportControlCount,
            Message = $"Export complete: {rows.Count} selected RCB(s) retained in one CID · {rows.Sum(row => row.MemberCount)} FCDA total."
        };
    }

    private static void ApplyP0ExactRuntimeIdentities(XDocument document, IReadOnlyList<RcbExportRow> rows)
    {
        var controls = document.Descendants(P0Scl + "ReportControl").ToArray();
        foreach (var row in rows.Where(row => !string.IsNullOrWhiteSpace(row.ExportName)))
        {
            var sourceName = SourceRcbName(row.SourceSelectionKey);
            var targetName = row.ExportName.Trim();
            var matches = controls.Where(element =>
                    string.Equals((string?)element.Attribute("name"), targetName, StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(sourceName) && string.Equals((string?)element.Attribute("name"), sourceName, StringComparison.Ordinal)))
                .Distinct()
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException($"Could not uniquely map selected RCB '{row.Name}' into the filtered CID.");

            var control = matches[0];
            control.SetAttributeValue("name", targetName);
            control.SetAttributeValue("indexed", "false");
            foreach (var rptEnabled in control.Elements(P0Scl + "RptEnabled").ToArray())
                rptEnabled.Remove();
        }
    }

    private static string SourceRcbName(string selectionKey)
    {
        if (string.IsNullOrWhiteSpace(selectionKey)) return string.Empty;
        var index = selectionKey.LastIndexOf('|');
        return index >= 0 && index + 1 < selectionKey.Length ? selectionKey[(index + 1)..] : string.Empty;
    }

    private static void ApplyP0SchemaProfile(XElement root, SclSchemaProfileDescriptor schema)
    {
        root.SetAttributeValue("version", schema.RootVersion);
        root.SetAttributeValue("revision", schema.RootRevision);
        if (!schema.SupportsTriggerGi)
        {
            foreach (var triggerOptions in root.Descendants(P0Scl + "TrgOps"))
                triggerOptions.SetAttributeValue("gi", null);
        }
        if (!schema.IsEdition2)
        {
            foreach (var confReportControl in root.Descendants(P0Scl + "ConfReportControl"))
                confReportControl.SetAttributeValue("bufConf", null);
        }
        if (!schema.SupportsReservationTime)
        {
            foreach (var reportSettings in root.Descendants(P0Scl + "ReportSettings"))
            {
                reportSettings.SetAttributeValue("owner", null);
                reportSettings.SetAttributeValue("resvTms", null);
            }
            foreach (var service in root.Descendants().Where(element => element.Name.LocalName is "SGEdit" or "ConfSG"))
                service.SetAttributeValue("resvTms", null);
        }
    }

    private static void ValidateP0MultiRcbDocument(XDocument document, string iedName, int expectedCount)
    {
        var parsed = new SclParser().Parse(document, "legacy-sas-multi.cid");
        if (!parsed.Ieds.Any(item => item.Name.Equals(iedName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Filtered SCL validation lost IED '{iedName}'.");
        if (parsed.ReportControls.Count != expectedCount)
            throw new InvalidDataException($"Filtered SCL validation expected {expectedCount} ReportControl(s), found {parsed.ReportControls.Count}.");
        foreach (var retained in parsed.ReportControls)
        {
            if (retained.DataSetBindingStatus != SclDataSetBindingStatus.Resolved || retained.Entries.Count == 0)
                throw new InvalidDataException($"Filtered SCL validation found an unresolved or empty DataSet for '{retained.ControlBlockReference}'.");
        }
    }
}
