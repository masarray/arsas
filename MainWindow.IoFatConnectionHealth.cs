using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Keeps FAT IED cards on the same connection/monitoring authority as Engineering.
/// No MMS probe or polling loop is introduced here: existing device lifecycle events are
/// projected immediately into IoTestIedPlan. Last process values are intentionally retained
/// as historical display when the association drops; the card state is the authority that
/// tells the operator those values are no longer live.
/// </summary>
public partial class MainWindow
{
    private static readonly bool P0IoFatConnectionHealthRegistered = RegisterP0IoFatConnectionHealth();
    private readonly HashSet<Iec61850MonitorDevice> _p0IoFatHealthDevices = new();
    private IoListTestingWindow? _p0IoFatHealthWindow;

    private static bool RegisterP0IoFatConnectionHealth()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0IoFatConnectionHealth_Loaded));
        return true;
    }

    private static void P0IoFatConnectionHealth_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is IoListTestingWindow fat && fat.Owner is MainWindow engineering)
            engineering.AttachP0IoFatConnectionHealth(fat);
    }

    private void AttachP0IoFatConnectionHealth(IoListTestingWindow fat)
    {
        if (ReferenceEquals(_p0IoFatHealthWindow, fat))
        {
            SynchronizeP0IoFatConnectionHealth();
            return;
        }

        DetachP0IoFatConnectionHealth();
        _p0IoFatHealthWindow = fat;
        Devices.CollectionChanged += P0IoFatDevices_CollectionChanged;
        foreach (var device in Devices)
            AttachP0IoFatHealthDevice(device);

        fat.Closed += P0IoFatHealthWindow_Closed;
        SynchronizeP0IoFatConnectionHealth();
    }

    private void DetachP0IoFatConnectionHealth()
    {
        Devices.CollectionChanged -= P0IoFatDevices_CollectionChanged;
        foreach (var device in _p0IoFatHealthDevices.ToArray())
            device.PropertyChanged -= P0IoFatDevice_PropertyChanged;
        _p0IoFatHealthDevices.Clear();

        if (_p0IoFatHealthWindow != null)
            _p0IoFatHealthWindow.Closed -= P0IoFatHealthWindow_Closed;
        _p0IoFatHealthWindow = null;
    }

    private void P0IoFatHealthWindow_Closed(object? sender, EventArgs e)
        => DetachP0IoFatConnectionHealth();

    private void P0IoFatDevices_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var device in e.OldItems.OfType<Iec61850MonitorDevice>())
                DetachP0IoFatHealthDevice(device);
        }

        if (e.NewItems != null)
        {
            foreach (var device in e.NewItems.OfType<Iec61850MonitorDevice>())
                AttachP0IoFatHealthDevice(device);
        }

        SynchronizeP0IoFatConnectionHealth();
    }

    private void AttachP0IoFatHealthDevice(Iec61850MonitorDevice device)
    {
        if (!_p0IoFatHealthDevices.Add(device))
            return;
        device.PropertyChanged += P0IoFatDevice_PropertyChanged;
    }

    private void DetachP0IoFatHealthDevice(Iec61850MonitorDevice device)
    {
        if (!_p0IoFatHealthDevices.Remove(device))
            return;
        device.PropertyChanged -= P0IoFatDevice_PropertyChanged;
    }

    private void P0IoFatDevice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Iec61850MonitorDevice device ||
            e.PropertyName is not (nameof(Iec61850MonitorDevice.IsConnected) or
                                   nameof(Iec61850MonitorDevice.IsMonitoring) or
                                   nameof(Iec61850MonitorDevice.Status)))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            try
            {
                Dispatcher.BeginInvoke(new Action(() => SynchronizeP0IoFatConnectionHealth(device)));
            }
            catch (InvalidOperationException)
            {
                // Window teardown only; protocol state remains owned by Engineering.
            }
            return;
        }

        SynchronizeP0IoFatConnectionHealth(device);
    }

    private void SynchronizeP0IoFatConnectionHealth()
    {
        if (_p0IoFatHealthWindow is not { IsLoaded: true } fat)
            return;

        foreach (var device in Devices)
            SynchronizeP0IoFatConnectionHealth(device, fat);

        // Plans whose Engineering device disappeared must not keep a stale LIVE badge.
        foreach (var ied in fat.Project.Ieds)
        {
            if (ResolveP0FatDevice(ied) == null)
                ied.ApplyLiveDeviceBinding(null, "Disconnected · Engineering device unavailable");
        }
    }

    private void SynchronizeP0IoFatConnectionHealth(Iec61850MonitorDevice device)
    {
        if (_p0IoFatHealthWindow is { IsLoaded: true } fat)
            SynchronizeP0IoFatConnectionHealth(device, fat);
    }

    private static string P0IoFatDeviceStatus(Iec61850MonitorDevice device)
    {
        if (!device.IsConnected)
            return string.IsNullOrWhiteSpace(device.Status)
                ? "Disconnected"
                : $"Disconnected · {device.Status}";
        if (!device.IsMonitoring)
            return string.IsNullOrWhiteSpace(device.Status)
                ? "Connected · acquisition stopped"
                : $"Connected · {device.Status}";
        return $"Monitoring · {device.AcquisitionMode}";
    }

    private void SynchronizeP0IoFatConnectionHealth(
        Iec61850MonitorDevice device,
        IoListTestingWindow fat)
    {
        foreach (var ied in fat.Project.Ieds)
        {
            var owner = ResolveP0FatDevice(ied);
            if (!ReferenceEquals(owner, device))
                continue;

            ied.ApplyLiveDeviceBinding(
                device.DeviceId,
                P0IoFatDeviceStatus(device),
                device.IsConnected,
                device.IsMonitoring);
        }
    }
}
