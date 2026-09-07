using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Multi-select generic RCB export. Source-backed RCBs are sliced directly from the source
/// SCL XML in ARSAS so XDocument/XElement never enters JSON serialization. Live-only RCBs
/// retain the proven singular fallback and are merged into that XML by exact LN scope.
/// ARIEC61850 remains immutable.
/// </summary>
public partial class MainWindow
{
    [ModuleInitializer]
    internal static void RegisterMultiRcbExportButtonClassHandler()
    {
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
                // Availability is evidence only. Probe automatically for live-only rows so
                // the export route does not need a separate warning/confirmation workflow.
                if (latestAvailability == null && device.IsConnected && selected.Any(row => !row.IsSourceBacked))
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
        var sourceRows = uniqueRows.Where(row => row.IsSourceBacked).ToArray();
        var liveOnlyRows = uniqueRows.Where(row => !row.IsSourceBacked).ToArray();
        var hasSource = sourceRows.Length > 0 &&
                        !string.IsNullOrWhiteSpace(device.SclSourcePath) &&
                        File.Exists(device.SclSourcePath);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"arsas-rcb-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            XDocument? merged = null;

            if (hasSource)
            {
                cancellationToken.ThrowIfCancellationRequested();
                merged = BuildSourceBackedMultiRcbDocument(device, sourceRows, cancellationToken);
            }
            else if (sourceRows.Length > 0)
            {
                throw new InvalidOperationException("The selected source-backed RCBs require their original SCL file, but that file is no longer available.");
            }

            // Never send source-backed XML through the legacy exporter: that path can enter
            // System.Text.Json and recurse through XAttribute linked-list ownership. Only
            // genuinely live-only rows use the singular fallback.
            for (var index = 0; index < liveOnlyRows.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = liveOnlyRows[index];
                var tempPath = Path.Combine(tempRoot, $"live-selected-{index + 1:D3}.cid");
                var completion = await ExportLegacySasRcbAsync(
                        device,
                        row,
                        schema,
                        tempPath,
                        availability,
                        cancellationToken)
                    .ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(completion.OutputPath) || !File.Exists(completion.OutputPath))
                    throw new InvalidOperationException($"The live-only staging export for '{row.Name}' did not produce an SCL file.");

                var additional = XDocument.Load(
                    completion.OutputPath,
                    LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
                if (merged == null)
                    merged = additional;
                else
                {
                    MergeScopedLnChildren(merged, additional, "DataSet");
                    MergeScopedLnChildren(merged, additional, "ReportControl");
                }
            }

            if (merged == null)
                throw new InvalidOperationException("No selected RCB could be projected into the generic SCL document.");

            ValidateGenericMultiRcbDocument(merged, uniqueRows.Length);

            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            merged.Save(outputPath, SaveOptions.DisableFormatting);

            AddLog("INFO", "RCB Export",
                $"{device.Name}: generic multi-RCB SCL saved; selected RCB={uniqueRows.Length}; source-backed={sourceRows.Length}; live-only={liveOnlyRows.Length}; XML-only source slicing=true; output={outputPath}");
            SetStatus($"{device.Name}: generic SCL exported with {uniqueRows.Length} selected RCB(s).");

            return new RcbExportCompletion
            {
                OutputPath = outputPath,
                SchemaDisplayName = schema.ToString(),
                RetainedReportControl = string.Join(", ", uniqueRows.Select(row => row.Name)),
                DataSetName = string.Join(", ", uniqueRows.Select(row => row.DataSetName).Where(name => !string.IsNullOrWhiteSpace(name) && name != "—").Distinct(StringComparer.OrdinalIgnoreCase)),
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

    private XDocument BuildSourceBackedMultiRcbDocument(
        Iec61850MonitorDevice device,
        IReadOnlyList<RcbExportRow> selectedRows,
        CancellationToken cancellationToken)
    {
        var sourcePath = device.SclSourcePath;
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new InvalidOperationException("Source SCL is unavailable for source-backed RCB export.");

        var document = XDocument.Load(sourcePath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var targetIedName = EffectiveSclIedName(device);
        var targetIed = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "IED" &&
                string.Equals(element.Attribute("name")?.Value, targetIedName, StringComparison.OrdinalIgnoreCase));
        if (targetIed == null)
            throw new InvalidOperationException($"IED '{targetIedName}' was not found in the source SCL.");

        var selected = new HashSet<XElement>();
        foreach (var row in selectedRows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = FindSourceReportControl(targetIed, row);
            if (match == null)
                throw new InvalidOperationException($"Selected RCB '{row.Reference}' could not be resolved uniquely in the source SCL.");
            selected.Add(match);
        }

        // Capture the exact native DataSet identity before removing unselected RCBs.
        var requiredDataSets = selected
            .Select(reportControl => new
            {
                Parent = reportControl.Parent,
                Name = reportControl.Attribute("datSet")?.Value?.Trim() ?? string.Empty
            })
            .Where(item => item.Parent != null && item.Name.Length > 0)
            .ToArray();

        foreach (var reportControl in document.Descendants()
                     .Where(element => element.Name.LocalName == "ReportControl")
                     .ToArray())
        {
            if (!selected.Contains(reportControl))
                reportControl.Remove();
        }

        foreach (var dataSet in document.Descendants()
                     .Where(element => element.Name.LocalName == "DataSet")
                     .ToArray())
        {
            var name = dataSet.Attribute("name")?.Value?.Trim() ?? string.Empty;
            var needed = requiredDataSets.Any(item =>
                ReferenceEquals(item.Parent, dataSet.Parent) &&
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (!needed)
                dataSet.Remove();
        }

        return document;
    }

    private static XElement? FindSourceReportControl(XElement targetIed, RcbExportRow row)
    {
        var candidates = targetIed.Descendants()
            .Where(element => element.Name.LocalName == "ReportControl")
            .Where(element => string.Equals(
                element.Attribute("name")?.Value,
                row.ExportName,
                StringComparison.OrdinalIgnoreCase) ||
                string.Equals(element.Attribute("name")?.Value, row.Name, StringComparison.OrdinalIgnoreCase))
            .Where(element =>
            {
                var buffered = bool.TryParse(element.Attribute("buffered")?.Value, out var value) && value;
                return buffered == row.Buffered;
            })
            .ToArray();

        if (candidates.Length == 1)
            return candidates[0];

        var normalizedTarget = NormalizeRcbReference(row.Reference);
        var exact = candidates.Where(candidate =>
            string.Equals(
                NormalizeRcbReference(BuildSourceReportControlReference(candidate, row.Buffered)),
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        return exact.Length == 1 ? exact[0] : null;
    }

    private static string BuildSourceReportControlReference(XElement reportControl, bool buffered)
    {
        var logicalNode = reportControl.Parent;
        var ied = reportControl.Ancestors().FirstOrDefault(element => element.Name.LocalName == "IED");
        var lDevice = reportControl.Ancestors().FirstOrDefault(element => element.Name.LocalName == "LDevice");
        if (logicalNode == null || ied == null || lDevice == null)
            return string.Empty;

        var ln = logicalNode.Name.LocalName == "LN0"
            ? "LLN0"
            : $"{logicalNode.Attribute("prefix")?.Value}{logicalNode.Attribute("lnClass")?.Value}{logicalNode.Attribute("inst")?.Value}";
        var service = buffered ? "BR" : "RP";
        return $"{ied.Attribute("name")?.Value}{lDevice.Attribute("inst")?.Value}/{ln}.{service}.{reportControl.Attribute("name")?.Value}";
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
