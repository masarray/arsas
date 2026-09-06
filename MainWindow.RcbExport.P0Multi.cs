using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
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
                "Multi-RCB Legacy SAS export currently requires the opened source CID/SCD/ICD so every selected ReportControl/DataSet can be retained without inventing configuration.");
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

        var result = await Task.Run(() => LegacySasSclExporter.WriteFiles(
            device.SclSourcePath,
            outputPath,
            new LegacySasSclExportOptions
            {
                IedName = EffectiveSclIedName(device),
                AccessPointName = device.SclAccessPointName,
                SchemaProfile = schema,
                SelectedReportControls = selections,
                RemoveUnreferencedDataSets = false,
                ToolId = "ARIEC61850"
            }), cancellationToken);

        var retainedCount = result.RetainedReportControlCount;
        var retainedDetail = string.Join(
            "; ",
            result.RetainedReportControls.Select(item =>
                $"{item.Reference} -> {item.DataSetName} ({item.DataSetMemberCount} FCDA)"));

        AddLog("INFO", "RCB Export",
            $"{device.Name}: legacy SAS CID saved; schema={result.SclSchema}; retained RCB={retainedCount}; {retainedDetail}; removed RCB={result.RemovedReportControlCount}; output={result.OutputPath}");
        SetStatus($"{device.Name}: legacy SAS CID exported with {retainedCount} selected RCB(s).");

        return new RcbExportCompletion
        {
            OutputPath = result.OutputPath,
            ReportPath = result.ReportPath,
            SummaryPath = result.SummaryPath,
            SchemaDisplayName = result.SclSchema,
            RetainedReportControl = result.RetainedReportControlReference,
            DataSetName = result.RetainedDataSetName,
            DataSetMemberCount = result.RetainedDataSetMemberCount,
            RemovedReportControlCount = result.RemovedReportControlCount,
            Message = $"Export complete: {retainedCount} selected RCB(s) retained in one CID · {result.RetainedDataSetMemberCount} FCDA total."
        };
    }
}
