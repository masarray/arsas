using System.Windows;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private IntegratedFatWorkspaceControl? _integratedFatWorkspace;
    private IoTestWorkspaceLaunchResult? _integratedFatLaunch;

    private async void OpenIntegratedFatFromCurrentScl_Click(object sender, RoutedEventArgs e)
    {
        if (_integratedFatWorkspace != null)
        {
            MainTabs.SelectedIndex = 6;
            return;
        }

        var sources = CurrentEngineeringSclSourcePaths();
        if (sources.Length == 0)
        {
            SetStatus("Open an SCL in IEC 61850 Explorer before entering FAT.");
            MessageBox.Show(
                this,
                "The integrated FAT tab uses only the SCL currently open in Engineering. Open an SCL first.",
                "No active Engineering SCL",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            MainTabs.SelectedIndex = 0;
            return;
        }

        await OpenSclFatSourcesAsync(sources, selectionMode: null);
    }

    private void ShowIntegratedFatWorkspace(IoTestWorkspaceLaunchResult launch)
    {
        _integratedFatLaunch = launch;
        _activeIoTestSessionController = launch.Session;
        Interlocked.Exchange(ref _ioTestObservationSequence, DateTime.UtcNow.Ticks);
        _runtime.PointUpdated += Runtime_IoTestPointUpdated;

        var engineeringIeds = Devices.Where(device => launch.Project.Ieds.Any(ied =>
            ied.LiveDeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase) ||
            (ied.IedName.Equals(device.Name, StringComparison.OrdinalIgnoreCase) &&
             ied.IpAddress.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase))));

        _integratedFatWorkspace = new IntegratedFatWorkspaceControl(
            launch,
            engineeringIeds,
            selected => SynchronizeIntegratedFatSelection(selected));
        IntegratedFatHost.Children.Clear();
        IntegratedFatHost.Children.Add(_integratedFatWorkspace);
        if (SelectedDevice != null)
        {
            _integratedFatWorkspace.SelectEngineeringIed(
                SelectedDevice.DeviceId,
                SelectedDevice.Name,
                SelectedDevice.IpAddress);
        }

        MainTabs.SelectedIndex = 6;
        SetStatus($"Integrated FAT ready · {launch.Project.Ieds.Count} IED · shared Engineering live process image.");
        UpdateIoFatWorkspaceModeState();
    }

    private void SynchronizeIntegratedFatSelection(Iec61850MonitorDevice? selected)
    {
        if (selected == null) return;
        if (!ReferenceEquals(SelectedDevice, selected))
            SelectedDevice = selected;
    }

    private void DisposeIntegratedFatWorkspace()
    {
        var launch = _integratedFatLaunch;
        if (launch == null) return;

        _runtime.PointUpdated -= Runtime_IoTestPointUpdated;
        if (launch.Session.IsSessionActive)
            launch.Session.Stop("Integrated FAT workspace closed.");
        _integratedFatWorkspace?.Dispose();
        DetachIoFatSelectionBridge(launch.Project);
        launch.Session.Dispose();
        launch.Workspace.Dispose();
        if (ReferenceEquals(_activeIoTestSessionController, launch.Session))
            _activeIoTestSessionController = null;

        _integratedFatWorkspace = null;
        _integratedFatLaunch = null;
        if (IntegratedFatHost != null)
            IntegratedFatHost.Children.Clear();
        if (_pollingIntervalBeforeIoFat.HasValue)
        {
            PollingIntervalMs = _pollingIntervalBeforeIoFat.Value;
            _pollingIntervalBeforeIoFat = null;
        }
    }
}
