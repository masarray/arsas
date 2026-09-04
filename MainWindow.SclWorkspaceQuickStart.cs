using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void OpenRcbEngineeringQuickStart(Iec61850MonitorDevice device)
    {
        SelectedDevice = device;
        AddLog("INFO", "SCL/RCB", $"{device.Name}: opening source-backed RCB engineering workspace; live monitoring is not started by this action.");

        // Reuse the established source-backed RCB workflow without changing its card-button
        // contract. The handler consumes Button.Tag only; it never starts Live Monitor, and
        // live availability probing remains optional when the device is already connected.
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

        // FaultRecordWindow owns an isolated file-transfer client. Opening it from SCL does
        // not connect the Engineering monitoring runtime or subscribe any report/polling path.
        var dialog = new FaultRecordWindow(device.Name, device.IpAddress, device.Port)
        {
            Owner = this,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.ShowDialog();
    }
}
