using System.Windows;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void IedBrowserWriteValues_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        var liveModel = device?.LiveDiscoveryModel;
        if (device is null || liveModel is null || !device.IsConnected || device.IsDemo ||
            device.IsBusy || _iedBrowserReadOperation is not null)
        {
            IedBrowserDetailStatus.Text =
                "Connect a real IED with LIVE discovery before configuration Write.";
            return;
        }
        var page = _iedBrowserNavigation.Current;
        var selectedDo = page?.Kind == "LN" ? _iedBrowserSelectedDoReference : null;
        var plan = IedBrowserWritePlan.Build(liveModel, page, selectedDo);
        if (plan.Count == 0)
        {
            IedBrowserDetailStatus.Text =
                "No supported live CF/DC/SP/SG/SE primitive DA in the selected object. " +
                "Select a specific Data Object (DO) or Data Attribute (DA), not a whole LN.";
            return;
        }
        // The editor retains exact model and association identities. A late
        // reconnect cannot turn its staged values into a write on a new IED.
        var editor = new IedBrowserWriteWindow(_runtime, device, liveModel, plan)
        {
            Owner = this
        };
        editor.ShowDialog();
        IedBrowserDetailStatus.Text =
            "MMS configuration editor closed. Use explicit Read to refresh visible live values.";
    }
}
