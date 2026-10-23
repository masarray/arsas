using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private bool _sclLiveModelAuthorityTrackingAttached;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainWindowAuthorityLoaded));
    }

    private static void MainWindowAuthorityLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.AttachSclLiveModelAuthorityTracking();
    }

    private void AttachSclLiveModelAuthorityTracking()
    {
        if (_sclLiveModelAuthorityTrackingAttached)
            return;

        _sclLiveModelAuthorityTrackingAttached = true;
        Devices.CollectionChanged += Devices_AuthorityCollectionChanged;
        foreach (var device in Devices)
            TrackAuthorityDevice(device);
    }

    private void Devices_AuthorityCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var device in e.OldItems.OfType<Iec61850MonitorDevice>())
                device.PropertyChanged -= AuthorityDevice_PropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (var device in e.NewItems.OfType<Iec61850MonitorDevice>())
                TrackAuthorityDevice(device);
        }
    }

    private void TrackAuthorityDevice(Iec61850MonitorDevice device)
    {
        RegisterAuthorityModels(device);
        device.PropertyChanged -= AuthorityDevice_PropertyChanged;
        device.PropertyChanged += AuthorityDevice_PropertyChanged;
    }

    private static void AuthorityDevice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is Iec61850MonitorDevice device)
            RegisterAuthorityModels(device);
    }

    private static void RegisterAuthorityModels(Iec61850MonitorDevice device)
    {
        var workspace = device.SclWorkspace;
        if (workspace != null)
        {
            SclLiveModelAuthorityRegistry.RegisterDesign(
                workspace.IedName,
                workspace.AccessPointName,
                workspace.DesignModel);
        }

        var live = device.LiveDiscoveryModel;
        if (live == null)
            return;

        RegisterLiveIdentity(device.Name, workspace?.AccessPointName, live);
        RegisterLiveIdentity(live.IedName, workspace?.AccessPointName, live);
        RegisterLiveIdentity(device.Name, device.SclAccessPointName, live);
        RegisterLiveIdentity(live.IedName, device.SclAccessPointName, live);
        RegisterLiveIdentity(device.Name, live.AccessPointName, live);
        RegisterLiveIdentity(live.IedName, live.AccessPointName, live);
    }

    private static void RegisterLiveIdentity(
        string? iedName,
        string? accessPointName,
        AR.Iec61850.Discovery.LiveIedModelDiscoveryDocument model)
    {
        if (string.IsNullOrWhiteSpace(iedName))
            return;

        SclLiveModelAuthorityRegistry.RegisterLive(iedName, accessPointName, model);
    }
}
