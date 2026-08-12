using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using System.Windows;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly Services.RcbAvailabilityProbeService _rcbAvailabilityProbe = new();

    private void IedEditRcb_Click(object sender, RoutedEventArgs e)
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
            MessageBox.Show(this, detail, "RCB Export Filter", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus($"{device.Name}: no RCB inventory is available for legacy SAS export.");
            return;
        }

        SelectedDevice = device;
        var options = new RcbExportWindowOptions
        {
            IedName = device.Name,
            Endpoint = device.EndpointText,
            IsMock = false,
            CanCheckAvailability = device.IsConnected,
            Rows = rows,
            RefreshAvailabilityAsync = device.IsConnected
                ? async cancellationToken =>
                {
                    latestAvailability = await _rcbAvailabilityProbe.CheckAsync(device, cancellationToken).ConfigureAwait(false);
                    return BuildRcbExportRows(device, sourceInventory, latestAvailability);
                }
                : null,
            ExportAsync = (row, schema, outputPath, cancellationToken) =>
                ExportLegacySasRcbAsync(device, row, schema, outputPath, latestAvailability, cancellationToken)
        };

        var dialog = new RcbExportFilterWindow(options)
        {
            Owner = this,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.ShowDialog();
    }

    private static IReadOnlyList<RcbExportRow> BuildRcbExportRows(
        Iec61850MonitorDevice device,
        SclReportControlInventoryResult? sourceInventory,
        MmsRcbAvailabilityResult? availability)
    {
        if (sourceInventory != null)
            return BuildSourceBackedRcbRows(device, sourceInventory, availability);
        return BuildLiveModelRcbRows(device.LiveDiscoveryModel, availability);
    }

    private static IReadOnlyList<RcbExportRow> BuildSourceBackedRcbRows(
        Iec61850MonitorDevice device,
        SclReportControlInventoryResult inventory,
        MmsRcbAvailabilityResult? availability)
    {
        var rows = new List<RcbExportRow>();
        var mappedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (availability != null)
        {
            foreach (var snapshot in availability.ReportControls)
            {
                var descriptor = FindSourceDescriptor(snapshot, inventory.ReportControls);
                if (descriptor == null) continue;
                mappedKeys.Add(descriptor.SelectionKey);
                rows.Add(CreateSourceBackedRow(descriptor, snapshot));
            }
        }

        foreach (var descriptor in inventory.ReportControls)
        {
            if (mappedKeys.Contains(descriptor.SelectionKey)) continue;
            rows.Add(CreateSourceBackedRow(descriptor, snapshot: null, device.IsConnected));
        }
        return rows;
    }

    private static RcbExportRow CreateSourceBackedRow(
        SclReportControlDescriptor descriptor,
        MmsRcbAvailabilitySnapshot? snapshot,
        bool connected = true)
    {
        var effectiveDataSetReference = RcbExportEvidencePolicy.EffectiveDataSetReference(
            snapshot?.DataSetReference,
            descriptor.DataSetReference);
        var memberCount = RcbExportEvidencePolicy.EffectiveMemberCount(
            snapshot?.DataSetMemberCount ?? 0,
            descriptor.DataSetMemberCount);
        var evidenceConflict = snapshot != null &&
                               !string.IsNullOrWhiteSpace(snapshot.DataSetReference) &&
                               !string.IsNullOrWhiteSpace(descriptor.DataSetReference) &&
                               !NormalizeRcbReference(snapshot.DataSetReference)
                                   .Equals(NormalizeRcbReference(descriptor.DataSetReference), StringComparison.OrdinalIgnoreCase);
        var availability = evidenceConflict
            ? MmsRcbOperationalAvailability.Unknown
            : RcbExportEvidencePolicy.SourceAvailability(
                snapshot?.Availability,
                descriptor.DataSetName,
                descriptor.DataSetResolved,
                descriptor.DataSetMemberCount);
        var effectiveDataSetName = !string.IsNullOrWhiteSpace(snapshot?.DataSetReference)
            ? LastReferenceSegment(snapshot.DataSetReference)
            : descriptor.DataSetName;
        var scope = !string.IsNullOrWhiteSpace(descriptor.LogicalDeviceInstance) ||
                    !string.IsNullOrWhiteSpace(descriptor.LogicalNodePath)
            ? $"{descriptor.LogicalDeviceInstance} / {descriptor.LogicalNodePath}".Trim(' ', '/')
            : RcbExportEvidencePolicy.ScopeFromReference(snapshot?.Reference ?? descriptor.DisplayReference);
        var reason = evidenceConflict
            ? $"Configuration mismatch: source SCL binds {descriptor.DataSetReference}, while the live IED reports {snapshot!.DataSetReference}. Export is blocked until the mismatch is resolved."
            : snapshot?.Reason ?? RcbExportEvidencePolicy.SourceReason(
                descriptor.DataSetName,
                descriptor.DataSetResolved,
                descriptor.DataSetMemberCount,
                connected);
        var dataSetDetail = evidenceConflict
            ? $"Configuration mismatch • source {descriptor.DataSetReference} • live {snapshot!.DataSetReference}"
            : snapshot?.DataSetDirectorySuccess == true
                ? "Static DataSet • live directory verified"
                : string.IsNullOrWhiteSpace(descriptor.DataSetName)
                    ? "No configured DataSet • source model"
                    : descriptor.DataSetResolved
                        ? $"Static DataSet • source model{(descriptor.Indexed ? $" • indexed ×{descriptor.InstanceCount}" : string.Empty)}"
                        : "Broken DataSet reference • source model";

        return new RcbExportRow
        {
            SourceSelectionKey = descriptor.SelectionKey,
            ExportName = snapshot?.Name ?? descriptor.Name,
            Name = snapshot?.Name ?? descriptor.Name,
            Reference = snapshot?.Reference ?? descriptor.DisplayReference,
            ScopeText = scope,
            Type = descriptor.Type,
            Buffered = descriptor.Buffered,
            DataSetName = string.IsNullOrWhiteSpace(effectiveDataSetName) ? "—" : effectiveDataSetName,
            DataSetReference = effectiveDataSetReference,
            DataSetDetail = dataSetDetail,
            MemberCount = memberCount,
            Availability = availability,
            Confidence = evidenceConflict ? MmsRcbAvailabilityConfidence.Unknown : snapshot?.Confidence ?? MmsRcbAvailabilityConfidence.Unknown,
            StatusText = evidenceConflict
                ? "Config mismatch"
                : snapshot == null && availability == MmsRcbOperationalAvailability.Unknown
                    ? "Not checked"
                    : RcbExportRow.ToStatusText(availability),
            Reason = reason,
            Owner = snapshot?.Owner ?? string.Empty,
            HasEvidenceConflict = evidenceConflict,
            IsSourceBacked = true,
            IsIndexedSource = descriptor.Indexed
        };
    }

    private static SclReportControlDescriptor? FindSourceDescriptor(
        MmsRcbAvailabilitySnapshot snapshot,
        IReadOnlyList<SclReportControlDescriptor> descriptors)
    {
        var modeMatches = descriptors.Where(descriptor => descriptor.Buffered == snapshot.Buffered).ToArray();
        var exact = modeMatches.Where(descriptor =>
                descriptor.Name.Equals(snapshot.Name, StringComparison.OrdinalIgnoreCase) &&
                LogicalScopeMatches(descriptor, snapshot))
            .ToArray();
        if (exact.Length == 1) return exact[0];

        var indexed = modeMatches.Where(descriptor =>
                descriptor.Indexed && IsIndexedRuntimeName(descriptor.Name, snapshot.Name) &&
                LogicalScopeMatches(descriptor, snapshot))
            .ToArray();
        if (indexed.Length == 1) return indexed[0];

        var dataSetName = LastReferenceSegment(snapshot.DataSetReference);
        var byDataSet = modeMatches.Where(descriptor =>
                descriptor.DataSetName.Equals(dataSetName, StringComparison.OrdinalIgnoreCase) &&
                LogicalScopeMatches(descriptor, snapshot))
            .ToArray();
        return byDataSet.Length == 1 ? byDataSet[0] : null;
    }

    private static bool LogicalScopeMatches(SclReportControlDescriptor descriptor, MmsRcbAvailabilitySnapshot snapshot)
    {
        var logicalNodeMatches = string.IsNullOrWhiteSpace(snapshot.LogicalNode) ||
                                 descriptor.LogicalNodePath.Equals(snapshot.LogicalNode, StringComparison.OrdinalIgnoreCase);
        var logicalDeviceMatches = string.IsNullOrWhiteSpace(snapshot.Domain) ||
                                   string.IsNullOrWhiteSpace(descriptor.LogicalDeviceInstance) ||
                                   snapshot.Domain.EndsWith(descriptor.LogicalDeviceInstance, StringComparison.OrdinalIgnoreCase);
        return logicalNodeMatches && logicalDeviceMatches;
    }

    private static bool IsIndexedRuntimeName(string sourceName, string runtimeName)
    {
        if (!runtimeName.StartsWith(sourceName, StringComparison.OrdinalIgnoreCase)) return false;
        var suffix = runtimeName[sourceName.Length..];
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }

    private static IReadOnlyList<RcbExportRow> BuildLiveModelRcbRows(
        LiveIedModelDiscoveryDocument? model,
        MmsRcbAvailabilityResult? availability)
    {
        if (model == null) return Array.Empty<RcbExportRow>();

        var snapshots = availability?.ReportControls
            .GroupBy(snapshot => NormalizeRcbReference(snapshot.Reference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, MmsRcbAvailabilitySnapshot>(StringComparer.OrdinalIgnoreCase);
        var dataSets = model.DataSets
            .GroupBy(dataSet => NormalizeRcbReference(dataSet.Reference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rows = new List<RcbExportRow>();

        foreach (var reportControl in model.ReportControls)
        {
            snapshots.TryGetValue(NormalizeRcbReference(reportControl.Reference), out var snapshot);
            var effectiveDataSetReference = RcbExportEvidencePolicy.EffectiveDataSetReference(
                snapshot?.DataSetReference,
                reportControl.DataSetReference);
            dataSets.TryGetValue(NormalizeRcbReference(effectiveDataSetReference), out var dataSet);
            var members = RcbExportEvidencePolicy.EffectiveMemberCount(
                snapshot?.DataSetMemberCount ?? 0,
                dataSet?.MemberCount ?? 0);
            var availabilityState = RcbExportEvidencePolicy.LiveModelAvailability(
                snapshot?.Availability,
                effectiveDataSetReference,
                dataSet != null,
                members);
            var liveOverridesDiscovery = snapshot != null &&
                                         !string.IsNullOrWhiteSpace(snapshot.DataSetReference) &&
                                         !string.IsNullOrWhiteSpace(reportControl.DataSetReference) &&
                                         !NormalizeRcbReference(snapshot.DataSetReference)
                                             .Equals(NormalizeRcbReference(reportControl.DataSetReference), StringComparison.OrdinalIgnoreCase);
            var dataSetName = string.IsNullOrWhiteSpace(effectiveDataSetReference)
                ? "—"
                : string.IsNullOrWhiteSpace(dataSet?.Name)
                    ? LastReferenceSegment(effectiveDataSetReference)
                    : dataSet.Name;
            var dataSetDetail = liveOverridesDiscovery
                ? "Live DatSet verified • overrides stale discovery binding"
                : snapshot?.DataSetDirectorySuccess == true
                    ? "Static DataSet • live directory verified"
                    : snapshot?.Availability == MmsRcbOperationalAvailability.NoDataSet
                        ? "No configured DataSet • live verified"
                        : string.IsNullOrWhiteSpace(effectiveDataSetReference)
                            ? "DataSet binding unresolved"
                            : dataSet == null
                                ? "DataSet directory unresolved"
                                : "Static DataSet • live discovery";

            rows.Add(new RcbExportRow
            {
                ExportName = reportControl.Name,
                Name = reportControl.Name,
                Reference = reportControl.Reference,
                ScopeText = RcbExportEvidencePolicy.ScopeFromReference(reportControl.Reference),
                Type = reportControl.Buffered ? "Buffered" : "Unbuffered",
                Buffered = reportControl.Buffered,
                DataSetName = dataSetName,
                DataSetReference = effectiveDataSetReference,
                DataSetDetail = dataSetDetail,
                MemberCount = members,
                Availability = availabilityState,
                Confidence = snapshot?.Confidence ?? MmsRcbAvailabilityConfidence.Unknown,
                StatusText = snapshot == null && availabilityState == MmsRcbOperationalAvailability.Unknown
                    ? "Not checked"
                    : RcbExportRow.ToStatusText(availabilityState),
                Reason = snapshot?.Reason ?? RcbExportEvidencePolicy.LiveModelReason(
                    effectiveDataSetReference,
                    dataSet != null,
                    members),
                Owner = snapshot?.Owner ?? string.Empty,
                IsSourceBacked = false
            });
        }
        return rows;
    }

    private async Task<RcbExportCompletion> ExportLegacySasRcbAsync(
        Iec61850MonitorDevice device,
        RcbExportRow row,
        SclSchemaProfile schema,
        string outputPath,
        MmsRcbAvailabilityResult? availability,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (row.HasEvidenceConflict)
            throw new InvalidOperationException("Source SCL and live IED DataSet bindings conflict. Resolve the configuration mismatch before exporting this RCB.");
        if (row.MemberCount <= 0)
            throw new InvalidOperationException("The selected RCB has no populated DataSet.");

        if (row.IsSourceBacked && !string.IsNullOrWhiteSpace(device.SclSourcePath) && File.Exists(device.SclSourcePath))
        {
            var result = await Task.Run(() => LegacySasSclExporter.WriteFiles(
                device.SclSourcePath,
                outputPath,
                new LegacySasSclExportOptions
                {
                    IedName = EffectiveSclIedName(device),
                    AccessPointName = device.SclAccessPointName,
                    SchemaProfile = schema,
                    SelectedReportControl = new SclReportControlSelection(row.SourceSelectionKey, row.ExportName),
                    RemoveUnreferencedDataSets = false,
                    ToolId = "ARIEC61850"
                }), cancellationToken);

            AddLog("INFO", "RCB Export",
                $"{device.Name}: legacy SAS CID saved; schema={result.SclSchema}; RCB={result.RetainedReportControlReference}; DataSet={result.RetainedDataSetName}; members={result.RetainedDataSetMemberCount}; removed RCB={result.RemovedReportControlCount}; output={result.OutputPath}");
            SetStatus($"{device.Name}: legacy SAS CID exported with one RCB — {row.Name}.");
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
                Message = $"Export complete: {result.RetainedReportControlReference} is the only RCB in the generated CID."
            };
        }

        var liveModel = device.LiveDiscoveryModel
            ?? throw new InvalidOperationException("A source SCL file or complete live discovery model is required for legacy SAS export.");
        var selectedDataSet = liveModel.DataSets.FirstOrDefault(dataSet =>
            NormalizeRcbReference(dataSet.Reference)
                .Equals(NormalizeRcbReference(row.DataSetReference), StringComparison.OrdinalIgnoreCase));
        var exportModel = liveModel;
        if (selectedDataSet is null || selectedDataSet.Members.Count == 0)
        {
            if (availability is null)
            {
                throw new InvalidOperationException(
                    "The live discovery model does not contain FCDA member references for this DataSet. Click Check Availability, wait for the read-only audit to finish, then export again.");
            }

            exportModel = LiveRcbDataSetEvidenceMerger.MergeSelectedDataSetDirectory(
                liveModel,
                row.Reference,
                availability);
        }

        if (availability != null)
        {
            exportModel = LiveRcbDataSetEvidenceMerger.MergeSelectedReportControlEvidence(
                exportModel,
                row.Reference,
                availability);
        }

        var filteredModel = SclReportControlFilter.FilterLiveModel(exportModel, row.Reference);
        var liveResult = await Task.Run(() => AuthoritativeLiveIedSclExporter.WriteFiles(
            filteredModel,
            outputPath,
            new LiveIedSclExportOptions
            {
                Profile = "full-model",
                SchemaProfile = schema,
                IpAddress = device.IpAddress,
                IedNameOverride = device.Name,
                IncludeLowConfidenceTypes = true,
                IncludeRuntimeStateComment = false
            }), cancellationToken);

        var removedCount = Math.Max(0, liveModel.ReportControls.Count - 1);
        AddLog("INFO", "RCB Export",
            $"{device.Name}: live-model legacy SAS CID saved; schema={liveResult.SclSchema}; RCB={row.Reference}; DataSet={row.DataSetName}; members={row.MemberCount}; removed RCB={removedCount}; output={liveResult.SclPath}");
        SetStatus($"{device.Name}: legacy SAS CID exported with one RCB — {row.Name}.");
        return new RcbExportCompletion
        {
            OutputPath = liveResult.SclPath,
            ReportPath = liveResult.ReportPath,
            SummaryPath = liveResult.SummaryPath,
            SchemaDisplayName = liveResult.SclSchema,
            RetainedReportControl = row.Reference,
            DataSetName = row.DataSetName,
            DataSetMemberCount = row.MemberCount,
            RemovedReportControlCount = removedCount,
            Message = $"Export complete: {row.Reference} is the only RCB in the generated CID."
        };
    }

    private static string EffectiveSclIedName(Iec61850MonitorDevice device)
        => string.IsNullOrWhiteSpace(device.SclIedName) ? device.Name : device.SclIedName;

    private static string LastReferenceSegment(string? reference)
    {
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        var index = text.LastIndexOf('.');
        return index < 0 ? text : text[(index + 1)..];
    }

    private static string NormalizeRcbReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.').ToLowerInvariant();
}
