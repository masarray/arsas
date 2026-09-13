using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Release durability guard for native FAT evidence. Capture persistence is frozen at the
/// evidence event/edit boundary, before an Engineering IED can clear/remove its live rows.
/// This intentionally leaves the canonical WPF binding/recycling path unchanged.
/// </summary>
public partial class MainWindow
{
    private bool _nativeFatEvidenceDurabilityInstalled;
    private DataGrid? _nativeFatEvidenceDurabilityGrid;
    private NativeFatEvidencePersistenceCoordinator? _nativeFatEvidencePersistenceCoordinator;
    private readonly Dictionary<string, Iec61850MonitorDevice> _nativeFatFrozenIdentityByRuntimeIed =
        new(StringComparer.OrdinalIgnoreCase);

    [ModuleInitializer]
    internal static void RegisterNativeFatEvidenceDurability()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NativeFatEvidenceDurability_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void NativeFatEvidenceDurability_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._nativeFatEvidenceDurabilityInstalled)
            return;

        // Production FAT builds its canonical grid during Loaded. Hook after that install so
        // the durability handler runs in addition to, not instead of, the proven binding path.
        window.Dispatcher.BeginInvoke(
            new Action(window.InstallNativeFatEvidenceDurability),
            DispatcherPriority.ApplicationIdle);
    }

    private void InstallNativeFatEvidenceDurability()
    {
        if (_nativeFatEvidenceDurabilityInstalled || !IsLoaded)
            return;

        _nativeFatEvidenceDurabilityInstalled = true;
        _nativeFatEvidencePersistenceCoordinator ??=
            new NativeFatEvidencePersistenceCoordinator(_nativeFatEvidenceHydrationService);

        _nativeFatArmCoordinator.EvidenceChanged += NativeFatEvidenceDurability_EvidenceChanged;
        MainTabs.SelectionChanged += NativeFatEvidenceDurability_MainTabsSelectionChanged;
        EnsureNativeFatEvidenceDurabilityGridHook();
    }

    private void NativeFatEvidenceDurability_MainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, MainTabs))
            EnsureNativeFatEvidenceDurabilityGridHook();
    }

    private void EnsureNativeFatEvidenceDurabilityGridHook()
    {
        var grid = _nativeFatCanonicalGrid;
        if (ReferenceEquals(_nativeFatEvidenceDurabilityGrid, grid))
            return;

        if (_nativeFatEvidenceDurabilityGrid != null)
            _nativeFatEvidenceDurabilityGrid.CellEditEnding -= NativeFatEvidenceDurability_CellEditEnding;

        _nativeFatEvidenceDurabilityGrid = grid;
        if (_nativeFatEvidenceDurabilityGrid != null)
            _nativeFatEvidenceDurabilityGrid.CellEditEnding += NativeFatEvidenceDurability_CellEditEnding;
    }

    private void NativeFatEvidenceDurability_EvidenceChanged(
        object? sender,
        NativeFatEvidenceChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                () => NativeFatEvidenceDurability_EvidenceChanged(sender, e),
                DispatcherPriority.DataBind);
            return;
        }

        QueueFrozenNativeFatEvidence(e.DeviceId, "capture");
    }

    private void NativeFatEvidenceDurability_CellEditEnding(
        object? sender,
        DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit ||
            e.Row.Item is not Iec61850MonitorPoint point ||
            e.Column is not NativeFatEvidenceBindingColumn evidenceColumn ||
            e.EditingElement is not TextBox editor ||
            string.IsNullOrWhiteSpace(_nativeFatBoundIedKey))
        {
            return;
        }

        // Make the durability guard order-independent from the existing binding handler.
        // Write is idempotent for an unchanged V1/V2 value, so either handler may run first.
        var cache = GetNativeFatSession(_nativeFatBoundIedKey);
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, evidenceColumn.Field, editor.Text);
        cache.ActiveRowKey = NativeFatCanonicalEvidenceOverlay.BuildRowKey(point);
        QueueFrozenNativeFatEvidence(_nativeFatBoundIedKey, "operator edit");
    }

    private void QueueFrozenNativeFatEvidence(string deviceId, string reason)
    {
        var device = Devices.FirstOrDefault(candidate =>
            candidate.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        if (device == null || !_nativeFatSessionByIed.TryGetValue(deviceId, out var cache))
            return;

        // Cancel the old 350 ms live-device debounce. Its SaveAsync would enumerate
        // device.Points later and can therefore observe an already-cleared IED workspace.
        if (_nativeFatEvidencePersistCtsByIed.Remove(deviceId, out var pending))
        {
            pending.Cancel();
            pending.Dispose();
        }

        if (!_nativeFatFrozenIdentityByRuntimeIed.TryGetValue(deviceId, out var frozenDevice) ||
            frozenDevice.Points.Count != device.Points.Count)
        {
            frozenDevice = NativeFatEvidenceDurabilitySnapshot.FreezeCanonicalIdentity(device);
            _nativeFatFrozenIdentityByRuntimeIed[deviceId] = frozenDevice;
        }

        var frozen = NativeFatEvidenceDurabilitySnapshot.CaptureFrozen(frozenDevice, cache);
        _nativeFatEvidencePersistenceCoordinator ??=
            new NativeFatEvidencePersistenceCoordinator(_nativeFatEvidenceHydrationService);
        _nativeFatEvidencePersistenceCoordinator.Queue(frozen);

        Trace.WriteLine(
            $"[FAT durability] queued frozen evidence at {reason}; ied={device.Name}; deviceId={device.DeviceId}; canonicalRows={frozen.Device.Points.Count}.");
    }
}
