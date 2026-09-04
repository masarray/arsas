using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private SclWorkspaceAction? PromptSclWorkspaceQuickStart(
        IReadOnlyCollection<Iec61850MonitorDevice> importedDevices,
        Iec61850MonitorDevice selectedDevice)
    {
        var canMonitor = selectedDevice.Signals.Count > 0;
        var canDownloadComtrade = !string.IsNullOrWhiteSpace(selectedDevice.IpAddress);
        var endpoint = canDownloadComtrade
            ? selectedDevice.EndpointText
            : "No MMS endpoint in SCL";

        var dialog = new SclWorkspaceQuickStartWindow(
            importedDevices.Count,
            selectedDevice.Name,
            endpoint,
            canMonitor,
            canDownloadComtrade)
        {
            Owner = this
        };

        return dialog.ShowDialog() == true
            ? dialog.SelectedAction
            : null;
    }

    private async Task<SclSignalSelectionMode?> RunSclWorkspaceQuickStartAsync(
        SclWorkspaceAction? action,
        IReadOnlyList<Iec61850MonitorDevice> selectableDevices,
        Iec61850MonitorDevice selectedDevice,
        string sourceLabel)
    {
        switch (action)
        {
            case null:
            case SclWorkspaceAction.BrowseOffline:
                AddLog("INFO", "SCL", $"{sourceLabel}: offline Engineering workspace ready; no MMS connection or monitoring session was started.");
                SetStatus($"{sourceLabel} ready offline · choose an IED action when needed.");
                return null;

            case SclWorkspaceAction.MonitorStaticDataSet:
                foreach (var device in selectableDevices)
                    ApplyStaticDataSetSelection(device);

                AddLog("INFO", "SCL", $"Static DataSet selection is shared by Engineering and FAT for {selectableDevices.Count} IED workspace(s).");
                if (selectedDevice.SelectedLiveSignalCount > 0)
                {
                    SelectedDevice = selectedDevice;
                    var connected = selectedDevice.IsConnected ||
                                    await ConnectUsingSavedModelAsync(selectedDevice);
                    if (connected && !selectedDevice.IsMonitoring)
                        await StartDeviceMonitorAsync(selectedDevice);
                }

                return SclSignalSelectionMode.StaticDataSet;

            case SclWorkspaceAction.MonitorSelectedSignals:
                AddLog("INFO", "SCL", "Manual Signal Selection is shared by Engineering and FAT; monitoring starts only because the operator selected this quick-start action.");
                foreach (var device in selectableDevices)
                {
                    await OpenSignalSelectionWizardAsync(
                        device,
                        autoStartAfterSave: ReferenceEquals(device, selectedDevice));
                    MarkSharedSelectionAuthority(device);
                }

                return SclSignalSelectionMode.Manual;

            case SclWorkspaceAction.RcbEngineering:
                OpenRcbEngineeringQuickStart(selectedDevice);
                return null;

            case SclWorkspaceAction.DownloadComtrade:
                OpenComtradeQuickStart(selectedDevice);
                return null;

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown SCL workspace action.");
        }
    }

    private void OpenRcbEngineeringQuickStart(Iec61850MonitorDevice device)
    {
        SelectedDevice = device;
        AddLog("INFO", "SCL/RCB", $"{device.Name}: opening source-backed RCB engineering workspace; live monitoring is not started by this action.");

        // Reuse the existing source-backed RCB workflow without changing its established
        // card-button contract. The handler only consumes Button.Tag and does not initiate
        // monitoring; live availability probing remains optional when already connected.
        var actionSource = new Button { Tag = device };
        IedEditRcb_Click(
            actionSource,
            new RoutedEventArgs(Button.ClickEvent, actionSource));
    }

    private void OpenComtradeQuickStart(Iec61850MonitorDevice device)
    {
        if (string.IsNullOrWhiteSpace(device.IpAddress))
        {
            MessageBox.Show(
                this,
                "This SCL workspace does not contain a usable MMS endpoint for the selected IED. Bind an endpoint first, then open Download COMTRADE again.",
                "Download COMTRADE",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SelectedDevice = device;
        AddLog("INFO", "SCL/File", $"{device.Name}: opening task-scoped fault-record transfer at {device.EndpointText}; Live Monitor remains unchanged.");
        SetStatus($"{device.Name}: opening fault records · file service only, monitoring unchanged.");

        var dialog = new FaultRecordWindow(device.Name, device.IpAddress, device.Port)
        {
            Owner = this,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.ShowDialog();
    }
}
