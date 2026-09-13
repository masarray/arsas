using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Native FAT evidence lifecycle: auto-load by stable IEDName, capture-only Start FAT,
/// detached sparse persistence, and no dependency on Engineering Points during file IO.
/// </summary>
public partial class MainWindow
{
    private bool _nativeFatEvidenceDurabilityInstalled;
    private DataGrid? _nativeFatEvidenceDurabilityGrid;
    private NativeFatEvidenceStore? _nativeFatEvidenceStore;
    private NativeFatEvidencePersistenceCoordinator? _nativeFatEvidencePersistenceCoordinator;
    private readonly Dictionary<string, CancellationTokenSource> _nativeFatEvidenceStoreLoadCtsBySession =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _nativeFatEvidenceStoreLoadedSessions =
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

        window.Dispatcher.BeginInvoke(
            new Action(window.InstallNativeFatEvidenceDurability),
            DispatcherPriority.ApplicationIdle);
    }

    private void InstallNativeFatEvidenceDurability()
    {
        if (_nativeFatEvidenceDurabilityInstalled || !IsLoaded)
            return;

        _nativeFatEvidenceDurabilityInstalled = true;
        _nativeFatEvidenceStore ??= new NativeFatEvidenceStore();
        _nativeFatEvidencePersistenceCoordinator ??=
            new NativeFatEvidencePersistenceCoordinator(_nativeFatEvidenceStore);

        _nativeFatArmCoordinator.EvidenceChanged += NativeFatEvidenceDurability_EvidenceChanged;
        Devices.CollectionChanged += NativeFatEvidenceDurability_DevicesCollectionChanged;
        MainTabs.SelectionChanged += NativeFatEvidenceDurability_MainTabsSelectionChanged;
        PropertyChanged += NativeFatEvidenceDurability_MainWindowPropertyChanged;
        Closed += NativeFatEvidenceDurability_MainWindowClosed;
        EnsureNativeFatEvidenceDurabilityGridHook();

        // Prepare every IED already present in the Engineering workspace. This is intentionally
        // independent of FAT tab activation and Start FAT, and also covers multi-IED SCL files.
        foreach (var device in Devices)
            BeginNativeFatEvidenceStoreLoad(device);
    }

    private void NativeFatEvidenceDurability_DevicesCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null)
            return;

        foreach (var device in e.NewItems.OfType<Iec61850MonitorDevice>())
        {
            var addedDevice = device;
            // Open SCL inserts a new device before ApplySclWorkspaceToDevice assigns the
            // authoritative workspace.IedName. Defer one dispatcher turn so the store lookup
            // never runs against the constructor placeholder name "IED".
            Dispatcher.BeginInvoke(
                new Action(() => BeginNativeFatEvidenceStoreLoad(addedDevice)),
                DispatcherPriority.Background);
        }
    }

    private void NativeFatEvidenceDurability_MainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs))
            return;

        EnsureNativeFatEvidenceDurabilityGridHook();
        BeginNativeFatEvidenceStoreLoad(SelectedDevice);
    }

    private void NativeFatEvidenceDurability_MainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedDevice))
            BeginNativeFatEvidenceStoreLoad(SelectedDevice);
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

        QueueFrozenNativeFatEvidence(
            e.DeviceId,
            "capture",
            e.Point.DeviceName,
            e.Point.IpAddress);
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

        // Keep this order-independent from the existing binding handler. The write is
        // idempotent for unchanged V1/V2 values.
        var cache = GetNativeFatSession(_nativeFatBoundIedKey);
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, evidenceColumn.Field, editor.Text);
        cache.ActiveRowKey = NativeFatCanonicalEvidenceOverlay.BuildRowKey(point);
        QueueFrozenNativeFatEvidence(
            _nativeFatBoundIedKey,
            "operator edit",
            point.DeviceName,
            point.IpAddress);
    }

    private void QueueFrozenNativeFatEvidence(
        string deviceId,
        string reason,
        string? fallbackIedName = null,
        string? fallbackIpAddress = null)
    {
        if (!_nativeFatSessionByIed.TryGetValue(deviceId, out var cache))
            return;

        var device = Devices.FirstOrDefault(candidate =>
            candidate.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        var iedName = device?.Name ?? fallbackIedName ?? string.Empty;
        var ipAddress = device?.IpAddress ?? fallbackIpAddress ?? string.Empty;
        if (string.IsNullOrWhiteSpace(iedName))
            return;

        // Retire the old live-device debounce for this generation. Its SaveAsync walks
        // device.Points later; the new store writes only the already-detached sparse payload.
        if (_nativeFatEvidencePersistCtsByIed.Remove(deviceId, out var pending))
        {
            pending.Cancel();
            pending.Dispose();
        }

        _nativeFatEvidenceStore ??= new NativeFatEvidenceStore();
        _nativeFatEvidencePersistenceCoordinator ??=
            new NativeFatEvidencePersistenceCoordinator(_nativeFatEvidenceStore);

        var snapshot = device != null
            ? NativeFatEvidenceDurabilitySnapshot.Capture(device, cache)
            : NativeFatEvidenceDurabilitySnapshot.Capture(deviceId, iedName, ipAddress, cache);
        _nativeFatEvidencePersistenceCoordinator.Queue(snapshot);

        Trace.WriteLine(
            $"[FAT evidence store] queued detached sparse evidence at {reason}; " +
            $"ied={iedName}; deviceId={deviceId}; evidenceRows={snapshot.EvidenceByRow.Count}; " +
            $"liveDevicePresent={device != null}.");
    }

    private void BeginNativeFatEvidenceStoreLoad(Iec61850MonitorDevice? device)
    {
        if (device == null || string.IsNullOrWhiteSpace(device.Name))
            return;

        var sessionKey = BuildNativeFatEvidenceStoreSessionKey(device.DeviceId, device.Name);
        if (_nativeFatEvidenceStoreLoadedSessions.Contains(sessionKey) ||
            _nativeFatEvidenceStoreLoadCtsBySession.ContainsKey(sessionKey))
        {
            return;
        }

        _nativeFatEvidenceStore ??= new NativeFatEvidenceStore();
        var cts = new CancellationTokenSource();
        _nativeFatEvidenceStoreLoadCtsBySession[sessionKey] = cts;
        _ = LoadNativeFatEvidenceStoreAsync(
            device.DeviceId,
            device.Name,
            sessionKey,
            cts);
    }

    private async Task LoadNativeFatEvidenceStoreAsync(
        string runtimeDeviceId,
        string iedName,
        string sessionKey,
        CancellationTokenSource owner)
    {
        try
        {
            _nativeFatEvidenceStore ??= new NativeFatEvidenceStore();
            var result = await _nativeFatEvidenceStore.LoadAsync(iedName, owner.Token);
            owner.Token.ThrowIfCancellationRequested();

            if (result.Succeeded)
                _nativeFatEvidenceStoreLoadedSessions.Add(sessionKey);

            var cache = GetNativeFatSession(runtimeDeviceId);
            if (result.Succeeded && result.SnapshotFound)
            {
                ReplaceNativeFatEvidenceForIed(cache, iedName, result.EvidenceByRow);
                cache.EvidenceHydrationState = NativeFatEvidenceHydrationState.Resolved;
                cache.EvidenceHydratedAt = DateTimeOffset.Now;
                cache.EvidenceHydrationError = string.Empty;

                if (string.Equals(_nativeFatBoundIedKey, runtimeDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    RefreshAllVisibleNativeFatEvidenceCells();
                    RefreshNativeFatEvidenceBindingRuntime();
                    UpdateNativeFatArmUi(
                        Devices.FirstOrDefault(candidate => candidate.DeviceId.Equals(runtimeDeviceId, StringComparison.OrdinalIgnoreCase)),
                        $"Canonical Engineering live rows · evidence ready · {result.LoadedRows} persisted row(s) auto-loaded by IEDName");
                }

                Trace.WriteLine(
                    $"[FAT evidence store] auto-loaded; ied={iedName}; deviceId={runtimeDeviceId}; " +
                    $"rows={result.LoadedRows}; ignored={result.IgnoredRows}; source={result.SourcePath}; " +
                    $"elapsedMs={result.ElapsedMilliseconds}; StartFATRequired=false.");
            }
            else if (!result.Succeeded)
            {
                Trace.WriteLine(
                    $"[FAT evidence store] auto-load failed for {iedName}: {result.Message}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_nativeFatEvidenceStoreLoadCtsBySession.TryGetValue(sessionKey, out var current) &&
                ReferenceEquals(current, owner))
            {
                _nativeFatEvidenceStoreLoadCtsBySession.Remove(sessionKey);
                owner.Dispose();
            }
        }
    }

    private static string BuildNativeFatEvidenceStoreSessionKey(string deviceId, string iedName)
        => $"{deviceId.Trim().ToLowerInvariant()}|{NativeFatCanonicalEvidenceOverlay.NormalizeIedName(iedName)}";

    private static void ReplaceNativeFatEvidenceForIed(
        NativeFatIedSessionCacheState cache,
        string iedName,
        IReadOnlyDictionary<string, NativeFatEvidenceSlotState> evidenceByRow)
    {
        var ownerPrefix = NativeFatCanonicalEvidenceOverlay.NormalizeIedName(iedName) + "|";
        lock (cache.EvidenceByRow)
        {
            foreach (var key in cache.EvidenceByRow.Keys
                         .Where(key => key.StartsWith(ownerPrefix, StringComparison.OrdinalIgnoreCase))
                         .ToArray())
            {
                cache.EvidenceByRow.Remove(key);
            }

            foreach (var pair in evidenceByRow)
                cache.EvidenceByRow[pair.Key] = pair.Value;
        }
    }

    private void NativeFatEvidenceDurability_MainWindowClosed(object? sender, EventArgs e)
    {
        foreach (var cts in _nativeFatEvidenceStoreLoadCtsBySession.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _nativeFatEvidenceStoreLoadCtsBySession.Clear();

        try
        {
            _nativeFatEvidencePersistenceCoordinator?.DrainAllAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Trace.WriteLine($"[FAT evidence store] final drain failed: {ex.Message}");
        }

        _nativeFatArmCoordinator.EvidenceChanged -= NativeFatEvidenceDurability_EvidenceChanged;
        Devices.CollectionChanged -= NativeFatEvidenceDurability_DevicesCollectionChanged;
        MainTabs.SelectionChanged -= NativeFatEvidenceDurability_MainTabsSelectionChanged;
        PropertyChanged -= NativeFatEvidenceDurability_MainWindowPropertyChanged;
        Closed -= NativeFatEvidenceDurability_MainWindowClosed;
        if (_nativeFatEvidenceDurabilityGrid != null)
            _nativeFatEvidenceDurabilityGrid.CellEditEnding -= NativeFatEvidenceDurability_CellEditEnding;
        _nativeFatEvidenceDurabilityGrid = null;
        _nativeFatEvidenceStoreLoadedSessions.Clear();

        _nativeFatEvidenceStore?.Dispose();
        _nativeFatEvidenceStore = null;
        _nativeFatEvidencePersistenceCoordinator = null;
    }
}
