using System.Windows;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private static readonly bool EngineeringCommandDefaultsClassHandlerRegistered = RegisterEngineeringCommandDefaultsClassHandler();
    private readonly HashSet<SignalDefinition> _engineeringCommandDefaultsInitialized = new();
    private bool _engineeringCommandDefaultsInstalled;

    private static bool RegisterEngineeringCommandDefaultsClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(EngineeringCommandDefaultsClassLoaded));
        return true;
    }

    private static void EngineeringCommandDefaultsClassLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._engineeringCommandDefaultsInstalled)
            return;

        window._engineeringCommandDefaultsInstalled = true;
        window._uiFlushTimer.Tick += window.EngineeringCommandDefaultsUiFlush_Tick;
        window.Closed += window.EngineeringCommandDefaultsWindow_Closed;
        window.ApplyEngineeringCommandDefaults();
    }

    private void EngineeringCommandDefaultsWindow_Closed(object? sender, EventArgs e)
    {
        Closed -= EngineeringCommandDefaultsWindow_Closed;
        _uiFlushTimer.Tick -= EngineeringCommandDefaultsUiFlush_Tick;
        _engineeringCommandDefaultsInitialized.Clear();
    }

    private void EngineeringCommandDefaultsUiFlush_Tick(object? sender, EventArgs e)
        => ApplyEngineeringCommandDefaults();

    private void ApplyEngineeringCommandDefaults()
    {
        foreach (var device in Devices)
        {
            foreach (var signal in device.CommandSignals)
            {
                // Default once per command signal. If the operator deliberately changes a
                // checkbox afterwards, do not force it back on during the next UI flush.
                if (!_engineeringCommandDefaultsInitialized.Add(signal))
                    continue;

                signal.ControlInterlockCheck = true;
                signal.ControlSynchroCheck = true;
            }
        }
    }
}
