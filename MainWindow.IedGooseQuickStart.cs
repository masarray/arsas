// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using ArIED61850Tester.Services;
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
        // Same device authority as the visible GOOSE capability pill.
        // The binding updates after live discovery/reconnect without polling.
        button.SetBinding(UIElement.IsEnabledProperty,
            new System.Windows.Data.Binding(nameof(Iec61850MonitorDevice.HasGooseControlBlock))
            {
                Source = device,
                Mode = System.Windows.Data.BindingMode.OneWay
            });
        button.Click += IedGooseQuickStart_Click;
        actionGrid.Children.Add(button);
    }

    private void IedGooseQuickStart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: Iec61850MonitorDevice device } &&
            device.HasGooseControlBlock)
            OpenIedGooseSubscriber(device);
    }

    /// <summary>
    /// Shared entry point for the visible IED-card GOOSE capability pill and
    /// the legacy icon. The UI must show GOOSE before touching optional Npcap.
    /// The operator's next adapter selection starts capture exactly once.
    /// Direct navigation to the GOOSE tab preserves its manual Start workflow.
    /// </summary>
    internal void OpenIedGooseSubscriber(Iec61850MonitorDevice device)
    {
        if (!device.HasGooseControlBlock)
            return;
        SelectedDevice = device;
        _pendingIedGooseAutoStart = null;
        MainTabs.SelectedIndex = GooseSubscriberTabIndex;
        UpdateNavigationVisuals(GooseSubscriberTabIndex, animate: true);
        ActivateGooseSubscriberWorkspace();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(async () =>
        {
            if (Dispatcher.HasShutdownStarted || MainTabs.SelectedIndex != GooseSubscriberTabIndex)
                return;
            if (IsGooseCapturing || GooseActionBusy)
            {
                GooseStatusText = IsGooseCapturing
                    ? "GOOSE monitoring is already running. Stop it before changing network adapters."
                    : "GOOSE capture is busy. Wait for the current operation.";
                return;
            }
            // Share the same asynchronous Npcap discovery as ordinary tab
            // navigation. A background worker also resolves OS NIC routing:
            // neither scan can block the first GOOSE frame/render.
            if (GooseAdapters.Count == 0)
                await EnsureGooseAdaptersForNavigationAsync();
            if (Dispatcher.HasShutdownStarted || MainTabs.SelectedIndex != GooseSubscriberTabIndex ||
                IsGooseCapturing || GooseActionBusy)
                return;

            // Capture a frozen list on the WPF thread before examining network
            // interfaces off-thread. The candidate is observation only, not
            // proof that an IED is publishing Ethernet GOOSE.
            var candidates = GooseAdapters.ToArray();
            GooseAdapterOption? proposed;
            string route;
            try
            {
                var resolved = await Task.Run(() =>
                {
                    var adapter = ResolveGooseAdapterForIed(device, out var evidence, candidates);
                    return (Adapter: adapter, Evidence: evidence);
                });
                proposed = resolved.Adapter;
                route = resolved.Evidence;
            }
            catch (Exception ex)
            {
                proposed = null;
                route = $"Network adapter routing could not be resolved: {ex.Message}. Select the capture adapter manually.";
            }
            if (Dispatcher.HasShutdownStarted || MainTabs.SelectedIndex != GooseSubscriberTabIndex ||
                IsGooseCapturing || GooseActionBusy)
                return;
            _gooseCaptureContextHint = route.Contains("Same-PC", StringComparison.Ordinal)
                ? "Same-PC simulator: verify it actually publishes Ethernet GOOSE on this adapter."
                : "If no frames arrive, check the publisher, station NIC and VLAN.";
            AddLog("INFO", "GOOSE", $"Adapter selection: {route}");
            if (proposed is not null)
            {
                SelectedGooseAdapter = proposed;
                GooseStatusText = $"Matched capture NIC {proposed.DisplayText}. GOOSE frame reception not yet verified.";
                SetStatus($"GOOSE: observing on {proposed.DisplayText}.");
                StartGooseSubscriber_Click(this, new RoutedEventArgs());
                return;
            }
            SelectedGooseAdapter = null;
            _pendingIedGooseAutoStart = device;
            GooseStatusText = GooseAdapters.Count == 0
                ? "No Npcap capture adapter found. Check installation and refresh."
                : $"Choose GOOSE adapter for {device.Name}; capture starts on confirmation. {route}";
            SetStatus($"GOOSE: select network adapter for {device.Name}.");
        }));
    }

    private GooseAdapterOption? ResolveGooseAdapterForIed(
        Iec61850MonitorDevice device,
        out string routeDetail,
        IReadOnlyList<GooseAdapterOption>? capturedAdapters = null)
    {
        routeDetail = "No route was resolved.";
        if (!IPAddress.TryParse(device.IpAddress, out var target) ||
            target.AddressFamily != AddressFamily.InterNetwork)
        {
            routeDetail = $"IED address '{device.IpAddress}' is not a valid IPv4 endpoint.";
            return null;
        }

        try
        {
            var route = ResolveLocalIpv4ForTarget(target);
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Select(nic => new GooseWindowsInterfaceSnapshot(
                    nic.Id,
                    nic.Name,
                    nic.Description,
                    nic.GetPhysicalAddress().ToString(),
                    nic.GetIPProperties().UnicastAddresses
                        .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(unicast => unicast.Address)
                        .ToArray()))
                .ToArray();
            var selection = GooseAdapterSelectionPolicy.Resolve(
                target, route, interfaces, capturedAdapters ?? GooseAdapters.ToArray());
            routeDetail = selection.Evidence;
            return selection.Adapter;
        }
        catch (Exception ex) when (ex is NetworkInformationException or SocketException or InvalidOperationException)
        {
            routeDetail = $"Network adapter identity unavailable: {ex.Message}. Select the capture adapter manually.";
            return null;
        }
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
        => GooseAdapterSelectionPolicy.IsNpcapPseudoLoopback(adapter);
}
