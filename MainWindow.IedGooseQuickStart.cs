// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private const string IedGooseQuickStartTag = "ARSAS_IED_GOOSE_QUICK_START";
    private const int GooseSubscriberTabIndex = 4;
    private static readonly bool IedGooseQuickStartRegistered = RegisterIedGooseQuickStart();

    private static bool RegisterIedGooseQuickStart()
    {
        EventManager.RegisterClassHandler(
            typeof(UniformGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(IedActionGrid_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void IedActionGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not UniformGrid grid ||
            grid.DataContext is not Iec61850MonitorDevice device ||
            Window.GetWindow(grid) is not MainWindow window)
        {
            return;
        }

        window.InstallIedGooseQuickStart(grid, device);
    }

    private void InstallIedGooseQuickStart(UniformGrid actionGrid, Iec61850MonitorDevice device)
    {
        if (actionGrid.Children
            .OfType<Button>()
            .Any(button => Equals(button.Tag, IedGooseQuickStartTag)))
        {
            return;
        }

        var deviceButtons = actionGrid.Children
            .OfType<Button>()
            .Count(button => ReferenceEquals(button.Tag, device));
        if (deviceButtons < 4)
            return;

        actionGrid.Columns = Math.Max(6, actionGrid.Columns);

        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(
                "M12,10 A2,2 0 1 1 11.99,10 " +
                "M12,14 V22 " +
                "M7.8,16.2 A6,6 0 0 1 7.8,7.8 " +
                "M4.9,19.1 A10,10 0 0 1 4.9,4.9"),
            Style = TryFindResource("LucideIcon") as Style,
            Stroke = new SolidColorBrush(Color.FromRgb(22, 163, 74))
        };

        var button = new Button
        {
            Tag = IedGooseQuickStartTag,
            CommandParameter = device,
            Style = TryFindResource("IedIconButton") as Style,
            Width = 27,
            Height = 27,
            Margin = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Open GOOSE Subscriber, choose the station Ethernet adapter, then press Start",
            Content = new Viewbox
            {
                Width = 14,
                Height = 14,
                Child = icon
            }
        };
        button.Click += IedGooseQuickStart_Click;
        actionGrid.Children.Add(button);
    }

    private void IedGooseQuickStart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: Iec61850MonitorDevice device })
            return;

        SelectedDevice = device;
        // Workspace indices: Explorer=0, Live=1, Events=2, Alarm=3, GOOSE=4.
        // This CTA is navigation + adapter recommendation, never a hidden Start.
        MainTabs.SelectedIndex = GooseSubscriberTabIndex;
        UpdateNavigationVisuals(GooseSubscriberTabIndex, animate: true);
        ActivateGooseSubscriberWorkspace();

        // First render the GOOSE tab; Npcap enumeration is optional and can fail.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (Dispatcher.HasShutdownStarted || MainTabs.SelectedIndex != GooseSubscriberTabIndex)
                return;

            if (GooseAdapters.Count == 0)
                RefreshGooseAdapters();

            // Do not stop/rebind an ongoing capture when the operator clicks a
            // different IED card. The active capture adapter remains authoritative.
            if (IsGooseCapturing || GooseActionBusy)
            {
                GooseStatusText = IsGooseCapturing
                    ? $"GOOSE capture already running on {SelectedGooseAdapter?.DisplayText ?? "the active adapter"}. Stop before changing adapters. Captured publishers remain visible."
                    : "GOOSE capture is busy. Wait until it finishes before changing adapters.";
                return;
            }

            // Unicast MMS routing is only a suggestion for multicast L2 GOOSE.
            // It cannot prove station-LAN, switch mirror or VLAN placement.
            var adapter = ResolveGooseAdapterForIed(device, out var routeDetail);
            SelectedGooseAdapter = adapter;
            if (adapter is null)
            {
                GooseStatusText = GooseAdapters.Count == 0
                    ? "No capture adapters available. Install or enable Npcap, then refresh the adapter list."
                    : $"Choose the station Ethernet adapter for {device.Name}, then press Start. MMS IP routing alone cannot determine the GOOSE LAN.";
                SetStatus($"GOOSE: select a capture adapter for {device.Name}.");
                AddLog("INFO", "GOOSE", $"Manual adapter selection requested for {device.Name}: {routeDetail}");
                return;
            }

            GooseStatusText =
                $"Suggested adapter: {adapter.DisplayText} for {device.Name}. Confirm station-LAN/VLAN and press Start to monitor GOOSE frames.";
            SetStatus($"GOOSE: confirm adapter and press Start for {device.Name}.");
            AddLog("INFO", "GOOSE", $"Capture adapter suggested, not started: {routeDetail}");
        }));
    }

    private GooseAdapterOption? ResolveGooseAdapterForIed(
        Iec61850MonitorDevice device,
        out string routeDetail)
    {
        routeDetail = "No route was resolved.";
        if (!IPAddress.TryParse(device.IpAddress, out var target) ||
            target.AddressFamily != AddressFamily.InterNetwork)
        {
            routeDetail = $"IED address '{device.IpAddress}' is not a valid IPv4 endpoint.";
            return null;
        }

        var localAddress = ResolveLocalIpv4ForTarget(target);
        if (localAddress is not null)
        {
            var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(adapter => adapter.GetIPProperties().UnicastAddresses.Any(unicast =>
                    unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                    unicast.Address.Equals(localAddress)));

            if (networkInterface is not null)
            {
                var matches = GooseAdapters
                    .Where(adapter => CaptureAdapterMatchesNetworkInterface(adapter, networkInterface))
                    .ToList();
                if (matches.Count == 1)
                {
                    routeDetail =
                        $"Windows route {localAddress} → {target} via {networkInterface.Name} ({networkInterface.Description})";
                    return matches[0];
                }

                routeDetail = matches.Count == 0
                    ? $"Windows selected {networkInterface.Name} ({localAddress}), but no Npcap adapter matched its ID/MAC."
                    : $"Windows selected {networkInterface.Name} ({localAddress}), but {matches.Count} Npcap adapters matched.";
            }
            else
            {
                routeDetail = $"Windows selected local address {localAddress}, but its network interface was not found.";
            }
        }

        var usable = GooseAdapters
            .Where(adapter => !LooksLikeLoopback(adapter))
            .ToList();
        if (usable.Count == 1)
        {
            routeDetail += $" Falling back to the only non-loopback capture adapter: {usable[0].DisplayText}.";
            return usable[0];
        }

        return null;
    }

    private static IPAddress? ResolveLocalIpv4ForTarget(IPAddress target)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(target, 102));
            return (socket.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static bool CaptureAdapterMatchesNetworkInterface(
        GooseAdapterOption captureAdapter,
        NetworkInterface networkInterface)
    {
        var captureMac = NormalizeGooseAdapterMac(captureAdapter.MacAddress);
        var windowsMac = NormalizeGooseAdapterMac(networkInterface.GetPhysicalAddress().ToString());
        if (captureMac.Length > 0 && captureMac.Equals(windowsMac, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(networkInterface.Id) &&
            captureAdapter.Name.Contains(networkInterface.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return captureAdapter.FriendlyName.Equals(networkInterface.Name, StringComparison.OrdinalIgnoreCase) ||
               captureAdapter.FriendlyName.Equals(networkInterface.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeGooseAdapterMac(string? value)
        => Regex.Replace(value ?? string.Empty, "[^0-9A-Fa-f]", string.Empty).ToUpperInvariant();

    private static bool LooksLikeLoopback(GooseAdapterOption adapter)
    {
        var text = $"{adapter.Name} {adapter.Description} {adapter.FriendlyName}";
        return text.Contains("loopback", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("npcap loopback", StringComparison.OrdinalIgnoreCase);
    }
}
