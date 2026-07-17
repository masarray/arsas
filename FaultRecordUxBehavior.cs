using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Adds compact protocol-capability actions to each IED card. Capability actions are
/// intentionally scoped to the card's IED instead of being presented as global tools.
/// </summary>
internal static class FaultRecordUxBehavior
{
    private const string CapabilityPanelMarker = "ARSAS_IED_CAPABILITIES";
    private static readonly ConditionalWeakTable<ListBoxItem, CardRegistration> Registrations = new();
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        EventManager.RegisterClassHandler(
            typeof(ListBoxItem),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnListBoxItemLoaded),
            handledEventsToo: true);
    }

    private static void OnListBoxItemLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBoxItem item || item.DataContext is not Iec61850MonitorDevice device)
            return;
        if (Window.GetWindow(item) is not MainWindow mainWindow)
            return;
        if (Registrations.TryGetValue(item, out _))
            return;

        item.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (item.DataContext is not Iec61850MonitorDevice currentDevice ||
                Window.GetWindow(item) is not MainWindow currentWindow ||
                Registrations.TryGetValue(item, out _))
            {
                return;
            }

            var actionPanel = FindDescendants<StackPanel>(item)
                .FirstOrDefault(panel =>
                    panel.Orientation == Orientation.Horizontal &&
                    panel.HorizontalAlignment == HorizontalAlignment.Right &&
                    FindDescendants<Button>(panel).Count() >= 3);
            if (actionPanel?.Parent is not Grid cardGrid)
                return;

            var existing = cardGrid.Children
                .OfType<StackPanel>()
                .FirstOrDefault(panel => Equals(panel.Tag, CapabilityPanelMarker));
            if (existing != null)
                cardGrid.Children.Remove(existing);

            var gooseButton = CreateCapabilityButton(
                "GOOSE",
                "M4,6 L9,6 M4,10 L13,10 M4,14 L17,14 M17,6 L20,6 M13,10 L20,10 M9,14 L20,14",
                (_, _) => OpenGooseWorkspace(currentWindow, currentDevice));
            var smvButton = CreateCapabilityButton(
                "SMV",
                "M2,12 C4,12 4,5 7,5 C10,5 9,19 12,19 C15,19 14,8 17,8 C19,8 19,12 22,12",
                (_, _) => OpenSmvViewer(currentWindow, currentDevice));
            var fileButton = CreateCapabilityButton(
                "File Transfer",
                "M6,2 L15,2 L20,7 L20,22 L6,22 Z M15,2 L15,7 L20,7 M13,10 L13,17 M10,14 L13,17 L16,14",
                (_, _) => OpenFaultRecordWindow(currentWindow, currentDevice));

            var capabilityPanel = new StackPanel
            {
                Tag = CapabilityPanelMarker,
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4, 2, 7, 0)
            };
            capabilityPanel.Children.Add(gooseButton);
            capabilityPanel.Children.Add(smvButton);
            capabilityPanel.Children.Add(fileButton);

            // Keep protocol capabilities on their own card row. The previous overlay shared
            // the lifecycle-action row and could cover the Stop button when File Transfer was enabled.
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(capabilityPanel, cardGrid.RowDefinitions.Count - 1);
            Grid.SetColumn(capabilityPanel, 1);
            Panel.SetZIndex(capabilityPanel, 10);
            cardGrid.Children.Add(capabilityPanel);

            var registration = new CardRegistration(
                currentDevice,
                gooseButton,
                smvButton,
                fileButton,
                (_, _) => QueueCapabilityRefresh(item, currentDevice, gooseButton, smvButton, fileButton));
            Registrations.Add(item, registration);
            currentDevice.PropertyChanged += registration.PropertyChangedHandler;
            RefreshCapabilityState(currentDevice, gooseButton, smvButton, fileButton);
        }));
    }

    private static void QueueCapabilityRefresh(
        ListBoxItem item,
        Iec61850MonitorDevice device,
        Button gooseButton,
        Button smvButton,
        Button fileButton)
    {
        void RefreshOnUiThread()
        {
            if (!item.IsLoaded || !ReferenceEquals(item.DataContext, device))
                return;

            RefreshCapabilityState(device, gooseButton, smvButton, fileButton);
        }

        if (item.Dispatcher.CheckAccess())
        {
            RefreshOnUiThread();
            return;
        }

        // Discovery and connection state can be published from worker threads. WPF controls
        // have thread affinity, so every capability-state update must be marshalled to the card UI.
        item.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(RefreshOnUiThread));
    }

    private static Button CreateCapabilityButton(string capability, string pathData, RoutedEventHandler click)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(pathData),
            Stretch = Stretch.Uniform,
            Stroke = Application.Current.TryFindResource("Accent") as Brush ?? Brushes.SteelBlue,
            StrokeThickness = 1.8,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };

        var button = new Button
        {
            Width = 22,
            Height = 22,
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 3, 0),
            Focusable = false,
            Content = new Viewbox { Width = 13, Height = 13, Child = icon },
            Tag = capability
        };
        if (Application.Current.TryFindResource("IedIconButton") is Style style)
            button.Style = style;
        button.Click += click;
        return button;
    }

    private static void RefreshCapabilityState(
        Iec61850MonitorDevice device,
        Button gooseButton,
        Button smvButton,
        Button fileButton)
    {
        var hasGoose = device.SclWorkspace?.GooseStreams.Count > 0 ||
                       device.LiveDiscoveryModel?.GooseControlBlocks.Count > 0;
        var hasSmv = device.SclWorkspace?.SampledValuesStreams.Count > 0 ||
                     device.LiveDiscoveryModel?.SampledValueControlBlocks.Count > 0;

        var fileServiceVerified = device.LiveDiscoveryModel?.FileDirectory.IsSuccess == true;
        var endpointReady = !string.IsNullOrWhiteSpace(device.IpAddress) &&
                            IPAddress.TryParse(device.IpAddress, out _) &&
                            device.Port is > 0 and <= 65535;

        ApplyState(
            gooseButton,
            hasGoose ? CapabilityState.Available : CapabilityState.Unavailable,
            hasGoose
                ? $"Open GOOSE Subscriber for {device.Name}"
                : $"{device.Name} has no configured or discovered GOOSE control block");
        ApplyState(
            smvButton,
            hasSmv ? CapabilityState.Available : CapabilityState.Unavailable,
            hasSmv
                ? $"Open SMV Viewer for {device.Name}"
                : $"{device.Name} has no configured or discovered Sampled Value control block");
        ApplyState(
            fileButton,
            fileServiceVerified
                ? CapabilityState.Available
                : endpointReady ? CapabilityState.ProbeReady : CapabilityState.Unavailable,
            fileServiceVerified
                ? $"Browse and download fault records from {device.Name}"
                : endpointReady
                    ? $"Probe the MMS file service and browse fault records from {device.Name}"
                    : $"Bind a valid MMS endpoint before using File Transfer for {device.Name}");
    }

    private static void ApplyState(Button button, CapabilityState state, string toolTip)
    {
        button.IsEnabled = state != CapabilityState.Unavailable;
        button.Opacity = state switch
        {
            CapabilityState.Available => 1.0,
            CapabilityState.ProbeReady => 0.72,
            _ => 0.32
        };
        button.ToolTip = toolTip;
    }

    private static void OpenGooseWorkspace(MainWindow mainWindow, Iec61850MonitorDevice device)
    {
        mainWindow.SelectedDevice = device;
        if (mainWindow.FindName("MainTabs") is TabControl tabs)
            tabs.SelectedIndex = 3;
        mainWindow.Activate();
    }

    private static void OpenSmvViewer(MainWindow mainWindow, Iec61850MonitorDevice device)
    {
        mainWindow.SelectedDevice = device;
        var existing = Application.Current.Windows
            .OfType<SmvViewerWindow>()
            .FirstOrDefault(window => window.DeviceId.Equals(device.DeviceId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        new SmvViewerWindow(device) { Owner = mainWindow }.Show();
    }

    private static void OpenFaultRecordWindow(MainWindow mainWindow, Iec61850MonitorDevice device)
    {
        mainWindow.SelectedDevice = device;
        if (string.IsNullOrWhiteSpace(device.IpAddress) || !IPAddress.TryParse(device.IpAddress, out _))
        {
            MessageBox.Show(
                mainWindow,
                "Bind a valid MMS endpoint to this IED before using File Transfer.",
                "File Transfer",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var endpoint = $"{device.IpAddress}:{device.Port}";
        var existing = Application.Current.Windows
            .OfType<FaultRecordWindow>()
            .FirstOrDefault(window => window.EndpointText.Equals(endpoint, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        new FaultRecordWindow(device.Name, device.IpAddress, device.Port)
        {
            Owner = mainWindow
        }.Show();
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var nested in FindDescendants<T>(child))
                yield return nested;
        }
    }

    private enum CapabilityState
    {
        Unavailable,
        ProbeReady,
        Available
    }

    private sealed record CardRegistration(
        Iec61850MonitorDevice Device,
        Button GooseButton,
        Button SmvButton,
        Button FileButton,
        PropertyChangedEventHandler PropertyChangedHandler);
}
