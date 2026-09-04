using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// Opens the existing source-backed RCB engineering/export workflow for the imported
    /// SCL IED without connecting or starting Live Monitor. Live availability remains an
    /// explicit optional probe inside that workflow when the IED is already connected.
    /// </summary>
    internal void OpenSelectedSclRcbEngineering()
    {
        var device = SelectedDevice;
        if (device == null)
        {
            MessageBox.Show(this, "Select an imported IED first.", "RCB Engineering",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Reuse the established IED-card action so source inspection, export identity and
        // optional live-availability policy keep one authority. This adapter deliberately
        // does not establish a connection or start monitoring.
        var actionSource = new Button { Tag = device };
        IedEditRcb_Click(actionSource, new RoutedEventArgs());
    }

    /// <summary>
    /// Opens the fault-record/COMTRADE workflow with its own bounded file-transfer client.
    /// It may establish the MMS connection required for file services, but it never starts
    /// the Engineering monitoring acquisition pipeline.
    /// </summary>
    internal void OpenSelectedSclComtradeDownload()
    {
        var device = SelectedDevice;
        if (device == null)
        {
            MessageBox.Show(this, "Select an imported IED first.", "Download COMTRADE",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

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

        var window = new FaultRecordWindow(device, _applicationCancellation.Token)
        {
            Owner = this,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.ShowDialog();
    }
}
