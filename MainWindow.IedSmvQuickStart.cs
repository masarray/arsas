// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private const string IedSmvQuickStartTag = "ARSAS_IED_SMV_QUICK_START";
    private static readonly bool IedSmvQuickStartRegistered = RegisterIedSmvQuickStart();
    private SmvViewerWindow? _activeSmvViewerWindow;

    private static bool RegisterIedSmvQuickStart()
    {
        EventManager.RegisterClassHandler(
            typeof(UniformGrid),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(IedSmvActionGrid_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void IedSmvActionGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not UniformGrid grid ||
            grid.DataContext is not Iec61850MonitorDevice device ||
            Window.GetWindow(grid) is not MainWindow window)
        {
            return;
        }

        window.InstallIedSmvQuickStart(grid, device);
    }

    private void InstallIedSmvQuickStart(UniformGrid actionGrid, Iec61850MonitorDevice device)
    {
        if (actionGrid.Children
            .OfType<Button>()
            .Any(button => Equals(button.Tag, IedSmvQuickStartTag)))
        {
            return;
        }

        var deviceButtons = actionGrid.Children
            .OfType<Button>()
            .Count(button => ReferenceEquals(button.Tag, device));
        if (deviceButtons < 4)
            return;

        actionGrid.Columns = Math.Max(7, actionGrid.Columns);

        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M3,12 H6 L8,5 L11,19 L14,8 L16,12 H21"),
            Style = TryFindResource("LucideIcon") as Style,
            Stroke = new SolidColorBrush(Color.FromRgb(79, 70, 229)),
            StrokeThickness = 2.1
        };

        var button = new Button
        {
            Tag = IedSmvQuickStartTag,
            CommandParameter = device,
            Style = TryFindResource("IedIconButton") as Style,
            Width = 27,
            Height = 27,
            Margin = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "Open Sampled Values and capture the first discovered SMV stream on the Ethernet adapter routed to this IED",
            Content = new Viewbox
            {
                Width = 14,
                Height = 14,
                Child = icon
            }
        };
        button.Click += IedSmvQuickStart_Click;
        actionGrid.Children.Add(button);
    }

    private void IedSmvQuickStart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: Iec61850MonitorDevice device })
            return;

        SelectedDevice = device;

        if (_activeSmvViewerWindow is { IsLoaded: true } existing)
        {
            if (existing.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                if (existing.WindowState == WindowState.Minimized)
                    existing.WindowState = WindowState.Normal;
                existing.Activate();
                existing.Focus();
                SetStatus($"SMV workspace active for {device.Name}; the loaded snapshot and waveform were preserved.");
                return;
            }

            existing.Close();
        }

        IReadOnlyList<GooseAdapterOption> adapters;
        try
        {
            adapters = new SmvSnapshotCaptureService().ListAdapters();
        }
        catch (Exception ex)
        {
            adapters = Array.Empty<GooseAdapterOption>();
            AddLog("ERROR", "SMV", $"Npcap adapter discovery failed for {device.Name}: {ex.Message}");
            MarkDiagnosticAlert();
        }

        var adapter = ResolveSmvAdapterForIed(device, adapters, out var routeDetail);
        var window = new SmvViewerWindow(device)
        {
            Owner = this
        };
        window.EnableFastWorkflow(adapter, routeDetail);
        window.Closed += ActiveSmvViewerWindow_Closed;
        _activeSmvViewerWindow = window;
        window.Show();
        window.Activate();

        if (adapter is null)
        {
            SetStatus($"SMV workspace opened for {device.Name}; select the process/station-bus adapter to capture.");
            AddLog(
                "WARN",
                "SMV",
                $"One-click SMV opened for {device.Name} ({device.IpAddress}), but automatic adapter selection was not unique. {routeDetail}");
            return;
        }

        SetStatus($"SMV snapshot starting for {device.Name} on its routed Ethernet adapter.");
        AddLog(
            "INFO",
            "SMV",
            $"One-click SMV workspace opened for {device.Name} ({device.IpAddress}) on adapter {adapter.Index}: {adapter.Description}. Route selection: {routeDetail}");
    }

    private void ActiveSmvViewerWindow_Closed(object? sender, EventArgs e)
    {
        if (sender is SmvViewerWindow window)
            window.Closed -= ActiveSmvViewerWindow_Closed;
        if (ReferenceEquals(_activeSmvViewerWindow, sender))
            _activeSmvViewerWindow = null;
    }

    private static GooseAdapterOption? ResolveSmvAdapterForIed(
        Iec61850MonitorDevice device,
        IReadOnlyCollection<GooseAdapterOption> adapters,
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
                .FirstOrDefault(candidate => candidate.GetIPProperties().UnicastAddresses.Any(unicast =>
                    unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                    unicast.Address.Equals(localAddress)));

            if (networkInterface is not null)
            {
                var matches = adapters
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

        var usable = adapters
            .Where(adapter => !LooksLikeLoopback(adapter))
            .ToList();
        if (usable.Count == 1)
        {
            routeDetail += $" Falling back to the only non-loopback capture adapter: {usable[0].DisplayText}.";
            return usable[0];
        }

        return null;
    }
}
