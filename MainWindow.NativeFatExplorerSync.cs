using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Keeps the native FAT projection aligned with the persistent IED Explorer's signal
/// selection without turning FAT into a second signal database.
///
/// ObservableCollection changes were already reconciled by the first P2 slice. Explorer
/// checkbox changes are different: SignalDefinition stays in the collection and only
/// IsSelected changes. This bridge observes that property, marks the FAT scope dirty, and
/// forces non-destructive reconciliation when FAT is visible (or the next time it opens).
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _nativeFatExplorerSyncInstallRetry;
    private DispatcherTimer? _nativeFatExplorerScopeTimer;
    private Iec61850MonitorDevice? _nativeFatExplorerSyncDevice;
    private readonly HashSet<SignalDefinition> _nativeFatExplorerObservedSignals = new();
    private bool _nativeFatExplorerSyncAttached;
    private bool _nativeFatExplorerScopeDirty;

    [ModuleInitializer]
    internal static void RegisterNativeFatExplorerSelectionSync()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NativeFatExplorerSync_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void NativeFatExplorerSync_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._nativeFatExplorerSyncAttached)
            return;

        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(window.TryAttachNativeFatExplorerSelectionSync));
    }

    private void TryAttachNativeFatExplorerSelectionSync()
    {
        if (_nativeFatExplorerSyncAttached || !IsLoaded)
            return;

        if (!_nativeFatInstalled)
        {
            _nativeFatExplorerSyncInstallRetry ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(180)
            };
            _nativeFatExplorerSyncInstallRetry.Tick -= NativeFatExplorerSyncInstallRetry_Tick;
            _nativeFatExplorerSyncInstallRetry.Tick += NativeFatExplorerSyncInstallRetry_Tick;
            _nativeFatExplorerSyncInstallRetry.Start();
            return;
        }

        _nativeFatExplorerSyncInstallRetry?.Stop();
        _nativeFatExplorerSyncAttached = true;

        _nativeFatExplorerScopeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _nativeFatExplorerScopeTimer.Tick += NativeFatExplorerScopeTimer_Tick;

        PropertyChanged += NativeFatExplorerSync_MainWindowPropertyChanged;
        MainTabs.SelectionChanged += NativeFatExplorerSync_MainTabsSelectionChanged;
        Closed += NativeFatExplorerSync_MainWindowClosed;
        RebindNativeFatExplorerSignals(SelectedDevice);
    }

    private void NativeFatExplorerSyncInstallRetry_Tick(object? sender, EventArgs e)
    {
        _nativeFatExplorerSyncInstallRetry?.Stop();
        TryAttachNativeFatExplorerSelectionSync();
    }

    private void NativeFatExplorerSync_MainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedDevice))
            return;

        RebindNativeFatExplorerSignals(SelectedDevice);
        MarkNativeFatExplorerScopeDirty();
    }

    private void RebindNativeFatExplorerSignals(Iec61850MonitorDevice? device)
    {
        if (ReferenceEquals(_nativeFatExplorerSyncDevice, device))
        {
            SyncNativeFatExplorerSignalSubscriptions();
            return;
        }

        if (_nativeFatExplorerSyncDevice != null)
            _nativeFatExplorerSyncDevice.Signals.CollectionChanged -= NativeFatExplorerSignals_CollectionChanged;

        foreach (var signal in _nativeFatExplorerObservedSignals)
            signal.PropertyChanged -= NativeFatExplorerSignal_PropertyChanged;
        _nativeFatExplorerObservedSignals.Clear();

        _nativeFatExplorerSyncDevice = device;
        if (_nativeFatExplorerSyncDevice != null)
        {
            _nativeFatExplorerSyncDevice.Signals.CollectionChanged += NativeFatExplorerSignals_CollectionChanged;
            SyncNativeFatExplorerSignalSubscriptions();
        }
    }

    private void NativeFatExplorerSignals_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncNativeFatExplorerSignalSubscriptions();
        MarkNativeFatExplorerScopeDirty();
    }

    private void SyncNativeFatExplorerSignalSubscriptions()
    {
        var current = _nativeFatExplorerSyncDevice?.Signals.ToHashSet() ?? new HashSet<SignalDefinition>();

        foreach (var signal in _nativeFatExplorerObservedSignals.Where(signal => !current.Contains(signal)).ToArray())
        {
            signal.PropertyChanged -= NativeFatExplorerSignal_PropertyChanged;
            _nativeFatExplorerObservedSignals.Remove(signal);
        }

        foreach (var signal in current)
        {
            if (!_nativeFatExplorerObservedSignals.Add(signal))
                continue;
            signal.PropertyChanged += NativeFatExplorerSignal_PropertyChanged;
        }
    }

    private void NativeFatExplorerSignal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // IsSelected is the Explorer membership authority. Value/quality/timestamp changes
        // stay live-bound directly and must never trigger a full FAT reconciliation.
        if (e.PropertyName == nameof(SignalDefinition.IsSelected))
            MarkNativeFatExplorerScopeDirty();
    }

    private void MarkNativeFatExplorerScopeDirty()
    {
        _nativeFatExplorerScopeDirty = true;
        if (MainTabs.SelectedIndex != NativeFatWorkspaceIndex)
            return;

        _nativeFatExplorerScopeTimer?.Stop();
        _nativeFatExplorerScopeTimer?.Start();
    }

    private async void NativeFatExplorerScopeTimer_Tick(object? sender, EventArgs e)
    {
        _nativeFatExplorerScopeTimer?.Stop();
        if (!_nativeFatExplorerScopeDirty || MainTabs.SelectedIndex != NativeFatWorkspaceIndex)
            return;

        await EnsureNativeFatLoadedAsync(forceReconcile: true);
        _nativeFatExplorerScopeDirty = false;
    }

    private void NativeFatExplorerSync_MainTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs) ||
            MainTabs.SelectedIndex != NativeFatWorkspaceIndex ||
            !_nativeFatExplorerScopeDirty)
        {
            return;
        }

        // The base P2 selection handler may have issued a fast non-forced load already.
        // Re-run at ContextIdle with forceReconcile so checkbox changes made while another
        // workspace was active are never hidden behind the current-state fast path.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(async () =>
            {
                if (MainTabs.SelectedIndex != NativeFatWorkspaceIndex || !_nativeFatExplorerScopeDirty)
                    return;
                await EnsureNativeFatLoadedAsync(forceReconcile: true);
                _nativeFatExplorerScopeDirty = false;
            }));
    }

    private void NativeFatExplorerSync_MainWindowClosed(object? sender, EventArgs e)
    {
        _nativeFatExplorerSyncInstallRetry?.Stop();
        _nativeFatExplorerScopeTimer?.Stop();

        PropertyChanged -= NativeFatExplorerSync_MainWindowPropertyChanged;
        MainTabs.SelectionChanged -= NativeFatExplorerSync_MainTabsSelectionChanged;
        Closed -= NativeFatExplorerSync_MainWindowClosed;

        if (_nativeFatExplorerSyncDevice != null)
            _nativeFatExplorerSyncDevice.Signals.CollectionChanged -= NativeFatExplorerSignals_CollectionChanged;
        foreach (var signal in _nativeFatExplorerObservedSignals)
            signal.PropertyChanged -= NativeFatExplorerSignal_PropertyChanged;

        _nativeFatExplorerObservedSignals.Clear();
        _nativeFatExplorerSyncDevice = null;
        _nativeFatExplorerSyncAttached = false;
        _nativeFatExplorerScopeDirty = false;
    }
}
