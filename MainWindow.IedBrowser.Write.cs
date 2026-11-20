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
        if (editor.AttemptedLeaves.Count > 0)
            QueueIedBrowserPostWriteRead(device, liveModel, editor.WriteAssociation,
                editor.AttemptedLeaves, editor.WriteStatus);
        if (editor.PotentiallyChangedControlModels.Count > 0 &&
            device.IsConnected && ReferenceEquals(device.LiveDiscoveryModel, liveModel))
        {
            // Once a configuration Write was attempted, the previous live
            // ctlModel evidence is no longer authoritative for Operate.
            // Refuse stale green command buttons until fresh typed inspection.
            foreach (var signal in device.Signals.Where(signal =>
                         editor.PotentiallyChangedControlModels.Contains(signal.ObjectReference)))
            {
                signal.ClearLiveControlAuthority();
                signal.ControlModelText = "Auto-detect";
                signal.ControlInspectionEvidence =
                    "ctlModel Write attempted in Browser; fresh typed inspection required.";
                signal.ClearControlConfirmation();
            }
            device.RefreshCommandSignalProjection();
            ResetControlPreloadAttempts(device);
            _ = PreloadControlModelsAsync();
        }
        if (editor.AttemptedLeaves.Count == 0)
            IedBrowserDetailStatus.Text = "Configuration editor closed; no Write sent.";
    }
}
