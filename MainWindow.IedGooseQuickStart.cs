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
            ToolTip = "Open GOOSE Subscriber and start capture on the Ethernet adapter routed to this IED",
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

    private async void IedGooseQuickStart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: Iec61850MonitorDevice device })
            return;

        SelectedDevice = device;
        MainTabs.SelectedIndex = 3;
        UpdateNavigationVisuals(3, animate: true);
        ActivateGooseSubscriberWorkspace();
        await Dispatcher.Yield(DispatcherPriority.Loaded);

        if (GooseAdapters.Count == 0)
            RefreshGooseAdapters();

        var adapter = ResolveGooseAdapterForIed(device, out var routeDetail);
        if (adapter is null)
        {
            GooseStatusText =
                $"GOOSE workspace opened for {device.Name}, but ARSAS could not identify one unique capture adapter for route {device.IpAddress}. Select the station-LAN adapter and press Start.";
            SetStatus($"GOOSE: adapter selection required for {device.Name}.");
            AddLog("WARN", "GOOSE", $"Automatic adapter selection failed for {device.Name} ({device.IpAddress}). {routeDetail}");
            return;
        }

        await StartGooseForIedAsync(device, adapter, routeDetail);
    }

    private async Task StartGooseForIedAsync(
        Iec61850MonitorDevice device,
        GooseAdapterOption adapter,
        string routeDetail)
    {
        if (GooseActionBusy)
            return;

        if (IsGooseCapturing &&
            SelectedGooseAdapter is not null &&
            SelectedGooseAdapter.Name.Equals(adapter.Name, StringComparison.OrdinalIgnoreCase))
        {
            SelectedGooseAdapter = adapter;
            GooseStatusText =
                $"LIVE on {adapter.DisplayText} · selected from {device.Name} route {device.IpAddress} · capture already running.";
            SetStatus($"GOOSE Subscriber already live for {device.Name} on the routed Ethernet adapter.");
            return;
        }

        if (IsGooseCapturing)
            await StopGooseSubscriberAsync();

        SelectedGooseAdapter = adapter;
        GooseActionBusy = true;
        try
        {
            RefreshGooseBindingPreview();
            ResetGooseView(resetCounters: true);
            await _gooseSubscriberRuntime.StartAsync(
                adapter.Selector,
                _gooseBindingCatalog.SclDocument,
                GooseCaptureFilter,
                _applicationCancellation.Token);

            IsGooseCapturing = true;
            GooseStatusText =
                $"Listening on {adapter.DisplayText} for {device.Name} ({device.IpAddress}). Waiting for GOOSE frames…";
            SetStatus($"GOOSE Subscriber started for {device.Name} on its routed Ethernet adapter.");
            AddLog(
                "INFO",
                "GOOSE",
                $"One-click subscriber started for {device.Name} ({device.IpAddress}) on adapter {adapter.Index}: {adapter.Description}. Route selection: {routeDetail}. Binding: {_gooseBindingCatalog.Summary}");
        }
        catch (Exception ex)
        {
            IsGooseCapturing = false;
            GooseStatusText = $"Could not start GOOSE subscriber for {device.Name}: {DescribeGooseFailure(ex)}";
            AddLog("ERROR", "GOOSE", GooseStatusText);
            MarkDiagnosticAlert();
        }
        finally
        {
            GooseActionBusy = false;
        }
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
