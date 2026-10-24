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
    /// Reopens the same typed task-first chooser used after Open SCL, scoped to
    /// this IED. Browse navigates without changing reporting/selection authority;
    /// cancel/X remain side-effect free. Only explicit monitoring tasks can
    /// mutate acquisition state.
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

        // Engineering/file tasks close the chooser themselves with false; close/X
        // must not act as a monitoring choice or change saved signal authority.
        if (dialog.ShowDialog() != true)
        {
            device.RefreshComputed();
            RaiseWorkspaceCounts();
            return;
        }

        if (dialog.Selection == IedWorkspaceTaskChoice.BrowseIed)
        {
            OpenIedBrowserModel(device);
            device.RefreshComputed();
            RaiseWorkspaceCounts();
            return; // no RCB, GI, polling, manual wizard or selection mutation
        }

        if (dialog.Selection == IedWorkspaceTaskChoice.StaticDataSet)
        {
            device.BeginReportingProgress("Preparing Static DataSet reporting…");
            try
            {
                if (device.IsMonitoring)
                    await StopDeviceMonitorAsync(device);

            await ApplyStaticDataSetSelection(device);
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

            // Unit metadata belongs to the same source-neutral Static DataSet workflow.
            // Resolve only selected measurement owners on the active MMS session, before
            // any RCB is armed, so discovery and opened-SCL paths expose the same verified
            // engineering-unit behavior without disturbing active reporting.
            SetStatus($"{device.Name}: verifying engineering units for selected Static DataSet measurements…");
            await _runtime.EnrichSelectedStaticDataSetUnitsAsync(
                device,
                _applicationCancellation.Token);
            device.RefreshComputed();

            if (!device.IsMonitoring)
                await StartDeviceMonitorAsync(device);
                return;
            }
            finally
            {
                device.CompleteReportingProgress();
            }
        }

        if (dialog.Selection != IedWorkspaceTaskChoice.ManualSignals)
            return; // fail closed on new/unknown action kinds

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
