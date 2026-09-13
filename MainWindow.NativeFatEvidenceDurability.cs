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
    private CancellationTokenSource? _nativeFatEvidenceStoreLoadCts;
    private readonly HashSet<string> _nativeFatEvidenceStoreLoadedSessions =
        new(StringComparer.OrdinalIgnoreCase);
    private long _nativeFatEvidenceStoreLoadGeneration;

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
        MainTabs.SelectionChanged += NativeFatEvidenceDurability_MainTabsSelectionChanged;
        PropertyChanged += NativeFatEvidenceDurability_MainWindowPropertyChanged;
        Closed += NativeFatEvidenceDurability_MainWindowClosed;
        EnsureNativeFatEvidenceDurabilityGridHook();

        // Load the already-selected IED too. This covers SCL/IEDs opened before this
        // ApplicationIdle hook was installed.
        BeginNativeFatEvidenceStoreLoad(SelectedDevice);
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

        // Keep this order-independent from the existing binding handler. The write is
        // idempotent for unchanged V1/V2 values.
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

        var snapshot = NativeFatEvidenceDurabilitySnapshot.Capture(device, cache);
        _nativeFatEvidencePersistenceCoordinator.Queue(snapshot);

        Trace.WriteLine(
            $"[FAT evidence store] queued detached sparse evidence at {reason}; " +
            $"ied={device.Name}; deviceId={device.DeviceId}; evidenceRows={snapshot.EvidenceByRow.Count}.");
    }

    private void BeginNativeFatEvidenceStoreLoad(Iec61850MonitorDevice? device)
    {
        if (device == null || string.IsNullOrWhiteSpace(device.Name))
            return;

        var sessionKey = BuildNativeFatEvidenceStoreSessionKey(device.DeviceId, device.Name);
        if (_nativeFatEvidenceStoreLoadedSessions.Contains(sessionKey))
            return;

        _nativeFatEvidenceStoreLoadCts?.Cancel();
        _nativeFatEvidenceStoreLoadCts?.Dispose();
        _nativeFatEvidenceStoreLoadCts = null;

        _nativeFatEvidenceStore ??= new NativeFatEvidenceStore();
        var generation = Interlocked.Increment(ref _nativeFatEvidenceStoreLoadGeneration);
        var cts = new CancellationTokenSource();
        _nativeFatEvidenceStoreLoadCts = cts;
        _ = LoadNativeFatEvidenceStoreAsync(
            device.DeviceId,
            device.Name,
            sessionKey,
            generation,
            cts.Token);
    }

    private async Task LoadNativeFatEvidenceStoreAsync(
        string runtimeDeviceId,
        string iedName,
        string sessionKey,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            _nativeFatEvidenceStore ??= new NativeFatEvidenceStore();
            var result = await _nativeFatEvidenceStore.LoadAsync(iedName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (generation != _nativeFatEvidenceStoreLoadGeneration)
                return;

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
        _nativeFatEvidenceStoreLoadCts?.Cancel();
        _nativeFatEvidenceStoreLoadCts?.Dispose();
        _nativeFatEvidenceStoreLoadCts = null;

        try
        {
            _nativeFatEvidencePersistenceCoordinator?.DrainAllAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Trace.WriteLine($"[FAT evidence store] final drain failed: {ex.Message}");
        }

        _nativeFatArmCoordinator.EvidenceChanged -= NativeFatEvidenceDurability_EvidenceChanged;
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
