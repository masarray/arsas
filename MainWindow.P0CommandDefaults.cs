using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Initializes Engineering/FAT command safety checks from one shared SignalDefinition.
/// Interlock and synchrocheck are true-by-default while ctlModel is still being hydrated;
/// once the live control model resolves, operator changes are never forced back.
/// </summary>
public partial class MainWindow
{
    private readonly HashSet<Iec61850MonitorDevice> _p0CommandDefaultDevices = new();
    private readonly HashSet<SignalDefinition> _p0CommandDefaultsInitialized = new();
    private readonly HashSet<SignalDefinition> _p0CommandDefaultsFinalized = new();
    private bool _p0CommandDefaultsAttached;

    // A static field initializer on a partial WPF Window is not a reliable registration
    // point because the type may be marked beforefieldinit. The physical relay bench showed
    // Sync could remain false even though the compatibility code existed. Register at module
    // load so every MainWindow Loaded event is observed deterministically.
    [ModuleInitializer]
    internal static void RegisterP0CommandDefaultsClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0CommandDefaultsLoaded),
            handledEventsToo: true);
    }

    private static void P0CommandDefaultsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.AttachP0CommandDefaults();
    }

    private void AttachP0CommandDefaults()
    {
        if (_p0CommandDefaultsAttached)
            return;

        _p0CommandDefaultsAttached = true;
        Devices.CollectionChanged += P0CommandDevicesChanged;
        Closed += P0CommandDefaultsClosed;
        foreach (var device in Devices)
            AttachP0CommandDevice(device);
    }

    private void AttachP0CommandDevice(Iec61850MonitorDevice device)
    {
        if (!_p0CommandDefaultDevices.Add(device))
            return;

        device.CommandSignals.CollectionChanged += P0CommandSignalsChanged;
        foreach (var signal in device.CommandSignals)
            InitializeP0CommandDefaults(signal);
    }

    private void P0CommandDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var device in e.NewItems.OfType<Iec61850MonitorDevice>())
                AttachP0CommandDevice(device);
        }

        if (e.OldItems != null)
        {
            foreach (var device in e.OldItems.OfType<Iec61850MonitorDevice>())
                DetachP0CommandDevice(device);
        }
    }

    private void P0CommandSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var signal in e.OldItems.OfType<SignalDefinition>())
                DetachP0CommandSignal(signal);
        }

        if (e.NewItems == null)
            return;

        foreach (var signal in e.NewItems.OfType<SignalDefinition>())
            InitializeP0CommandDefaults(signal);
    }

    private void InitializeP0CommandDefaults(SignalDefinition signal)
    {
        if (!_p0CommandDefaultsInitialized.Add(signal))
            return;

        // These are initial safety defaults, not permanent locks. Both Engineering and FAT
        // bind the same SignalDefinition, so this one initialization is the shared authority.
        signal.ControlInterlockCheck = true;
        signal.ControlSynchroCheck = true;
        signal.PropertyChanged += P0CommandSignal_PropertyChanged;

        // A cached/live ctlModel may already be resolved before the signal enters the
        // collection. Finalize immediately in that case. Otherwise keep the defaults
        // authoritative during hydration and stop enforcing them as soon as ctlModel is
        // proven, so later operator changes remain respected.
        if (signal.ControlModelResolved)
            FinalizeP0CommandDefaults(signal);
    }

    private void P0CommandSignal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SignalDefinition signal || _p0CommandDefaultsFinalized.Contains(signal))
            return;

        if (e.PropertyName == nameof(SignalDefinition.ControlModelResolved) && signal.ControlModelResolved)
        {
            FinalizeP0CommandDefaults(signal);
            return;
        }

        if (e.PropertyName == nameof(SignalDefinition.ControlInterlockCheck) && !signal.ControlInterlockCheck)
            signal.ControlInterlockCheck = true;
        else if (e.PropertyName == nameof(SignalDefinition.ControlSynchroCheck) && !signal.ControlSynchroCheck)
            signal.ControlSynchroCheck = true;
    }

    private void FinalizeP0CommandDefaults(SignalDefinition signal)
    {
        if (!_p0CommandDefaultsInitialized.Contains(signal) || !_p0CommandDefaultsFinalized.Add(signal))
            return;

        // Last write before exposing an operable command. FAT binds to these exact shared
        // properties, so Engineering and FAT start from the same checked state.
        signal.ControlInterlockCheck = true;
        signal.ControlSynchroCheck = true;
        signal.PropertyChanged -= P0CommandSignal_PropertyChanged;
    }

    private void DetachP0CommandSignal(SignalDefinition signal)
    {
        signal.PropertyChanged -= P0CommandSignal_PropertyChanged;
        _p0CommandDefaultsInitialized.Remove(signal);
        _p0CommandDefaultsFinalized.Remove(signal);
    }

    private void DetachP0CommandDevice(Iec61850MonitorDevice device)
    {
        if (!_p0CommandDefaultDevices.Remove(device))
            return;

        device.CommandSignals.CollectionChanged -= P0CommandSignalsChanged;
        foreach (var signal in device.CommandSignals)
            DetachP0CommandSignal(signal);
    }

    private void P0CommandDefaultsClosed(object? sender, EventArgs e)
    {
        Closed -= P0CommandDefaultsClosed;
        Devices.CollectionChanged -= P0CommandDevicesChanged;
        foreach (var device in _p0CommandDefaultDevices.ToArray())
            DetachP0CommandDevice(device);
        _p0CommandDefaultsInitialized.Clear();
        _p0CommandDefaultsFinalized.Clear();
        _p0CommandDefaultsAttached = false;
    }
}
