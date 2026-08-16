using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Alarm Annunciator controller.
///
/// Trigger authority is the same SOE/Event Log stream shown to the operator. The
/// annunciator never invents a second IEC 61850 acquisition path. A momentary alarm
/// edge therefore remains visible even after the process value has returned to normal.
///
/// Presentation is grouped by IED. Only the selected IED's alarm windows are rendered;
/// the IED rail retains independent alarm/unacknowledged indication for every configured
/// device. This keeps both layout and flash updates bounded for large SAS projects.
/// </summary>
public partial class MainWindow
{
    private readonly ConcurrentQueue<Iec61850EventEntry> _pendingAnnunciatorEvents = new();
    private readonly Dictionary<string, List<string>> _annunciatorConfiguredReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AlarmAnnunciatorItem> _annunciatorByPointKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AlarmAnnunciatorDeviceGroup> _annunciatorDeviceById = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _annunciatorUiTimer;
    private AlarmAnnunciatorDeviceGroup? _selectedAnnunciatorDevice;
    private bool _annunciatorInitialized;
    private bool _annunciatorFlashPhase = true;
    private int _annunciatorUiTicks;

    public BulkObservableCollection<AlarmAnnunciatorItem> AnnunciatorAlarms { get; } = new();
    public BulkObservableCollection<AlarmAnnunciatorDeviceGroup> AnnunciatorDevices { get; } = new();

    public AlarmAnnunciatorDeviceGroup? SelectedAnnunciatorDevice
    {
        get => _selectedAnnunciatorDevice;
        set
        {
            if (ReferenceEquals(_selectedAnnunciatorDevice, value))
                return;
            _selectedAnnunciatorDevice = value;
            if (_selectedAnnunciatorDevice != null)
            {
                _selectedAnnunciatorDevice.SetFlashPhase(_annunciatorFlashPhase);
                foreach (var alarm in _selectedAnnunciatorDevice.Alarms.Where(item => item.IsFlashing))
                    alarm.SetFlashPhase(_annunciatorFlashPhase);
            }
            Raise();
        }
    }

    public int AnnunciatorConfiguredCount => AnnunciatorDevices.Sum(group => group.ConfiguredCount);
    public int AnnunciatorActiveCount => AnnunciatorDevices.Sum(group => group.ActiveCount);
    public int AnnunciatorUnacknowledgedCount => AnnunciatorDevices.Sum(group => group.UnacknowledgedCount);
    public int AnnunciatorDeviceCount => AnnunciatorDevices.Count;
    public bool AnnunciatorHasUnacknowledged => AnnunciatorUnacknowledgedCount > 0;
    public string AnnunciatorSummaryText => $"{AnnunciatorActiveCount} ACTIVE • {AnnunciatorUnacknowledgedCount} UNACK • {AnnunciatorDeviceCount} IED • {AnnunciatorConfiguredCount} WINDOWS";
    public Visibility AnnunciatorEmptyVisibility => AnnunciatorDevices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AnnunciatorContentVisibility => AnnunciatorDevices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public double AnnunciatorBeaconOpacity => AnnunciatorHasUnacknowledged
        ? (_annunciatorFlashPhase ? 1d : 0.18d)
        : AnnunciatorActiveCount > 0 ? 1d : 0.32d;

    [ModuleInitializer]
    internal static void RegisterAlarmAnnunciator()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAlarmAnnunciatorWindowLoaded));
    }

    private static void OnAlarmAnnunciatorWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.InitializeAlarmAnnunciator();
    }

    private void InitializeAlarmAnnunciator()
    {
        if (_annunciatorInitialized)
            return;

        _annunciatorInitialized = true;
        _runtime.EventRaised += AlarmRuntime_EventRaised;
        GlobalPoints.CollectionChanged += AnnunciatorGlobalPoints_CollectionChanged;
        Devices.CollectionChanged += AnnunciatorDevices_CollectionChanged;
        Closed += AlarmAnnunciatorWindow_Closed;

        _annunciatorUiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _annunciatorUiTimer.Tick += AnnunciatorUiTimer_Tick;
        _annunciatorUiTimer.Start();

        SynchronizeAnnunciatorPointSelection();
        RaiseAnnunciatorSummary();
    }

    private void AlarmAnnunciatorWindow_Closed(object? sender, EventArgs e)
    {
        if (!_annunciatorInitialized)
            return;

        _annunciatorInitialized = false;
        _runtime.EventRaised -= AlarmRuntime_EventRaised;
        GlobalPoints.CollectionChanged -= AnnunciatorGlobalPoints_CollectionChanged;
        Devices.CollectionChanged -= AnnunciatorDevices_CollectionChanged;
        Closed -= AlarmAnnunciatorWindow_Closed;
        if (_annunciatorUiTimer != null)
        {
            _annunciatorUiTimer.Stop();
            _annunciatorUiTimer.Tick -= AnnunciatorUiTimer_Tick;
            _annunciatorUiTimer = null;
        }
    }

    private void AlarmRuntime_EventRaised(Iec61850EventEntry entry)
        => _pendingAnnunciatorEvents.Enqueue(entry);

    private void AnnunciatorUiTimer_Tick(object? sender, EventArgs e)
    {
        FlushPendingAnnunciatorEvents();

        _annunciatorUiTicks++;
        if (_annunciatorUiTicks < 5)
            return;

        _annunciatorUiTicks = 0;
        _annunciatorFlashPhase = !_annunciatorFlashPhase;

        // At scale, do not invalidate every alarm window in the project. Only visible
        // flashing windows and the compact IED rail indicators need a 2 Hz update.
        if (SelectedAnnunciatorDevice != null)
        {
            foreach (var alarm in SelectedAnnunciatorDevice.Alarms.Where(item => item.IsFlashing))
                alarm.SetFlashPhase(_annunciatorFlashPhase);
        }
        foreach (var group in AnnunciatorDevices.Where(group => group.HasUnacknowledged))
            group.SetFlashPhase(_annunciatorFlashPhase);

        Raise(nameof(AnnunciatorBeaconOpacity));
    }

    private void FlushPendingAnnunciatorEvents()
    {
        var changed = false;
        var processed = 0;
        while (processed < 1000 && _pendingAnnunciatorEvents.TryDequeue(out var entry))
        {
            processed++;
            if (!IsAnnunciatorConfigured(entry.DeviceId, entry.IecReference))
                continue;

            var item = EnsureAnnunciatorItem(entry);
            if (item.ApplyEvent(entry))
                changed = true;
        }

        if (changed)
            RaiseAnnunciatorSummary();
    }

    private void AnnunciatorGlobalPoints_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => SynchronizeAnnunciatorPointSelection();

    private void AnnunciatorDevices_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ClearAnnunciatorConfiguration();
            return;
        }

        if (e.OldItems == null)
            return;

        foreach (var removed in e.OldItems.OfType<Iec61850MonitorDevice>())
            RemoveAnnunciatorDevice(removed.DeviceId);
    }

    private void SynchronizeAnnunciatorPointSelection()
    {
        foreach (var point in GlobalPoints)
        {
            var configured = point.CanUseAsAnnunciator && IsAnnunciatorConfigured(point.DeviceId, point.IecReference);
            point.IsAnnunciatorSelected = configured;
            if (configured)
                EnsureAnnunciatorItem(point).InitializeFromPoint(point);
        }
        RaiseAnnunciatorSummary();
    }

    private void AnnunciatorSelection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox || checkBox.Tag is not Iec61850MonitorPoint point)
            return;

        var requested = checkBox.IsChecked == true;
        if (requested && !point.CanUseAsAnnunciator)
        {
            checkBox.IsChecked = false;
            point.IsAnnunciatorSelected = false;
            SetStatus($"{point.SignalName}: Alarm Annunciator accepts IEC 61850 ST status points only.");
            return;
        }

        if (!requested && _annunciatorByPointKey.TryGetValue(point.PointKey, out var existing) && !existing.IsNormal)
        {
            var confirm = MessageBox.Show(
                this,
                $"{point.SignalName} still has an active or unacknowledged annunciator occurrence.\n\nRemove it from Alarm Annunciator anyway?",
                "Remove annunciator signal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                checkBox.IsChecked = true;
                point.IsAnnunciatorSelected = true;
                return;
            }
        }

        SetAnnunciatorSelection(point, requested);
    }

    private void SetAnnunciatorSelection(Iec61850MonitorPoint point, bool selected)
    {
        var normalized = NormalizeReference(point.IecReference);
        if (normalized.Length == 0)
            return;

        if (!_annunciatorConfiguredReferences.TryGetValue(point.DeviceId, out var references))
        {
            references = new List<string>();
            _annunciatorConfiguredReferences[point.DeviceId] = references;
        }

        if (selected)
        {
            if (!references.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                references.Add(normalized);
            point.IsAnnunciatorSelected = true;
            EnsureAnnunciatorItem(point).InitializeFromPoint(point);
            SetStatus($"{point.DeviceName} / {point.SignalName}: added to Alarm Annunciator.");
        }
        else
        {
            references.RemoveAll(reference => reference.Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (references.Count == 0)
                _annunciatorConfiguredReferences.Remove(point.DeviceId);
            point.IsAnnunciatorSelected = false;
            RemoveAnnunciatorItem(point.PointKey);
            SetStatus($"{point.DeviceName} / {point.SignalName}: removed from Alarm Annunciator.");
        }

        RaiseAnnunciatorSummary();
    }

    private void AcknowledgeAlarm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not AlarmAnnunciatorItem item)
            return;

        if (!item.Acknowledge(DateTimeOffset.Now))
            return;

        SetStatus($"Alarm acknowledged: {item.DeviceName} / {item.SignalName}.");
        RefreshAnnunciatorDeviceGroup(item.DeviceId);
        RaiseAnnunciatorSummary();
    }

    private void AcknowledgeAllAlarms_Click(object sender, RoutedEventArgs e)
    {
        var group = SelectedAnnunciatorDevice;
        if (group == null)
        {
            SetStatus("Alarm Annunciator: select an IED first.");
            return;
        }

        var acknowledgedAt = DateTimeOffset.Now;
        var count = 0;
        foreach (var item in group.Alarms.Where(item => item.CanAcknowledge).ToArray())
        {
            if (item.Acknowledge(acknowledgedAt))
                count++;
        }

        RefreshAnnunciatorDeviceGroup(group.DeviceId);
        SetStatus(count == 0
            ? $"{group.DeviceName}: no unacknowledged alarms."
            : $"{group.DeviceName}: acknowledged {count} alarm(s).");
        RaiseAnnunciatorSummary();
    }

    private AlarmAnnunciatorItem EnsureAnnunciatorItem(Iec61850MonitorPoint point)
    {
        if (_annunciatorByPointKey.TryGetValue(point.PointKey, out var existing))
            return existing;

        var item = new AlarmAnnunciatorItem
        {
            DeviceId = point.DeviceId,
            PointKey = point.PointKey,
            ConfiguredReference = NormalizeReference(point.IecReference),
            DeviceName = point.DeviceName,
            SignalName = point.SignalName,
            IecReference = point.IecReference,
            IecTelegram = point.IecTelegram,
            IecDataType = point.IecDataType
        };
        AddAnnunciatorItem(item);
        return item;
    }

    private AlarmAnnunciatorItem EnsureAnnunciatorItem(Iec61850EventEntry entry)
    {
        var pointKey = string.IsNullOrWhiteSpace(entry.PointKey)
            ? BuildAnnunciatorPointKey(entry.DeviceId, entry.IecReference)
            : entry.PointKey;
        if (_annunciatorByPointKey.TryGetValue(pointKey, out var existing))
            return existing;

        var item = new AlarmAnnunciatorItem
        {
            DeviceId = entry.DeviceId,
            PointKey = pointKey,
            ConfiguredReference = NormalizeReference(entry.IecReference),
            DeviceName = entry.DeviceName,
            SignalName = entry.SignalName,
            IecReference = entry.IecReference,
            IecTelegram = entry.IecTelegram,
            IecDataType = entry.IecDataType
        };
        AddAnnunciatorItem(item);
        return item;
    }

    private AlarmAnnunciatorItem EnsureAnnunciatorPlaceholder(
        Iec61850MonitorDevice device,
        string reference,
        SignalDefinition? signal)
    {
        var pointKey = BuildAnnunciatorPointKey(device.DeviceId, reference);
        if (_annunciatorByPointKey.TryGetValue(pointKey, out var existing))
            return existing;

        var effectiveReference = signal?.ObjectReference ?? reference;
        var item = new AlarmAnnunciatorItem
        {
            DeviceId = device.DeviceId,
            PointKey = pointKey,
            ConfiguredReference = NormalizeReference(reference),
            DeviceName = device.Name,
            SignalName = signal?.Name ?? BuildAnnunciatorSignalCaption(reference),
            IecReference = effectiveReference,
            IecTelegram = Iec61850MonitorPoint.StripIedNamePrefix(effectiveReference, device.Name),
            IecDataType = signal?.DataType ?? string.Empty
        };
        item.MarkUnavailable(device.IsConnected ? "Waiting for live SOE" : "Offline / saved configuration");
        AddAnnunciatorItem(item);
        return item;
    }

    private void AddAnnunciatorItem(AlarmAnnunciatorItem item)
    {
        _annunciatorByPointKey[item.PointKey] = item;
        item.PropertyChanged += AnnunciatorItem_PropertyChanged;
        AnnunciatorAlarms.Add(item);

        var group = EnsureAnnunciatorDeviceGroup(item.DeviceId, item.DeviceName);
        if (!group.Alarms.Contains(item))
            group.Alarms.Add(item);
        group.Recalculate(_annunciatorFlashPhase);
        RaiseAnnunciatorSummary();
    }

    private AlarmAnnunciatorDeviceGroup EnsureAnnunciatorDeviceGroup(string deviceId, string deviceName)
    {
        if (_annunciatorDeviceById.TryGetValue(deviceId, out var existing))
        {
            existing.DeviceName = deviceName;
            return existing;
        }

        var group = new AlarmAnnunciatorDeviceGroup
        {
            DeviceId = deviceId,
            DeviceName = deviceName
        };
        _annunciatorDeviceById[deviceId] = group;
        AnnunciatorDevices.Add(group);
        SelectedAnnunciatorDevice ??= group;
        return group;
    }

    private void AnnunciatorItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not AlarmAnnunciatorItem item)
            return;

        if (e.PropertyName == nameof(AlarmAnnunciatorItem.DeviceName) &&
            _annunciatorDeviceById.TryGetValue(item.DeviceId, out var namedGroup))
        {
            namedGroup.DeviceName = item.DeviceName;
        }

        if (e.PropertyName == nameof(AlarmAnnunciatorItem.VisualState))
        {
            RefreshAnnunciatorDeviceGroup(item.DeviceId);
            RaiseAnnunciatorSummary();
        }
    }

    private void RefreshAnnunciatorDeviceGroup(string deviceId)
    {
        if (!_annunciatorDeviceById.TryGetValue(deviceId, out var group))
            return;
        group.Recalculate(_annunciatorFlashPhase);
    }

    private void RemoveAnnunciatorItem(string pointKey)
    {
        if (!_annunciatorByPointKey.Remove(pointKey, out var item))
            return;
        item.PropertyChanged -= AnnunciatorItem_PropertyChanged;
        AnnunciatorAlarms.Remove(item);

        if (_annunciatorDeviceById.TryGetValue(item.DeviceId, out var group))
        {
            group.Alarms.Remove(item);
            if (group.Alarms.Count == 0)
            {
                _annunciatorDeviceById.Remove(group.DeviceId);
                AnnunciatorDevices.Remove(group);
                if (ReferenceEquals(SelectedAnnunciatorDevice, group))
                    SelectedAnnunciatorDevice = AnnunciatorDevices.FirstOrDefault();
            }
            else
            {
                group.Recalculate(_annunciatorFlashPhase);
            }
        }
        RaiseAnnunciatorSummary();
    }

    private void RemoveAnnunciatorDevice(string deviceId)
    {
        _annunciatorConfiguredReferences.Remove(deviceId);
        foreach (var item in AnnunciatorAlarms.Where(item => item.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase)).ToArray())
            RemoveAnnunciatorItem(item.PointKey);
        RaiseAnnunciatorSummary();
    }

    private void ClearAnnunciatorConfiguration()
    {
        _annunciatorConfiguredReferences.Clear();
        foreach (var item in AnnunciatorAlarms)
            item.PropertyChanged -= AnnunciatorItem_PropertyChanged;
        AnnunciatorAlarms.Clear();
        _annunciatorByPointKey.Clear();
        AnnunciatorDevices.Clear();
        _annunciatorDeviceById.Clear();
        SelectedAnnunciatorDevice = null;
        while (_pendingAnnunciatorEvents.TryDequeue(out _)) { }
        foreach (var point in GlobalPoints)
            point.IsAnnunciatorSelected = false;
        RaiseAnnunciatorSummary();
    }

    private bool IsAnnunciatorConfigured(string deviceId, string? reference)
    {
        if (!_annunciatorConfiguredReferences.TryGetValue(deviceId, out var references))
            return false;
        var normalized = NormalizeReference(reference);
        return references.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private List<string> GetAnnunciatorReferencesForDevice(Iec61850MonitorDevice device)
    {
        if (!_annunciatorConfiguredReferences.TryGetValue(device.DeviceId, out var references))
            return new List<string>();
        return references.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void RestoreAnnunciatorReferences(Iec61850MonitorDevice device, IEnumerable<string>? references)
    {
        var normalized = (references ?? Array.Empty<string>())
            .Select(NormalizeReference)
            .Where(reference => reference.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            _annunciatorConfiguredReferences.Remove(device.DeviceId);
            return;
        }

        _annunciatorConfiguredReferences[device.DeviceId] = normalized;
        foreach (var reference in normalized)
        {
            var signal = device.Signals.FirstOrDefault(candidate =>
                NormalizeReference(candidate.ObjectReference).Equals(reference, StringComparison.OrdinalIgnoreCase));
            EnsureAnnunciatorPlaceholder(device, reference, signal);
        }
        SynchronizeAnnunciatorPointSelection();
        RaiseAnnunciatorSummary();
    }

    private void RaiseAnnunciatorSummary()
    {
        Raise(nameof(AnnunciatorConfiguredCount));
        Raise(nameof(AnnunciatorActiveCount));
        Raise(nameof(AnnunciatorUnacknowledgedCount));
        Raise(nameof(AnnunciatorDeviceCount));
        Raise(nameof(AnnunciatorHasUnacknowledged));
        Raise(nameof(AnnunciatorSummaryText));
        Raise(nameof(AnnunciatorEmptyVisibility));
        Raise(nameof(AnnunciatorContentVisibility));
        Raise(nameof(AnnunciatorBeaconOpacity));
    }

    private static string BuildAnnunciatorPointKey(string deviceId, string? reference)
        => $"{deviceId}|{NormalizeReference(reference)}";

    private static string BuildAnnunciatorSignalCaption(string reference)
    {
        var normalized = (reference ?? string.Empty).Replace('$', '.');
        var slash = normalized.LastIndexOf('/');
        var tail = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        return string.IsNullOrWhiteSpace(tail) ? "Alarm signal" : tail;
    }
}
