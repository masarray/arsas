using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using AR.Iec61850.Discovery;
using AR.Iec61850.Goose;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly GooseSubscriberRuntime _gooseSubscriberRuntime = new();
    private readonly ConcurrentDictionary<string, GooseSubscriberFrameSnapshot> _pendingGooseFrames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GooseStreamRow> _gooseStreamIndex = new(StringComparer.OrdinalIgnoreCase);
    private GooseBindingCatalog _gooseBindingCatalog = GooseBindingCatalog.Empty;
    private GooseAdapterOption? _selectedGooseAdapter;
    private GooseStreamRow? _selectedGooseStream;
    private bool _isGooseCapturing;
    private bool _gooseActionBusy;
    private string _gooseStatusText = "Select a network adapter, then start the read-only GOOSE subscriber.";
    private string _gooseBindingText = "No GOOSE model evaluated yet.";
    private string _gooseCaptureFilter = GooseSubscriberRuntime.DefaultCaptureFilter;
    private long _gooseCapturedFrames;
    private long _gooseFrames;
    private long _gooseOtherFrames;

    public BulkObservableCollection<GooseAdapterOption> GooseAdapters { get; } = new();
    public BulkObservableCollection<GooseStreamRow> GooseStreams { get; } = new();

    public GooseAdapterOption? SelectedGooseAdapter
    {
        get => _selectedGooseAdapter;
        set
        {
            if (!Set(ref _selectedGooseAdapter, value)) return;
            Raise(nameof(CanStartGooseSubscriber));
            Raise(nameof(SelectedGooseAdapterDetail));
        }
    }

    public GooseStreamRow? SelectedGooseStream
    {
        get => _selectedGooseStream;
        set
        {
            if (!Set(ref _selectedGooseStream, value)) return;
            Raise(nameof(GooseSelectedStreamText));
            Raise(nameof(GooseNoLeafValuesVisibility));
        }
    }

    public bool IsGooseCapturing
    {
        get => _isGooseCapturing;
        private set
        {
            if (!Set(ref _isGooseCapturing, value)) return;
            Raise(nameof(CanStartGooseSubscriber));
            Raise(nameof(CanStopGooseSubscriber));
            Raise(nameof(CanRefreshGooseConfiguration));
            Raise(nameof(GooseCaptureStateText));
        }
    }

    public bool GooseActionBusy
    {
        get => _gooseActionBusy;
        private set
        {
            if (!Set(ref _gooseActionBusy, value)) return;
            Raise(nameof(CanStartGooseSubscriber));
            Raise(nameof(CanStopGooseSubscriber));
            Raise(nameof(CanRefreshGooseConfiguration));
        }
    }

    public string GooseStatusText { get => _gooseStatusText; private set => Set(ref _gooseStatusText, value ?? string.Empty); }
    public string GooseBindingText { get => _gooseBindingText; private set => Set(ref _gooseBindingText, value ?? string.Empty); }
    public string GooseCaptureFilter { get => _gooseCaptureFilter; set => Set(ref _gooseCaptureFilter, value ?? string.Empty); }
    public long GooseCapturedFrames { get => _gooseCapturedFrames; private set { if (Set(ref _gooseCapturedFrames, value)) Raise(nameof(GooseCounterText)); } }
    public long GooseFrames { get => _gooseFrames; private set { if (Set(ref _gooseFrames, value)) Raise(nameof(GooseCounterText)); } }
    public long GooseOtherFrames { get => _gooseOtherFrames; private set { if (Set(ref _gooseOtherFrames, value)) Raise(nameof(GooseCounterText)); } }

    public bool CanStartGooseSubscriber => !IsGooseCapturing && !GooseActionBusy && SelectedGooseAdapter is not null;
    public bool CanStopGooseSubscriber => IsGooseCapturing && !GooseActionBusy;
    public bool CanRefreshGooseConfiguration => !IsGooseCapturing && !GooseActionBusy;
    public string GooseCaptureStateText => IsGooseCapturing ? "LIVE CAPTURE" : "STOPPED";
    public string SelectedGooseAdapterDetail => SelectedGooseAdapter?.DetailText ?? "No adapter selected";
    public string GooseCounterText => $"Captured {GooseCapturedFrames:N0} • GOOSE {GooseFrames:N0} • Other {GooseOtherFrames:N0} • Streams {GooseStreams.Count:N0}";
    public string GooseSelectedStreamText => SelectedGooseStream is null
        ? "Select a detected GOOSE stream to inspect its ordered DataSet leaves."
        : $"{SelectedGooseStream.IdentityText} • {SelectedGooseStream.DataSetReference} • {SelectedGooseStream.Leaves.Count:N0} ordered leaf value(s)";
    public Visibility GooseNoStreamsVisibility => GooseStreams.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GooseNoLeafValuesVisibility => SelectedGooseStream?.Leaves.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

    private void InitializeGooseSubscriber()
    {
        _gooseSubscriberRuntime.FrameReceived += GooseSubscriberRuntime_FrameReceived;
        _gooseSubscriberRuntime.StatusChanged += GooseSubscriberRuntime_StatusChanged;
        RefreshGooseBindingPreview();
        RefreshGooseAdapters();
    }

    private void RefreshGooseAdapters_Click(object sender, RoutedEventArgs e)
        => RefreshGooseAdapters();

    private void RefreshGooseModels_Click(object sender, RoutedEventArgs e)
        => RefreshGooseBindingPreview();

    private async void StartGooseSubscriber_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGooseAdapter is null || GooseActionBusy || IsGooseCapturing)
            return;

        GooseActionBusy = true;
        try
        {
            _gooseBindingCatalog = BuildGooseBindingCatalog();
            GooseBindingText = _gooseBindingCatalog.Summary;
            ResetGooseView(resetCounters: true);
            await _gooseSubscriberRuntime.StartAsync(
                SelectedGooseAdapter.Selector,
                _gooseBindingCatalog.SclDocument,
                GooseCaptureFilter,
                _applicationCancellation.Token);
            IsGooseCapturing = true;
            GooseStatusText = $"Listening on {SelectedGooseAdapter.DisplayText}. Waiting for GOOSE frames…";
            SetStatus("GOOSE Subscriber started in read-only capture mode.");
            AddLog("INFO", "GOOSE", $"Subscriber started on adapter {SelectedGooseAdapter.Index}: {SelectedGooseAdapter.Description}. Binding: {_gooseBindingCatalog.Summary}");
        }
        catch (Exception ex)
        {
            IsGooseCapturing = false;
            GooseStatusText = $"Could not start GOOSE subscriber: {ex.Message}";
            AddLog("ERROR", "GOOSE", GooseStatusText);
            MarkDiagnosticAlert();
        }
        finally
        {
            GooseActionBusy = false;
        }
    }

    private async void StopGooseSubscriber_Click(object sender, RoutedEventArgs e)
        => await StopGooseSubscriberAsync();

    private void ClearGooseSubscriber_Click(object sender, RoutedEventArgs e)
    {
        ResetGooseView(resetCounters: !IsGooseCapturing);
        GooseStatusText = IsGooseCapturing
            ? "Capture continues. Waiting for the next GOOSE frame…"
            : "GOOSE workspace cleared.";
    }

    private void ResetGooseView(bool resetCounters)
    {
        _pendingGooseFrames.Clear();
        _gooseStreamIndex.Clear();
        GooseStreams.Clear();
        SelectedGooseStream = null;
        if (resetCounters)
        {
            GooseCapturedFrames = 0;
            GooseFrames = 0;
            GooseOtherFrames = 0;
        }
        Raise(nameof(GooseNoStreamsVisibility));
        Raise(nameof(GooseNoLeafValuesVisibility));
        Raise(nameof(GooseCounterText));
        Raise(nameof(GooseSelectedStreamText));
    }

    private async Task StopGooseSubscriberAsync()
    {
        if (GooseActionBusy)
            return;

        GooseActionBusy = true;
        try
        {
            await _gooseSubscriberRuntime.StopAsync();
            IsGooseCapturing = false;
            GooseStatusText = "GOOSE subscriber stopped.";
            SetStatus("GOOSE Subscriber stopped.");
        }
        catch (Exception ex)
        {
            GooseStatusText = $"GOOSE subscriber stop reported: {ex.Message}";
            AddLog("WARN", "GOOSE", GooseStatusText);
        }
        finally
        {
            GooseActionBusy = false;
        }
    }

    private void RefreshGooseAdapters()
    {
        if (IsGooseCapturing)
            return;

        try
        {
            var previousName = SelectedGooseAdapter?.Name;
            var adapters = _gooseSubscriberRuntime.ListAdapters();
            GooseAdapters.ReplaceAll(adapters);
            SelectedGooseAdapter = adapters.FirstOrDefault(adapter =>
                adapter.Name.Equals(previousName, StringComparison.OrdinalIgnoreCase)) ?? adapters.FirstOrDefault();
            GooseStatusText = adapters.Count == 0
                ? "No Npcap/WinPcap adapters were found. Install Npcap and restart ArIED."
                : $"{adapters.Count:N0} capture adapter(s) available. Select the approved station/LAN interface.";
        }
        catch (Exception ex)
        {
            GooseAdapters.Clear();
            SelectedGooseAdapter = null;
            GooseStatusText = $"Npcap adapter discovery failed: {ex.Message}";
            AddLog("WARN", "GOOSE", GooseStatusText);
        }
    }

    private void RefreshGooseBindingPreview()
    {
        if (IsGooseCapturing)
            return;

        _gooseBindingCatalog = BuildGooseBindingCatalog();
        GooseBindingText = _gooseBindingCatalog.Summary;
    }

    private void GooseSubscriberRuntime_FrameReceived(GooseSubscriberFrameSnapshot snapshot)
        => _pendingGooseFrames[snapshot.StreamKey] = snapshot;

    private void GooseSubscriberRuntime_StatusChanged(GooseSubscriberStatusSnapshot status)
    {
        if (Dispatcher.HasShutdownStarted)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            IsGooseCapturing = status.IsRunning;
            GooseCapturedFrames = status.CapturedFrames;
            GooseFrames = status.GooseFrames;
            GooseOtherFrames = status.OtherFrames;
            GooseStatusText = status.Message;
            if (status.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                AddLog("ERROR", "GOOSE", status.Message);
                MarkDiagnosticAlert();
            }
        }));
    }

    private void FlushGooseSubscriberUi()
    {
        if (_pendingGooseFrames.IsEmpty)
            return;

        var processed = 0;
        foreach (var pair in _pendingGooseFrames.ToArray())
        {
            if (processed >= 64)
                break;
            if (!_pendingGooseFrames.TryRemove(pair.Key, out var frameSnapshot))
                continue;

            var snapshot = BuildGooseStreamSnapshot(frameSnapshot, _gooseBindingCatalog);
            if (!_gooseStreamIndex.TryGetValue(snapshot.StreamKey, out var row))
            {
                row = new GooseStreamRow { StreamKey = snapshot.StreamKey };
                _gooseStreamIndex[snapshot.StreamKey] = row;
                GooseStreams.Insert(0, row);
                while (GooseStreams.Count > 256)
                {
                    var removed = GooseStreams[^1];
                    GooseStreams.RemoveAt(GooseStreams.Count - 1);
                    _gooseStreamIndex.Remove(removed.StreamKey);
                    if (ReferenceEquals(SelectedGooseStream, removed))
                        SelectedGooseStream = GooseStreams.FirstOrDefault();
                }
                SelectedGooseStream ??= row;
            }

            row.Apply(snapshot);
            processed++;
        }

        Raise(nameof(GooseCounterText));
        Raise(nameof(GooseNoStreamsVisibility));
        Raise(nameof(GooseNoLeafValuesVisibility));
        Raise(nameof(GooseSelectedStreamText));
    }

    private GooseBindingCatalog BuildGooseBindingCatalog()
    {
        var monitorStreams = new List<SclGooseStream>();
        var bindings = new List<GooseStreamBindingDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sclBindingCount = 0;
        var sclDataSetFallbackCount = 0;
        var liveBindingCount = 0;
        var sclMonitorCount = 0;

        foreach (var device in Devices)
        {
            if (device.SclWorkspace is { } workspace)
            {
                var boundDataSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var stream in workspace.GooseStreams)
                {
                    var normalizedDataSet = NormalizeGooseReference(stream.DataSetReference);
                    var key = $"SCL|{stream.Address.AppId}|{NormalizeGooseReference(stream.ControlBlockReference)}|{normalizedDataSet}";
                    if (!seen.Add(key))
                        continue;

                    bindings.Add(new GooseStreamBindingDefinition(
                        device.Name,
                        "SCL",
                        stream.Address.AppId,
                        stream.ControlBlockReference,
                        stream.GoId,
                        stream.DataSetReference,
                        stream.ConfigurationRevision,
                        stream.Entries.OrderBy(entry => entry.Index)
                            .Select((entry, position) => BuildLeafBinding(device, position, entry.SignalReference, entry.Fc, entry.Cdc, entry.BType))
                            .ToArray()));
                    if (!string.IsNullOrWhiteSpace(normalizedDataSet))
                        boundDataSets.Add(normalizedDataSet);
                    sclBindingCount++;

                    if (stream.Address.AppId.HasValue &&
                        stream.Address.DestinationMac.HasValue &&
                        !string.IsNullOrWhiteSpace(stream.DataSetReference) &&
                        stream.Entries.Count > 0)
                    {
                        monitorStreams.Add(stream);
                        sclMonitorCount++;
                    }
                }

                // Keep SCL DataSet order usable even when a station file contains the DataSet
                // but its GSEControl/Communication binding is incomplete or intentionally absent.
                foreach (var dataSet in workspace.DataSets.Where(item => item.Entries.Count > 0))
                {
                    var normalizedDataSet = NormalizeGooseReference(dataSet.Reference);
                    if (string.IsNullOrWhiteSpace(normalizedDataSet) || boundDataSets.Contains(normalizedDataSet))
                        continue;

                    var key = $"SCL-DATASET|{device.DeviceId}|{normalizedDataSet}";
                    if (!seen.Add(key))
                        continue;

                    bindings.Add(new GooseStreamBindingDefinition(
                        device.Name,
                        "SCL",
                        null,
                        string.Empty,
                        string.Empty,
                        dataSet.Reference,
                        null,
                        dataSet.Entries.OrderBy(entry => entry.Index)
                            .Select((entry, position) => BuildLeafBinding(device, position, entry.SignalReference, entry.Fc, entry.Cdc, entry.BType))
                            .ToArray()));
                    sclDataSetFallbackCount++;
                }
            }

            if (device.LiveDiscoveryModel is { } liveModel)
            {
                var boundDataSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var control in liveModel.GooseControlBlocks)
                {
                    var dataSet = FindLiveDataSet(liveModel, control.DataSetReference);
                    if (dataSet is null)
                        continue;

                    var normalizedDataSet = NormalizeGooseReference(dataSet.Reference);
                    var key = $"LIVE|{device.DeviceId}|{NormalizeGooseReference(control.Reference)}|{normalizedDataSet}";
                    if (!seen.Add(key))
                        continue;

                    var leaves = dataSet.Members.OrderBy(member => member.Index)
                        .Select((member, position) => BuildLiveLeafBinding(device, liveModel, member, position))
                        .ToArray();
                    bindings.Add(new GooseStreamBindingDefinition(
                        device.Name,
                        "Live discovery",
                        TryParseAppId(control.AppId),
                        control.Reference,
                        control.ControlId,
                        dataSet.Reference,
                        TryParseUInt(control.ConfRev),
                        leaves));
                    boundDataSets.Add(normalizedDataSet);
                    liveBindingCount++;
                }

                // A live MMS discovery can enumerate DataSet members even when the GSEControl
                // DatSet/APPID values are not readable. The frame itself carries datSet, so every
                // discovered DataSet remains a valid ordered-leaf binding candidate.
                foreach (var dataSet in liveModel.DataSets.Where(item => item.Members.Count > 0))
                {
                    var normalizedDataSet = NormalizeGooseReference(dataSet.Reference);
                    if (string.IsNullOrWhiteSpace(normalizedDataSet) || boundDataSets.Contains(normalizedDataSet))
                        continue;

                    var key = $"LIVE-DATASET|{device.DeviceId}|{normalizedDataSet}";
                    if (!seen.Add(key))
                        continue;

                    bindings.Add(new GooseStreamBindingDefinition(
                        device.Name,
                        "Live discovery",
                        null,
                        string.Empty,
                        string.Empty,
                        dataSet.Reference,
                        null,
                        dataSet.Members.OrderBy(member => member.Index)
                            .Select((member, position) => BuildLiveLeafBinding(device, liveModel, member, position))
                            .ToArray()));
                    liveBindingCount++;
                }
            }
        }

        var document = monitorStreams.Count == 0
            ? null
            : new SclDocument
            {
                SourceName = "ArIED loaded SCL workspaces",
                GooseStreams = monitorStreams
                    .GroupBy(stream => $"{stream.Address.AppId}|{NormalizeGooseReference(stream.ControlBlockReference)}|{NormalizeGooseReference(stream.DataSetReference)}", StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToArray()
            };

        var leafCount = bindings.Sum(binding => binding.Leaves.Count);
        var summary = bindings.Count == 0
            ? "No SCL GSEControl or live GOOSE/DataSet model is available. Frames remain visible by allData position, but signal names are unbound."
            : $"Model binding ready • {sclBindingCount:N0} SCL GOOSE stream(s) ({sclMonitorCount:N0} fully address-bound) • {sclDataSetFallbackCount:N0} SCL DataSet fallback(s) • {liveBindingCount:N0} discovery binding(s) • {leafCount:N0} ordered DataSet leaf definition(s)";
        return new GooseBindingCatalog(document, bindings, summary);
    }

    private static GooseLeafBindingDefinition BuildLeafBinding(
        Iec61850MonitorDevice device,
        int index,
        string reference,
        string fc,
        string cdc,
        string bType)
    {
        var signal = FindSignal(device, reference);
        return new GooseLeafBindingDefinition(
            index,
            signal?.Name ?? BuildSignalName(reference, index),
            reference,
            string.IsNullOrWhiteSpace(fc) ? signal?.FunctionalConstraint ?? string.Empty : fc,
            cdc,
            string.IsNullOrWhiteSpace(bType) ? signal?.DataType ?? string.Empty : bType);
    }

    private static GooseLeafBindingDefinition BuildLiveLeafBinding(
        Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument model,
        LiveIedDataSetMemberModel member,
        int index)
    {
        var signal = FindSignal(device, member.Reference);
        var attribute = FindLiveAttribute(model, member.Reference, member.MmsReference);
        var dataObject = FindLiveDataObject(model, member.Reference);
        return new GooseLeafBindingDefinition(
            index,
            signal?.Name ?? BuildSignalName(member.Reference, index),
            member.Reference,
            string.IsNullOrWhiteSpace(member.FunctionalConstraint) ? signal?.FunctionalConstraint ?? string.Empty : member.FunctionalConstraint,
            dataObject?.InferredCdc ?? signal?.ControlCdc ?? string.Empty,
            signal?.DataType ?? attribute?.SclBType ?? attribute?.MmsType ?? string.Empty);
    }

    private static SignalDefinition? FindSignal(Iec61850MonitorDevice device, string reference)
    {
        var normalized = NormalizeGooseReference(reference);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return device.Signals.FirstOrDefault(signal =>
            ReferencesMatch(NormalizeGooseReference(signal.ObjectReference), normalized) ||
            ReferencesMatch(NormalizeGooseReference(signal.DisplayReference), normalized));
    }

    private static LiveIedDataSetModel? FindLiveDataSet(LiveIedModelDiscoveryDocument model, string reference)
    {
        var normalized = NormalizeGooseReference(reference);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return model.DataSets.FirstOrDefault(dataSet =>
            ReferencesMatch(NormalizeGooseReference(dataSet.Reference), normalized) ||
            ReferencesMatch(NormalizeGooseReference(dataSet.Name), normalized));
    }

    private static LiveIedDataAttributeModel? FindLiveAttribute(
        LiveIedModelDiscoveryDocument model,
        string reference,
        string mmsReference)
    {
        var normalized = NormalizeGooseReference(reference);
        var normalizedMms = NormalizeGooseReference(mmsReference);
        if (string.IsNullOrWhiteSpace(normalized) && string.IsNullOrWhiteSpace(normalizedMms))
            return null;

        return model.LogicalDevices
            .SelectMany(device => device.LogicalNodes)
            .SelectMany(node => node.DataObjects)
            .SelectMany(dataObject => dataObject.Attributes)
            .FirstOrDefault(attribute =>
                ReferencesMatch(NormalizeGooseReference(attribute.ObjectReference), normalized) ||
                ReferencesMatch(NormalizeGooseReference(attribute.MmsReference), normalizedMms));
    }

    private static LiveIedDataObjectModel? FindLiveDataObject(LiveIedModelDiscoveryDocument model, string reference)
    {
        var normalized = NormalizeGooseReference(reference);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return model.LogicalDevices
            .SelectMany(device => device.LogicalNodes)
            .SelectMany(node => node.DataObjects)
            .FirstOrDefault(dataObject =>
            {
                var objectReference = NormalizeGooseReference(dataObject.Reference);
                return !string.IsNullOrWhiteSpace(objectReference) &&
                       normalized.StartsWith(objectReference, StringComparison.OrdinalIgnoreCase);
            });
    }

    private static GooseStreamSnapshot BuildGooseStreamSnapshot(
        GooseSubscriberFrameSnapshot captured,
        GooseBindingCatalog catalog)
    {
        var frame = captured.Frame;
        var streamEvent = captured.StreamEvent;
        var binding = catalog.Resolve(frame);
        var rawValueCount = frame.Pdu.Values.Count;
        var modelLeafCount = binding?.Leaves.Count ?? 0;
        var displayedLeafCount = Math.Max(rawValueCount, modelLeafCount);
        var leaves = new List<GooseLeafValueSnapshot>(displayedLeafCount);

        for (var index = 0; index < displayedLeafCount; index++)
        {
            var decoded = index < streamEvent.GooseValues.Count ? streamEvent.GooseValues[index] : null;
            var definition = binding?.Leaves.FirstOrDefault(leaf => leaf.Index == index) ??
                             (binding is not null && index < binding.Leaves.Count ? binding.Leaves[index] : null);
            var signalReference = decoded?.IsMappedToScl == true
                ? decoded.SignalReference
                : definition?.SignalReference ?? string.Empty;
            var bindingSource = decoded?.IsMappedToScl == true
                ? "SCL"
                : binding?.Source ?? "Unbound";
            var value = index < rawValueCount
                ? decoded?.DisplayValue ?? MmsDataValueRenderer.ToCompactString(frame.Pdu.Values[index], signalReference)
                : "<missing in frame>";

            leaves.Add(new GooseLeafValueSnapshot(
                index + 1,
                index,
                definition?.SignalName ?? BuildSignalName(signalReference, index),
                signalReference,
                decoded?.IsMappedToScl == true ? decoded.Fc : definition?.FunctionalConstraint ?? string.Empty,
                decoded?.IsMappedToScl == true ? decoded.Cdc : definition?.Cdc ?? string.Empty,
                decoded?.IsMappedToScl == true ? decoded.BType : definition?.BType ?? string.Empty,
                value,
                decoded?.PreviousDisplayValue ?? string.Empty,
                decoded?.IsChanged ?? false,
                bindingSource));
        }

        var diagnosticItems = streamEvent.Diagnostics.ToList();
        if (binding is not null)
        {
            if (binding.AppId.HasValue && binding.AppId.Value != frame.AppId)
                diagnosticItems.Add($"GOOSE APPID differs from model. Model=0x{binding.AppId.Value:X4}, frame=0x{frame.AppId:X4}.");
            if (binding.ConfigurationRevision.HasValue && binding.ConfigurationRevision.Value != frame.Pdu.ConfigurationRevision)
                diagnosticItems.Add($"GOOSE confRev differs from model. Model={binding.ConfigurationRevision.Value}, frame={frame.Pdu.ConfigurationRevision}.");
            if (!string.IsNullOrWhiteSpace(binding.GoCbRef) &&
                !ReferencesMatch(NormalizeGooseReference(binding.GoCbRef), NormalizeGooseReference(frame.Pdu.GoCbRef)))
                diagnosticItems.Add($"GOOSE goCBRef differs from selected model. Model={binding.GoCbRef}, frame={frame.Pdu.GoCbRef}.");
            if (!string.IsNullOrWhiteSpace(binding.DataSetReference) &&
                !ReferencesMatch(NormalizeGooseReference(binding.DataSetReference), NormalizeGooseReference(frame.Pdu.DataSetReference)))
                diagnosticItems.Add($"GOOSE DataSet reference differs from selected model. Model={binding.DataSetReference}, frame={frame.Pdu.DataSetReference}.");
            if (modelLeafCount != rawValueCount)
                diagnosticItems.Add($"DataSet leaf count differs from model. Model={modelLeafCount}, frame={rawValueCount}.");
        }
        var diagnostics = diagnosticItems.Count == 0
            ? string.Empty
            : string.Join(" • ", diagnosticItems.Distinct(StringComparer.OrdinalIgnoreCase));
        var vlan = frame.Vlan is { } vlanTag
    ? $"VID {vlanTag.VlanId} / PCP {vlanTag.PriorityCodePoint}"
    : "untagged";

        return new GooseStreamSnapshot(
            captured.StreamKey,
            $"0x{frame.AppId:X4}",
            frame.Pdu.GoCbRef,
            frame.Pdu.GoId,
            frame.Pdu.DataSetReference,
            frame.Source.ToString(),
            frame.Destination.ToString(),
            vlan,
            frame.Pdu.StateNumber.ToString(CultureInfo.InvariantCulture),
            frame.Pdu.SequenceNumber.ToString(CultureInfo.InvariantCulture),
            streamEvent.GooseSequenceStatus.ToString(),
            $"{frame.Pdu.TimeAllowedToLiveMilliseconds} ms",
            frame.Pdu.ConfigurationRevision.ToString(CultureInfo.InvariantCulture),
            binding?.ModelIedName ?? string.Empty,
            streamEvent.IsBoundToScl ? "SCL" : binding?.Source ?? "Unbound",
            diagnostics,
            captured.CaptureTimestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            captured.PacketCount,
            streamEvent.ChangedValueCount,
            frame.Pdu.Test,
            frame.Pdu.NeedsCommissioning,
            leaves);
    }

    private static string BuildSignalName(string reference, int index)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return $"Leaf {index + 1}";

        var clean = Regex.Replace(reference, @"\[[^\]]+\]$", string.Empty);
        var slash = clean.LastIndexOf('/');
        if (slash >= 0 && slash < clean.Length - 1)
            clean = clean[(slash + 1)..];
        return clean.Replace('$', '.');
    }

    private static bool ReferencesMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        return left.Equals(right, StringComparison.OrdinalIgnoreCase) ||
               left.EndsWith(right, StringComparison.OrdinalIgnoreCase) ||
               right.EndsWith(left, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGooseReference(string? value)
    {
        var normalized = Regex.Replace(value?.Trim() ?? string.Empty, @"\[[^\]]+\]$", string.Empty);
        return normalized.Replace('$', '.').Replace("..", ".").ToLowerInvariant();
    }

    private static ushort? TryParseAppId(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ushort.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var prefixedHex) ? prefixedHex : null;

        if (text.Any(character => character is (>= 'A' and <= 'F') or (>= 'a' and <= 'f')))
            return ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;

        if (ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec))
            return dec;
        return ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fallbackHex) ? fallbackHex : null;
    }

    private static uint? TryParseUInt(string? value)
        => uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private sealed class GooseBindingCatalog
    {
        public static GooseBindingCatalog Empty { get; } = new(null, Array.Empty<GooseStreamBindingDefinition>(), "No GOOSE model evaluated yet.");

        public GooseBindingCatalog(SclDocument? sclDocument, IReadOnlyList<GooseStreamBindingDefinition> bindings, string summary)
        {
            SclDocument = sclDocument;
            Bindings = bindings;
            Summary = summary;
        }

        public SclDocument? SclDocument { get; }
        public IReadOnlyList<GooseStreamBindingDefinition> Bindings { get; }
        public string Summary { get; }

        public GooseStreamBindingDefinition? Resolve(GooseFrame frame)
        {
            GooseStreamBindingDefinition? best = null;
            var bestScore = 0;
            foreach (var candidate in Bindings)
            {
                var score = 0;
                if (candidate.AppId.HasValue && candidate.AppId.Value == frame.AppId)
                    score += 50;
                if (ReferencesMatch(NormalizeGooseReference(candidate.GoCbRef), NormalizeGooseReference(frame.Pdu.GoCbRef)))
                    score += 120;
                if (ReferencesMatch(NormalizeGooseReference(candidate.DataSetReference), NormalizeGooseReference(frame.Pdu.DataSetReference)))
                    score += 90;
                if (!string.IsNullOrWhiteSpace(candidate.GoId) && candidate.GoId.Equals(frame.Pdu.GoId, StringComparison.OrdinalIgnoreCase))
                    score += 40;
                if (candidate.ConfigurationRevision.HasValue && candidate.ConfigurationRevision.Value == frame.Pdu.ConfigurationRevision)
                    score += 10;
                if (candidate.Source.Equals("SCL", StringComparison.OrdinalIgnoreCase))
                    score += 5;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return bestScore >= 90 ? best : null;
        }
    }
}
