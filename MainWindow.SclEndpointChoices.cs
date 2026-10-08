using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void SclEndpoint_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TextBlock label ||
            label.DataContext is not Iec61850MonitorDevice device ||
            device.SclAccessPointChoices.Count < 2 ||
            string.IsNullOrWhiteSpace(device.SclSourcePath))
        {
            e.Handled = true;
            return;
        }

        var menu = label.ContextMenu ?? new ContextMenu();
        label.ContextMenu = menu;
        menu.Items.Clear();
        foreach (var choice in device.SclAccessPointChoices)
        {
            var selected = device.SclIedName.Equals(choice.IedName, StringComparison.OrdinalIgnoreCase) &&
                device.SclAccessPointName.Equals(choice.AccessPointName, StringComparison.OrdinalIgnoreCase);
            var address = choice.HasDeclaredAddress
                ? choice.DeclaredEndpoint!.EndpointText + " (SCD-declared)"
                : "No SCD IP • bind per AP";
            var item = new MenuItem
            {
                Header = $"AP {choice.AccessPointName}  •  {address}",
                IsCheckable = true,
                IsChecked = selected,
                IsEnabled = !device.IsConnected && !device.IsMonitoring && !device.IsBusy && !selected,
                ToolTip = selected
                    ? $"Selected AccessPoint ({device.SclEndpointOrigin})."
                    : "Select this offline model. If no SCD IP exists, a verified binding for exactly this AP may be restored; otherwise Play requests an explicit IP."
            };
            item.Click += async (_, _) => await SelectSclAccessPointAsync(device, choice);
            menu.Items.Add(item);
        }
    }

    private async Task SelectSclAccessPointAsync(Iec61850MonitorDevice device, SclAccessPointChoice selected)
    {
        // IsBusy is per-device, so Connect All, Play and a second AP click cannot
        // race with this asynchronous source re-parse. Other IEDs stay independent.
        if (!Devices.Contains(device) || device.IsConnected || device.IsBusy || device.IsMonitoring)
            return;

        var originalSource = device.SclSourcePath;
        var originalHash = device.SclSourceSha256;
        var originalIed = device.SclIedName;
        var originalAp = device.SclAccessPointName;
        var originalIp = device.IpAddress;
        var originalPort = device.Port;

        device.IsBusy = true;
        try
        {
            var document = await _sclWorkspaceService.OpenAsync(
                originalSource, cancellationToken: _applicationCancellation.Token);

            // Re-check the exact in-memory state after await. A selected AP is
            // never applied to an edited SCD, another IED, or an active session.
            if (device.IsConnected || device.IsMonitoring ||
                !Devices.Contains(device) ||
                !device.SclSourcePath.Equals(originalSource, StringComparison.OrdinalIgnoreCase) ||
                !device.SclSourceSha256.Equals(originalHash, StringComparison.OrdinalIgnoreCase) ||
                !document.SourceSha256.Equals(originalHash, StringComparison.OrdinalIgnoreCase) ||
                !device.SclIedName.Equals(originalIed, StringComparison.OrdinalIgnoreCase) ||
                !device.SclAccessPointName.Equals(originalAp, StringComparison.OrdinalIgnoreCase) ||
                !device.IpAddress.Equals(originalIp, StringComparison.OrdinalIgnoreCase) ||
                device.Port != originalPort)
            {
                AddLog("WARN", "SCL", "AccessPoint selection cancelled: IED state, endpoint or SCD SHA changed during reload.");
                return;
            }

            var workspace = SclEndpointTopology.FindExactWorkspace(document, selected);
            if (workspace is null || !workspace.IedName.Equals(originalIed, StringComparison.OrdinalIgnoreCase))
            {
                AddLog("WARN", "SCL",
                    $"AccessPoint {selected.AccessPointName} is missing, ambiguous, or not offline-browsable in the exact SCD source. Card preserved.");
                SetStatus("SCL AccessPoint switch blocked: model identity could not be proven.");
                return;
            }

            // Perform all potentially failing model computation before clearing
            // runtime rows. There is no network activity, session fallback or GI.
            var signals = SclWorkspaceSignalMapper.BuildSignals(workspace);
            RemoveDevicePoints(device.DeviceId);
            device.Points.Clear();
            _reportPulseUntil.Remove(device.DeviceId);
            RemoveDeviceHighlights(device.DeviceId);
            device.LiveDiscoveryModel = null;
            device.LiveCanonicalModel = null;
            device.SclComparison = null;

            // Apply clears the old AP address on a changed AP, even if the new
            // AP has no declared IP. Only an exact prior AP success can restore it.
            ApplySclWorkspaceToDevice(device, document, workspace, signals);
            RestoreKnownSclEndpointIfAvailable(device, workspace, allowLegacyIedHint: false);
            device.RefreshComputed();

            if (ReferenceEquals(SelectedDevice, device))
            {
                NewDeviceIp = device.IpAddress;
                NewDevicePort = device.Port.ToString(CultureInfo.InvariantCulture);
            }

            AddLog("INFO", "SCL",
                $"Selected SCD AP {workspace.IedName}/{workspace.AccessPointName}; endpoint={device.EndpointText}; provenance={device.SclEndpointOrigin}; SHA={originalHash}. No auto-connect, probe, failover or cyclic polling.");
            SetStatus($"SCD AP {workspace.AccessPointName} selected for {workspace.IedName} • {device.EndpointText} ({device.SclEndpointOrigin}). Press Play to associate.");
            RaiseWorkspaceCounts();
        }
        catch (OperationCanceledException)
        {
            // App closing or source read cancelled: keep the card unchanged.
        }
        catch (Exception ex)
        {
            AddLog("ERROR", "SCL", $"AccessPoint selection failed: {ex.GetType().Name}: {ex.Message}");
            SetStatus("SCD AccessPoint selection failed; existing device remains offline.");
        }
        finally
        {
            device.IsBusy = false;
        }
    }
}
