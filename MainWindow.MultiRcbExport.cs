using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Upgrades the legacy single-RCB export entry point to a multi-select generic SCL workflow
/// without changing ARIEC61850. Each selected RCB is first rendered by the existing proven
/// single-RCB exporter, then the resulting native DataSet/ReportControl elements are merged
/// by their exact IED/LDevice/LN scope. This deliberately never derives DataSets from ARSAS
/// runtime monitor selections or temporary dynamic acquisition state.
/// </summary>
public partial class MainWindow
{
    private static readonly string[] ArsasRuntimeDataSetPrefixes =
    {
        "ARIED_",
        "AR_G24_"
    };

    [ModuleInitializer]
    internal static void RegisterMultiRcbExportButtonClassHandler()
    {
        // ModuleInitializer is intentional. An unreferenced static bool on a beforefieldinit
        // partial class is not a reliable WPF registration point and was the reason the relay
        // bench could still open the legacy one-RCB dialog. Registration now happens when the
        // assembly is loaded, before any MainWindow button can be realized.
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MultiRcbExportButton_Loaded),
            handledEventsToo: true);
    }

    private static void MultiRcbExportButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || Window.GetWindow(button) is not MainWindow window)
            return;

        var toolTip = button.ToolTip?.ToString() ?? string.Empty;
        if (!toolTip.Contains("RCB Export Filter", StringComparison.OrdinalIgnoreCase) &&
            !toolTip.Contains("RCB Export", StringComparison.OrdinalIgnoreCase))
            return;

        button.Click -= window.IedEditRcb_Click;
        button.Click -= window.IedEditRcbMulti_Click;
        button.Click += window.IedEditRcbMulti_Click;
        button.ToolTip = "RCB Export — select any number of native RCBs and export generic interoperable IEC 61850 SCL";
    }

    private void IedEditRcbMulti_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDeviceFromButton(sender, out var device) || device.IsBusy || device.IsDemo)
            return;

        SclReportControlInventoryResult? sourceInventory = null;
        Exception? sourceInspectionError = null;
        MmsRcbAvailabilityResult? latestAvailability = null;

        if (!string.IsNullOrWhiteSpace(device.SclSourcePath) && File.Exists(device.SclSourcePath))
        {
            try
            {
                sourceInventory = SclReportControlFilter.InspectFile(
                    device.SclSourcePath,
                    EffectiveSclIedName(device),
                    device.SclAccessPointName);
            }
            catch (Exception ex)
            {
                sourceInspectionError = ex;
                AddLog("WARN", "RCB Export", $"{device.Name}: source SCL RCB inventory failed: {ex.Message}");
            }
        }

        var rows = BuildRcbExportRows(device, sourceInventory, availability: null);
        if (rows.Count == 0)
        {
            var detail = sourceInspectionError?.Message ??
                         "No Report Control Blocks are available in the opened SCL or last successful live discovery model.";
            MessageBox.Show(this, detail, "RCB Export", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus($"{device.Name}: no RCB inventory is available for generic SCL export.");
            return;
        }

        SelectedDevice = device;
        var dialog = new RcbMultiExportWindow(
            device.Name,
            device.EndpointText,
            rows,
            device.IsConnected
                ? async cancellationToken =>
                {
                    latestAvailability = await _rcbAvailabilityProbe
                        .CheckAsync(device, cancellationToken)
                        .ConfigureAwait(true);
                    return BuildRcbExportRows(device, sourceInventory, latestAvailability);
                }
                : null,
            async (selected, schema, outputPath, cancellationToken) =>
            {
                // Live-only RCBs may need the DataSet directory evidence that the original
                // singular exporter already validates. Collect it automatically so users do
                // not have to remember a separate Check Availability click before export.
                if (latestAvailability == null && device.IsConnected)
                {
                    latestAvailability = await _rcbAvailabilityProbe
                        .CheckAsync(device, cancellationToken)
                        .ConfigureAwait(true);
                }

                return await ExportGenericMultiRcbAsync(
                        device,
                        selected,
                        schema,
                        outputPath,
                        latestAvailability,
                        cancellationToken)
                    .ConfigureAwait(true);
            })
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private async Task<RcbExportCompletion> ExportGenericMultiRcbAsync(
        Iec61850MonitorDevice device,
        IReadOnlyList<RcbExportRow> selectedRows,
        SclSchemaProfile schema,
        string outputPath,
        MmsRcbAvailabilityResult? availability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (selectedRows == null || selectedRows.Count == 0)
            throw new InvalidOperationException("Select at least one RCB before export.");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("An output SCL path is required.", nameof(outputPath));

        var uniqueRows = selectedRows
            .GroupBy(row => row.SelectionIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var tempRoot = Path.Combine(Path.GetTempPath(), $"arsas-rcb-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var singularFiles = new List<string>(uniqueRows.Length);
            for (var index = 0; index < uniqueRows.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = uniqueRows[index];
                var tempPath = Path.Combine(tempRoot, $"selected-{index + 1:D3}.cid");
                var completion = await ExportLegacySasRcbAsync(
                        device,
                        row,
                        schema,
                        tempPath,
                        availability,
                        cancellationToken)
                    .ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(completion.OutputPath) || !File.Exists(completion.OutputPath))
                    throw new InvalidOperationException($"The single-RCB staging export for '{row.Name}' did not produce an SCL file.");
                singularFiles.Add(completion.OutputPath);
            }

            var merged = XDocument.Load(singularFiles[0], LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            for (var index = 1; index < singularFiles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var additional = XDocument.Load(singularFiles[index], LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                MergeScopedLnChildren(merged, additional, "DataSet");
                MergeScopedLnChildren(merged, additional, "ReportControl");
            }

            ValidateGenericMultiRcbDocument(merged, uniqueRows.Length);

            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            merged.Save(outputPath, SaveOptions.DisableFormatting);

            AddLog("INFO", "RCB Export",
                $"{device.Name}: generic multi-RCB SCL saved; selected RCB={uniqueRows.Length}; native/static DataSets retained from authoritative singular exports; output={outputPath}");
            SetStatus($"{device.Name}: generic SCL exported with {uniqueRows.Length} selected RCB(s).");

            return new RcbExportCompletion
            {
                OutputPath = outputPath,
                SchemaDisplayName = schema.ToString(),
                RetainedReportControl = string.Join(", ", uniqueRows.Select(row => row.Name)),
                DataSetName = string.Join(", ", uniqueRows.Select(row => row.DataSetName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)),
                DataSetMemberCount = uniqueRows.Sum(row => Math.Max(0, row.MemberCount)),
                RemovedReportControlCount = Math.Max(0, BuildRcbExportRows(device, sourceInventory: null, availability).Count - uniqueRows.Length),
                Message = $"Export complete: {uniqueRows.Length} selected RCB(s) retained with their native/static IED DataSets. No ARSAS runtime acquisition DataSet was generated."
            };
        }
        finally
        {
            TryDeleteMultiRcbTempDirectory(tempRoot);
        }
    }

    private static void MergeScopedLnChildren(XDocument target, XDocument source, string localName)
    {
        foreach (var sourceChild in source.Descendants().Where(element => element.Name.LocalName == localName))
        {
            var sourceLn = sourceChild.Parent;
            if (sourceLn == null || sourceLn.Name.LocalName is not ("LN" or "LN0"))
                continue;

            var identity = BuildLogicalNodeMergeIdentity(sourceLn);
            var targetLn = target.Descendants()
                .Where(element => element.Name.LocalName is "LN" or "LN0")
                .FirstOrDefault(element => BuildLogicalNodeMergeIdentity(element)
                    .Equals(identity, StringComparison.OrdinalIgnoreCase));
            if (targetLn == null)
                throw new InvalidOperationException($"Cannot merge {localName}: logical-node scope '{identity}' is missing from the base SCL.");

            var name = sourceChild.Attribute("name")?.Value ?? string.Empty;
            var alreadyExists = targetLn.Elements()
                .Where(element => element.Name.LocalName == localName)
                .Any(element => string.Equals(element.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase));
            if (!alreadyExists)
                targetLn.Add(new XElement(sourceChild));
        }
    }

    private static string BuildLogicalNodeMergeIdentity(XElement logicalNode)
    {
        var ied = logicalNode.Ancestors().FirstOrDefault(element => element.Name.LocalName == "IED")
                  ?? throw new InvalidOperationException("SCL logical node is not contained by an IED.");
        var accessPoint = logicalNode.Ancestors().FirstOrDefault(element => element.Name.LocalName == "AccessPoint");
        var lDevice = logicalNode.Ancestors().FirstOrDefault(element => element.Name.LocalName == "LDevice")
                      ?? throw new InvalidOperationException("SCL logical node is not contained by an LDevice.");
        var lnIdentity = logicalNode.Name.LocalName == "LN0"
            ? "LLN0"
            : $"{logicalNode.Attribute("prefix")?.Value}{logicalNode.Attribute("lnClass")?.Value}{logicalNode.Attribute("inst")?.Value}";
        return $"{ied.Attribute("name")?.Value}|{accessPoint?.Attribute("name")?.Value}|{lDevice.Attribute("inst")?.Value}|{lnIdentity}";
    }

    private static void ValidateGenericMultiRcbDocument(XDocument document, int expectedReportControls)
    {
        var reportControls = document.Descendants()
            .Where(element => element.Name.LocalName == "ReportControl")
            .ToArray();
        if (reportControls.Length != expectedReportControls)
        {
            throw new InvalidOperationException(
                $"Generic SCL validation failed: expected {expectedReportControls} selected ReportControl element(s), found {reportControls.Length}.");
        }

        // Runtime acquisition DataSets are association-scoped state, never engineering
        // configuration. Reject known ARSAS temporary namespaces even if a staging exporter
        // or live RCB inventory accidentally carries one into the merged XML.
        var leakedRuntimeDataSets = document.Descendants()
            .Where(element => element.Name.LocalName == "DataSet")
            .Select(element => element.Attribute("name")?.Value?.Trim() ?? string.Empty)
            .Where(IsArsasRuntimeDataSetNameForTest)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (leakedRuntimeDataSets.Length > 0)
        {
            throw new InvalidOperationException(
                "Generic SCL validation failed: transient ARSAS runtime DataSet(s) must not be exported: " +
                string.Join(", ", leakedRuntimeDataSets));
        }

        foreach (var reportControl in reportControls)
        {
            var dataSetName = reportControl.Attribute("datSet")?.Value?.Trim() ?? string.Empty;
            if (dataSetName.Length == 0)
                continue;

            var logicalNode = reportControl.Parent;
            if (logicalNode == null)
                throw new InvalidOperationException("Generic SCL validation failed: ReportControl has no logical-node parent.");

            var hasDataSet = logicalNode.Elements()
                .Where(element => element.Name.LocalName == "DataSet")
                .Any(element => string.Equals(element.Attribute("name")?.Value, dataSetName, StringComparison.OrdinalIgnoreCase));
            if (!hasDataSet)
            {
                throw new InvalidOperationException(
                    $"Generic SCL validation failed: ReportControl '{reportControl.Attribute("name")?.Value}' references missing DataSet '{dataSetName}'.");
            }
        }
    }

    internal static bool IsArsasRuntimeDataSetNameForTest(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        return normalized.Length > 0 && ArsasRuntimeDataSetPrefixes.Any(prefix =>
            normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static void TryDeleteMultiRcbTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
