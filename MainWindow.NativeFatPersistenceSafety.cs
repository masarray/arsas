using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Persistence durability guard for native FAT.
///
/// The normal 350 ms autosave debounce keeps multi-row operations cheap, but two short
/// windows still need explicit protection:
/// 1) the operator switches IED before the debounce fires; and
/// 2) the operator closes ARSAS immediately after a capture/result change.
///
/// Native FAT caches one state object per stable IED. Flush inactive cached states after
/// a SelectedDevice change and flush every cached state synchronously during Closing so
/// evidence from the previously selected relay cannot be stranded in memory.
/// </summary>
public partial class MainWindow
{
    private bool _nativeFatPersistenceSafetyAttached;
    private bool _nativeFatSwitchFlushQueued;

    [ModuleInitializer]
    internal static void RegisterNativeFatPersistenceSafety()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NativeFatPersistenceSafety_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void NativeFatPersistenceSafety_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._nativeFatPersistenceSafetyAttached)
            return;

        window._nativeFatPersistenceSafetyAttached = true;
        window.PropertyChanged += window.NativeFatPersistenceSafety_PropertyChanged;
        window.Closing += window.NativeFatPersistenceSafety_Closing;
        window.Closed += window.NativeFatPersistenceSafety_Closed;
    }

    private void NativeFatPersistenceSafety_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedDevice) || !_nativeFatInstalled || _nativeFatSwitchFlushQueued)
            return;

        // Let the normal SelectedDevice handler finish rebinding the workspace first.
        // The previous state remains in _nativeFatStateCache, so a ContextIdle flush can
        // save it without blocking the IED switch or relying on _nativeFatCurrentState.
        _nativeFatSwitchFlushQueued = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                _nativeFatSwitchFlushQueued = false;
                _ = NativeFatPersistenceSafety_FlushInactiveStatesAsync();
            }));
    }

    private async Task NativeFatPersistenceSafety_FlushInactiveStatesAsync()
    {
        if (_nativeFatStateCache.Count == 0)
            return;

        var current = _nativeFatCurrentState;
        var inactive = _nativeFatStateCache.Values
            .Where(state => !ReferenceEquals(state, current))
            .Distinct()
            .ToArray();

        foreach (var state in inactive)
            await SaveNativeFatStateAsync(state);
    }

    private void NativeFatPersistenceSafety_Closing(object? sender, CancelEventArgs e)
    {
        _nativeFatSaveTimer?.Stop();

        var states = _nativeFatStateCache.Values.Distinct().ToList();
        if (_nativeFatCurrentState != null && !states.Contains(_nativeFatCurrentState))
            states.Add(_nativeFatCurrentState);
        if (states.Count == 0)
            return;

        foreach (var state in states)
        {
            try
            {
                // Intentionally bypass the UI save gate here. A debounced asynchronous
                // save may currently own that gate and need the dispatcher for its
                // continuation; waiting for the gate synchronously could deadlock Closing.
                // NativeFatStateStore uses unique temp files + atomic replace, so a
                // concurrent same-state flush is safe and never truncates the valid file.
                _nativeFatStore.SaveAsync(state, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // Shutdown must remain possible. Any previous valid JSON is preserved;
                // record the specific IED failure while diagnostics are still available.
                AddLog(
                    "WARN",
                    "Native FAT",
                    $"Final FAT persistence flush failed for {state.IedName}: {ex.Message}");
            }
        }
    }

    private void NativeFatPersistenceSafety_Closed(object? sender, EventArgs e)
    {
        PropertyChanged -= NativeFatPersistenceSafety_PropertyChanged;
        Closing -= NativeFatPersistenceSafety_Closing;
        Closed -= NativeFatPersistenceSafety_Closed;
        _nativeFatPersistenceSafetyAttached = false;
        _nativeFatSwitchFlushQueued = false;
    }
}
