using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ArIED61850Tester;

/// <summary>
/// Shutdown durability guard for native FAT. The normal 350 ms autosave debounce keeps
/// multi-row operations cheap, but a user can legitimately close ARSAS inside that small
/// window. Flush the current per-IED evidence synchronously during Closing so the last
/// capture/result is not lost before the application cancellation token is triggered.
/// </summary>
public partial class MainWindow
{
    private bool _nativeFatPersistenceSafetyAttached;

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
        window.Closing += window.NativeFatPersistenceSafety_Closing;
        window.Closed += window.NativeFatPersistenceSafety_Closed;
    }

    private void NativeFatPersistenceSafety_Closing(object? sender, CancelEventArgs e)
    {
        var state = _nativeFatCurrentState;
        if (state == null)
            return;

        _nativeFatSaveTimer?.Stop();
        try
        {
            // Intentionally bypass the UI save gate here. A debounced asynchronous save
            // may currently own that gate and need the dispatcher for its continuation;
            // waiting for the gate synchronously could deadlock Closing. NativeFatStateStore
            // uses unique temp files + atomic replace, so a concurrent same-state flush is
            // safe and whichever write lands last still represents the same UI state.
            _nativeFatStore.SaveAsync(state, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Shutdown must remain possible. The existing in-memory state and any previous
            // valid JSON remain intact; record the failure while diagnostics are available.
            AddLog("WARN", "Native FAT", $"Final FAT persistence flush failed: {ex.Message}");
        }
    }

    private void NativeFatPersistenceSafety_Closed(object? sender, EventArgs e)
    {
        Closing -= NativeFatPersistenceSafety_Closing;
        Closed -= NativeFatPersistenceSafety_Closed;
        _nativeFatPersistenceSafetyAttached = false;
    }
}
