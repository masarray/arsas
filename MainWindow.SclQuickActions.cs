using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    internal void OpenSelectedSclRcbEngineering()
    {
        var device = SelectedDevice;
        if (device == null)
        {
            MessageBox.Show(this, "Select an imported IED first.", "RCB Engineering",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenSclRcbEngineering(device);
    }

    internal void OpenSclRcbEngineering(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        SelectedDevice = device;

        // Reuse the existing source-backed RCB engineering/export workflow, but pass
        // the exact card-scoped IED instead of relying on whichever card happened to
        // be selected when the modal action was invoked.
        var actionSource = new Button { Tag = device };
        IedEditRcb_Click(actionSource, new RoutedEventArgs());
    }

    internal void OpenSelectedSclComtradeDownload()
    {
        var device = SelectedDevice;
        if (device == null)
        {
            MessageBox.Show(this, "Select an imported IED first.", "Download COMTRADE",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenSclComtradeDownload(device);
    }

    internal void OpenSclComtradeDownload(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        SelectedDevice = device;

        if (string.IsNullOrWhiteSpace(device.IpAddress))
        {
            MessageBox.Show(
                this,
                $"{device.Name} has no usable MMS endpoint in the imported SCL. Bind an IP address before downloading fault records.",
                "Download COMTRADE",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var window = new FaultRecordWindow(device.Name, device.IpAddress, device.Port)
        {
            Owner = this,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.ShowDialog();
    }

    /// <summary>
    /// Reopens the same task-first chooser used after Open SCL, but pins every action
    /// to the IED card that invoked it. Cancel/X/Browse Offline are intentionally
    /// side-effect free: no selection, connection, polling, report ownership or FAT
    /// state is changed unless the user explicitly chooses a monitoring task.
    /// </summary>
    internal async Task OpenIedWorkspaceActionsAsync(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.IsBusy)
        {
            SetStatus($"{device.Name}: wait for the current IED task to finish before opening IED Actions.");
            return;
        }

        SelectedDevice = device;
        var dialog = new SclSignalSelectionModeWindow(1, device)
        {
            Owner = this,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        // RCB Engineering and COMTRADE are executed by the dialog against _targetDevice
        // and close with DialogResult=false. Browse Offline / Cancel / X also return false
        // but deliberately execute nothing. Only the two monitoring choices return true.
        if (dialog.ShowDialog() != true)
        {
            device.RefreshComputed();
            RaiseWorkspaceCounts();
            return;
        }

        if (dialog.UseStaticDataSet)
        {
            if (device.IsMonitoring)
                await StopDeviceMonitorAsync(device);

            ApplyStaticDataSetSelection(device);
            if (device.SelectedLiveSignalCount == 0)
            {
                SetStatus($"{device.Name}: Static DataSet selected, but no report-authoritative process leaf is available to monitor.");
                return;
            }

            if (!device.IsConnected)
            {
                var connected = device.HasDiscoveryCache && device.Signals.Count > 0
                    ? await ConnectUsingSavedModelAsync(device)
                    : await ConnectAndConfigureDeviceAsync(device, openWizard: false);
                if (!connected)
                    return;
            }

            if (!device.IsMonitoring)
                await StartDeviceMonitorAsync(device);
            return;
        }

        // Manual is deliberately two-stage when reopened from a card. Keep the existing
        // acquisition mode untouched while the manual wizard is open so cancelling that
        // second dialog cannot silently switch a running Static DataSet session to hybrid.
        var accepted = await OpenSignalSelectionWizardAsync(
            device,
            autoStartAfterSave: false,
            ownerOverride: this);
        if (!accepted)
            return;

        Iec61850MonitoringModeRegistry.UseHybrid(device);
        MarkSharedSelectionAuthority(device);

        if (device.SelectedLiveSignalCount == 0)
        {
            if (device.IsMonitoring)
                await StopDeviceMonitorAsync(device);
            SetStatus($"{device.Name}: Manual selection saved with no live monitor points.");
            return;
        }

        if (device.IsMonitoring)
            await StopDeviceMonitorAsync(device);

        if (!device.IsConnected)
        {
            var connected = device.HasDiscoveryCache && device.Signals.Count > 0
                ? await ConnectUsingSavedModelAsync(device)
                : await ConnectAndConfigureDeviceAsync(device, openWizard: false);
            if (!connected)
                return;
        }

        if (!device.IsMonitoring)
            await StartDeviceMonitorAsync(device);
    }

    internal async Task OpenIedWorkspaceActionsFromCardAsync(object sender)
    {
        if (!TryGetDeviceFromButton(sender, out var device))
            return;

        await OpenIedWorkspaceActionsAsync(device);
    }
}
