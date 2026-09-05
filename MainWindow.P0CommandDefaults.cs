using System.Collections.Specialized;
using System.Windows;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private static readonly bool P0CommandDefaultsRegistered = RegisterP0CommandDefaults();
    private readonly HashSet<Iec61850MonitorDevice> _p0CommandDefaultDevices = new();
    private readonly HashSet<SignalDefinition> _p0CommandDefaultsInitialized = new();
    private bool _p0CommandDefaultsAttached;

    private static bool RegisterP0CommandDefaults()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0CommandDefaults_Loaded));
        return true;
    }

    private static void P0CommandDefaults_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.AttachP0CommandDefaults();
    }

    private void AttachP0CommandDefaults()
    {
        if (_p0CommandDefaultsAttached)
            return;

        _p0CommandDefaultsAttached = true;
        Devices.CollectionChanged += P0CommandDefaults_DevicesChanged;
        foreach (var device in Devices)
            TrackP0CommandDefaultsDevice(device);
        Closed += P0CommandDefaults_WindowClosed;
    }

    private void P0CommandDefaults_WindowClosed(object? sender, EventArgs e)
    {
        Closed -= P0CommandDefaults_WindowClosed;
        Devices.CollectionChanged -= P0CommandDefaults_DevicesChanged;
        foreach (var device in _p0CommandDefaultDevices.ToArray())
            UntrackP0CommandDefaultsDevice(device);
        _p0CommandDefaultsInitialized.Clear();
        _p0CommandDefaultsAttached = false;
    }

    private void P0CommandDefaults_DevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var device in e.OldItems.OfType<Iec61850MonitorDevice>())
                UntrackP0CommandDefaultsDevice(device);
        }

        if (e.NewItems != null)
        {
            foreach (var device in e.NewItems.OfType<Iec61850MonitorDevice>())
                TrackP0CommandDefaultsDevice(device);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var device in _p0CommandDefaultDevices.ToArray())
            {
                if (!Devices.Contains(device))
                    UntrackP0CommandDefaultsDevice(device);
            }
            foreach (var device in Devices)
                TrackP0CommandDefaultsDevice(device);
        }
    }

    private void TrackP0CommandDefaultsDevice(Iec61850MonitorDevice device)
    {
        if (!_p0CommandDefaultDevices.Add(device))
        {
            ApplyP0CommandDefaults(device.CommandSignals);
            return;
        }

        device.CommandSignals.CollectionChanged += P0CommandDefaults_CommandSignalsChanged;
        ApplyP0CommandDefaults(device.CommandSignals);
    }

    private void UntrackP0CommandDefaultsDevice(Iec61850MonitorDevice device)
    {
        if (!_p0CommandDefaultDevices.Remove(device))
            return;
        device.CommandSignals.CollectionChanged -= P0CommandDefaults_CommandSignalsChanged;
    }

    private void P0CommandDefaults_CommandSignalsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is IEnumerable<SignalDefinition> signals)
            ApplyP0CommandDefaults(signals);
    }

    private void ApplyP0CommandDefaults(IEnumerable<SignalDefinition> signals)
    {
        foreach (var signal in signals)
        {
            // Initialize once per SignalDefinition object. If the operator later disables
            // Interlock or Synchro, projection refreshes must respect that explicit choice.
            if (!_p0CommandDefaultsInitialized.Add(signal))
                continue;

            signal.ControlInterlockCheck = true;
            signal.ControlSynchroCheck = true;
        }
    }
}
