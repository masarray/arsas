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
    private const string PrimaryRcbToolTipMarker = "RCB Export Filter";
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

            var actionPanel = FindDescendants<System.Windows.Controls.Primitives.UniformGrid>(item)
                .FirstOrDefault(panel => panel.Children
                    .OfType<Button>()
                    .Any(button => (button.ToolTip?.ToString() ?? string.Empty)
                        .Contains(PrimaryRcbToolTipMarker, StringComparison.OrdinalIgnoreCase)));
            if (actionPanel?.Parent is not Grid cardGrid)
                return;

            // RCB is a protocol-engineering tool, not a lifecycle control. Remove the
            // icon-only duplicate and keep the primary row as Play / Stop / Edit / Save.
            var legacyRcbButton = actionPanel.Children
                .OfType<Button>()
                .FirstOrDefault(button => (button.ToolTip?.ToString() ?? string.Empty)
                    .Contains(PrimaryRcbToolTipMarker, StringComparison.OrdinalIgnoreCase));
            if (legacyRcbButton != null)
                actionPanel.Children.Remove(legacyRcbButton);
            actionPanel.Columns = 4;
            cardGrid.MinHeight = Math.Max(cardGrid.MinHeight, 100);

            var existing = cardGrid.Children
                .OfType<Grid>()
                .FirstOrDefault(panel => Equals(panel.Tag, CapabilityPanelMarker));
            if (existing != null)
                cardGrid.Children.Remove(existing);

            var gooseButton = CreateCapabilityButton(
                "GOOSE",
                (_, _) => OpenGooseWorkspace(currentWindow, currentDevice));
            var smvButton = CreateCapabilityButton(
                "SMV",
                (_, _) => OpenSmvViewer(currentWindow, currentDevice));
            var fileButton = CreateCapabilityButton(
                "FILE",
                (_, _) => OpenFaultRecordWindow(currentWindow, currentDevice));
            var rcbButton = CreateCapabilityButton(
                "RCB",
                (_, _) => currentWindow.OpenRcbExportFilter(currentDevice));

            var capabilityPanel = new Grid
            {
                Tag = CapabilityPanelMarker,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 5, 0, 0)
            };
            for (var column = 0; column < 4; column++)
                capabilityPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddCapabilityButton(capabilityPanel, gooseButton, 0);
            AddCapabilityButton(capabilityPanel, smvButton, 1);
            AddCapabilityButton(capabilityPanel, fileButton, 2);
            AddCapabilityButton(capabilityPanel, rcbButton, 3);

            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(capabilityPanel, cardGrid.RowDefinitions.Count - 1);
            Grid.SetColumn(capabilityPanel, 0);
            Grid.SetColumnSpan(capabilityPanel, 2);
            Panel.SetZIndex(capabilityPanel, 10);
            cardGrid.Children.Add(capabilityPanel);

            var registration = new CardRegistration(
                currentDevice,
                gooseButton,
                smvButton,
                fileButton,
                rcbButton,
                (_, _) => QueueCapabilityRefresh(item, currentDevice, gooseButton, smvButton, fileButton, rcbButton));
            Registrations.Add(item, registration);
            currentDevice.PropertyChanged += registration.PropertyChangedHandler;
            RefreshCapabilityState(currentDevice, gooseButton, smvButton, fileButton, rcbButton);
        }));
    }

    private static void QueueCapabilityRefresh(
        ListBoxItem item,
        Iec61850MonitorDevice device,
        Button gooseButton,
        Button smvButton,
        Button fileButton,
        Button rcbButton)
    {
        void RefreshOnUiThread()
        {
            if (!item.IsLoaded || !ReferenceEquals(item.DataContext, device))
                return;

            RefreshCapabilityState(device, gooseButton, smvButton, fileButton, rcbButton);
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

    private static Button CreateCapabilityButton(string capability, RoutedEventHandler click)
    {
        var button = new Button
        {
            Height = 25,
            MinHeight = 0,
            MinWidth = 0,
            Padding = new Thickness(2, 0, 2, 0),
            Margin = new Thickness(2, 0, 2, 0),
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 9.4,
            FontWeight = FontWeights.SemiBold,
            Content = capability,
            Tag = capability
        };
        if (Application.Current.TryFindResource("SoftButton") is Style style)
            button.Style = style;
        button.Click += click;
        return button;
    }

    private static void AddCapabilityButton(Grid panel, Button button, int column)
    {
        Grid.SetColumn(button, column);
        panel.Children.Add(button);
    }

    private static void RefreshCapabilityState(
        Iec61850MonitorDevice device,
        Button gooseButton,
        Button smvButton,
        Button fileButton,
        Button rcbButton)
    {
        var hasGoose = device.SclWorkspace?.GooseStreams.Count > 0 ||
                       device.LiveDiscoveryModel?.GooseControlBlocks.Count > 0;
        var hasSmv = device.SclWorkspace?.SampledValuesStreams.Count > 0 ||
                     device.LiveDiscoveryModel?.SampledValueControlBlocks.Count > 0;

        var fileServiceVerified = device.LiveDiscoveryModel?.FileDirectory.IsSuccess == true;
        var endpointReady = !string.IsNullOrWhiteSpace(device.IpAddress) &&
                            IPAddress.TryParse(device.IpAddress, out _) &&
                            device.Port is > 0 and <= 65535;
        var hasRcbInventory = device.LiveDiscoveryModel?.ReportControls.Count > 0 ||
                              !string.IsNullOrWhiteSpace(device.SclSourcePath);

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
        ApplyState(
            rcbButton,
            hasRcbInventory ? CapabilityState.Available : CapabilityState.Unavailable,
            hasRcbInventory
                ? $"Select and export one RCB for legacy SAS import from {device.Name}"
                : $"Open SCL or complete live discovery before using RCB Export for {device.Name}");
    }

    private static void ApplyState(Button button, CapabilityState state, string toolTip)
    {
        button.IsEnabled = state != CapabilityState.Unavailable;
        button.Opacity = state switch
        {
            CapabilityState.Available => 1.0,
            CapabilityState.ProbeReady => 0.78,
            _ => 0.42
        };
        button.ToolTip = toolTip;
        button.Background = state switch
        {
            CapabilityState.Available => new SolidColorBrush(Color.FromRgb(234, 241, 255)),
            CapabilityState.ProbeReady => new SolidColorBrush(Color.FromRgb(255, 247, 230)),
            _ => new SolidColorBrush(Color.FromRgb(243, 246, 250))
        };
        button.BorderBrush = state switch
        {
            CapabilityState.Available => new SolidColorBrush(Color.FromRgb(177, 198, 237)),
            CapabilityState.ProbeReady => new SolidColorBrush(Color.FromRgb(232, 194, 121)),
            _ => new SolidColorBrush(Color.FromRgb(215, 223, 234))
        };
        button.Foreground = state == CapabilityState.ProbeReady
            ? new SolidColorBrush(Color.FromRgb(138, 91, 10))
            : new SolidColorBrush(Color.FromRgb(35, 67, 119));
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
        Button RcbButton,
        PropertyChangedEventHandler PropertyChangedHandler);
}
