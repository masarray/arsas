using System.Collections.Specialized;
using System.Windows;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Initializes Engineering command safety checks once when a command signal enters the
/// shared runtime model. No timer is used and later operator changes are never forced back.
/// </summary>
public partial class MainWindow
{
    private static readonly bool P0CommandDefaultsClassHandlerRegistered = RegisterP0CommandDefaultsClassHandler();
    private readonly HashSet<Iec61850MonitorDevice> _p0CommandDefaultDevices = new();
    private readonly HashSet<SignalDefinition> _p0CommandDefaultsInitialized = new();
    private bool _p0CommandDefaultsAttached;

    private static bool RegisterP0CommandDefaultsClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0CommandDefaultsLoaded));
        return true;
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
        if (e.NewItems == null)
            return;

        foreach (var signal in e.NewItems.OfType<SignalDefinition>())
            InitializeP0CommandDefaults(signal);
    }

    private void InitializeP0CommandDefaults(SignalDefinition signal)
    {
        if (!_p0CommandDefaultsInitialized.Add(signal))
            return;

        signal.ControlInterlockCheck = true;
        signal.ControlSynchroCheck = true;
    }

    private void DetachP0CommandDevice(Iec61850MonitorDevice device)
    {
        if (!_p0CommandDefaultDevices.Remove(device))
            return;
        device.CommandSignals.CollectionChanged -= P0CommandSignalsChanged;
    }

    private void P0CommandDefaultsClosed(object? sender, EventArgs e)
    {
        Closed -= P0CommandDefaultsClosed;
        Devices.CollectionChanged -= P0CommandDevicesChanged;
        foreach (var device in _p0CommandDefaultDevices.ToArray())
            DetachP0CommandDevice(device);
        _p0CommandDefaultsInitialized.Clear();
        _p0CommandDefaultsAttached = false;
    }
}
