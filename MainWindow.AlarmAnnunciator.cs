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
/// </summary>
public partial class MainWindow
{
    private readonly ConcurrentQueue<Iec61850EventEntry> _pendingAnnunciatorEvents = new();
    private readonly Dictionary<string, List<string>> _annunciatorConfiguredReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AlarmAnnunciatorItem> _annunciatorByPointKey = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _annunciatorUiTimer;
    private bool _annunciatorInitialized;
    private bool _annunciatorFlashPhase = true;
    private int _annunciatorUiTicks;

    public BulkObservableCollection<AlarmAnnunciatorItem> AnnunciatorAlarms { get; } = new();

    public int AnnunciatorConfiguredCount => _annunciatorConfiguredReferences.Values.Sum(items => items.Count);
    public int AnnunciatorActiveCount => AnnunciatorAlarms.Count(item => item.HasLatchedOccurrence && item.CurrentProcessActive);
    public int AnnunciatorUnacknowledgedCount => AnnunciatorAlarms.Count(item => item.CanAcknowledge);
    public bool AnnunciatorHasUnacknowledged => AnnunciatorUnacknowledgedCount > 0;
    public string AnnunciatorSummaryText => $"{AnnunciatorConfiguredCount} configured • {AnnunciatorActiveCount} active • {AnnunciatorUnacknowledgedCount} unacknowledged";
    public Visibility AnnunciatorEmptyVisibility => AnnunciatorAlarms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AnnunciatorContentVisibility => AnnunciatorAlarms.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public double AnnunciatorBeaconOpacity => AnnunciatorHasUnacknowledged
        ? (_annunciatorFlashPhase ? 1d : 0.22d)
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
        foreach (var alarm in AnnunciatorAlarms)
            alarm.SetFlashPhase(_annunciatorFlashPhase);
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
        RaiseAnnunciatorSummary();
    }

    private void AcknowledgeAllAlarms_Click(object sender, RoutedEventArgs e)
    {
        var acknowledgedAt = DateTimeOffset.Now;
        var count = 0;
        foreach (var item in AnnunciatorAlarms.Where(item => item.CanAcknowledge).ToArray())
        {
            if (item.Acknowledge(acknowledgedAt))
                count++;
        }

        SetStatus(count == 0 ? "Alarm Annunciator: no unacknowledged alarms." : $"Alarm Annunciator: acknowledged {count} alarm(s).");
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
        RaiseAnnunciatorSummary();
    }

    private void AnnunciatorItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AlarmAnnunciatorItem.VisualState) or
            nameof(AlarmAnnunciatorItem.CanAcknowledge) or
            nameof(AlarmAnnunciatorItem.CurrentProcessActive) or
            nameof(AlarmAnnunciatorItem.HasLatchedOccurrence))
        {
            RaiseAnnunciatorSummary();
        }
    }

    private void RemoveAnnunciatorItem(string pointKey)
    {
        if (!_annunciatorByPointKey.Remove(pointKey, out var item))
            return;
        item.PropertyChanged -= AnnunciatorItem_PropertyChanged;
        AnnunciatorAlarms.Remove(item);
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
