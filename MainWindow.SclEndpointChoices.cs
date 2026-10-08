using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void SclEndpoint_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TextBlock label ||
            label.DataContext is not Iec61850MonitorDevice device ||
            device.SclEndpointCandidates.Count < 2 ||
            string.IsNullOrWhiteSpace(device.SclSourcePath))
        {
            e.Handled = true;
            return;
        }

        var menu = label.ContextMenu ?? new ContextMenu();
        label.ContextMenu = menu;
        menu.Items.Clear();
        foreach (var endpoint in device.SclEndpointCandidates)
        {
            var selected = device.SclAccessPointName.Equals(endpoint.AccessPointName, StringComparison.OrdinalIgnoreCase) &&
                device.IpAddress.Equals(endpoint.IpAddress, StringComparison.OrdinalIgnoreCase) &&
                device.Port == endpoint.Port;
            var item = new MenuItem
            {
                Header = $"AP {endpoint.AccessPointName}  •  {endpoint.IpAddress}:{endpoint.Port}  ({endpoint.SubNetworkName})",
                IsCheckable = true,
                IsChecked = selected,
                IsEnabled = !device.IsConnected && !device.IsMonitoring && !device.IsBusy && !selected,
                ToolTip = selected
                    ? "Current source-declared MMS endpoint."
                    : "Choose this declared SCD AccessPoint. The model is reloaded offline; Play starts a fresh MMS association."
            };
            item.Click += async (_, _) => await SelectSclEndpointAsync(device, endpoint);
            menu.Items.Add(item);
        }
    }

    private async Task SelectSclEndpointAsync(Iec61850MonitorDevice device, SclMmsEndpoint selected)
    {
        if (!Devices.Contains(device) || device.IsConnected || device.IsBusy || device.IsMonitoring)
            return;

        var originalSource = device.SclSourcePath;
        var originalHash = device.SclSourceSha256;
        var originalIed = device.SclIedName;

        try
        {
            var document = await _sclWorkspaceService.OpenAsync(
                originalSource, cancellationToken: _applicationCancellation.Token);

            // Do not silently bind endpoint/model from a file that was edited since import.
            if (device.IsConnected || device.IsBusy || device.IsMonitoring ||
                !Devices.Contains(device) ||
                !device.SclSourcePath.Equals(originalSource, StringComparison.OrdinalIgnoreCase) ||
                !device.SclSourceSha256.Equals(originalHash, StringComparison.OrdinalIgnoreCase) ||
                !document.SourceSha256.Equals(originalHash, StringComparison.OrdinalIgnoreCase) ||
                !device.SclIedName.Equals(originalIed, StringComparison.OrdinalIgnoreCase))
            {
                AddLog("WARN", "SCL", "AccessPoint switch cancelled: IED state or source SHA changed during reload.");
                return;
            }

            var workspace = SclEndpointTopology.FindExactWorkspace(document, selected);
            if (workspace is null || !workspace.IedName.Equals(originalIed, StringComparison.OrdinalIgnoreCase))
            {
                AddLog("WARN", "SCL",
                    $"AccessPoint {selected.AccessPointName} has no unique, offline-browsable model bound to the declared endpoint. Existing IED card preserved.");
                SetStatus("SCL AccessPoint switch blocked: missing or ambiguous model/endpoint binding.");
                return;
            }

            // Switching an offline SCD AP cannot borrow a previous association's live model.
            device.LiveDiscoveryModel = null;
            device.LiveCanonicalModel = null;
            device.SclComparison = null;
            var signals = SclWorkspaceSignalMapper.BuildSignals(workspace);
            ApplySclWorkspaceToDevice(device, document, workspace, signals);
            device.RefreshComputed();

            if (ReferenceEquals(SelectedDevice, device))
            {
                NewDeviceIp = device.IpAddress;
                NewDevicePort = device.Port.ToString(CultureInfo.InvariantCulture);
            }

            AddLog("INFO", "SCL",
                $"Selected exact SCD MMS endpoint {workspace.IedName}/{workspace.AccessPointName} = {device.EndpointText}. Offline-only change; no automatic connect, probe or failover.");
            SetStatus($"SCD AccessPoint {workspace.AccessPointName} selected for {workspace.IedName} • {device.EndpointText}. Press Play to connect.");
            RaiseWorkspaceCounts();
        }
        catch (OperationCanceledException)
        {
            // App is closing or source reload was cancelled; do not mutate the card.
        }
        catch (Exception ex)
        {
            AddLog("ERROR", "SCL", $"AccessPoint endpoint selection failed: {ex.GetType().Name}: {ex.Message}");
            SetStatus("SCD endpoint selection failed; existing card unchanged.");
        }
    }
}
