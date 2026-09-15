using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed record SmartIedBulkActionState(
    bool ShowConnectAll,
    int TotalDevices,
    int CandidateCount,
    string Label,
    string ToolTip);

public static class SmartIedBulkActionPolicy
{
    public static SmartIedBulkActionState Evaluate(IEnumerable<Iec61850MonitorDevice> devices)
    {
        var snapshot = devices?.ToArray() ?? Array.Empty<Iec61850MonitorDevice>();
        var candidateCount = snapshot.Count(IsActionableConnectCandidate);
        var show = snapshot.Length > 1 && candidateCount > 0;
        var label = candidateCount switch
        {
            <= 0 => "Connect All",
            1 => "Connect 1 IED",
            _ => $"Connect {candidateCount} IEDs"
        };
        var toolTip = show
            ? $"Connect/start the {candidateCount} IED(s) that are ready for a bulk connection. Already monitoring, busy, or endpoint-unbound IEDs are skipped."
            : "Bulk connect appears only when multiple IEDs are loaded and at least one has a usable endpoint that still needs connection/monitoring.";

        return new SmartIedBulkActionState(show, snapshot.Length, candidateCount, label, toolTip);
    }

    private static bool IsActionableConnectCandidate(Iec61850MonitorDevice device)
        => device is not null
           && !device.IsBusy
           && !device.IsMonitoring
           && !string.IsNullOrWhiteSpace(device.IpAddress)
           && device.Port is >= 1 and <= 65535;
}

/// <summary>
/// Presentation-only onboarding refinement for the existing Engineering IED Explorer.
/// It does not own IEC 61850 sessions. Existing Open SCL, IP discovery, and Connect All
/// handlers remain the execution authorities; this behavior only exposes them when useful.
/// </summary>
public static class SmartIedOnboardingBehavior
{
    private static readonly ConditionalWeakTable<MainWindow, SmartIedOnboardingState> States = new();

    public static void Install(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var state = States.GetValue(window, static owner => new SmartIedOnboardingState(owner));
        state.InstallOrRefresh();
    }

    private sealed class SmartIedOnboardingState
    {
        private readonly MainWindow _window;
        private readonly HashSet<Button> _wiredAddButtons = new();
        private readonly HashSet<Iec61850MonitorDevice> _subscribedDevices = new();
        private Button? _connectAllButton;
        private Button? _openSclAuthorityButton;
        private bool _installed;

        public SmartIedOnboardingState(MainWindow window) => _window = window;

        public void InstallOrRefresh()
        {
            if (!_installed)
            {
                _installed = true;
                _window.ContentRendered += Window_ContentRendered;
                _window.Closed += Window_Closed;
                _window.Devices.CollectionChanged += Devices_CollectionChanged;
            }

            ReconcileDeviceSubscriptions();
            _window.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(RefreshVisuals));
        }

        private void Window_ContentRendered(object? sender, EventArgs e) => RefreshVisuals();

        private void Devices_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ReconcileDeviceSubscriptions();
            RefreshConnectAll();
            _window.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(RefreshVisuals));
        }

        private void ReconcileDeviceSubscriptions()
        {
            var current = _window.Devices.ToHashSet();
            foreach (var removed in _subscribedDevices.Where(device => !current.Contains(device)).ToArray())
            {
                if (removed is INotifyPropertyChanged observable)
                    observable.PropertyChanged -= Device_PropertyChanged;
                _subscribedDevices.Remove(removed);
            }

            foreach (var added in current.Where(device => !_subscribedDevices.Contains(device)))
            {
                if (added is INotifyPropertyChanged observable)
                    observable.PropertyChanged += Device_PropertyChanged;
                _subscribedDevices.Add(added);
            }
        }

        private void Device_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Iec61850MonitorDevice.IsBusy)
                or nameof(Iec61850MonitorDevice.IsMonitoring)
                or nameof(Iec61850MonitorDevice.IsConnected)
                or nameof(Iec61850MonitorDevice.IpAddress)
                or nameof(Iec61850MonitorDevice.Port)
                or null
                or "")
            {
                RefreshConnectAll();
            }
        }

        private void RefreshVisuals()
        {
            var buttons = Descendants<Button>(_window).ToArray();

            var exactOpenSclButtons = buttons
                .Where(button => ButtonText(button).Equals("Open SCL", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            _openSclAuthorityButton ??= exactOpenSclButtons.FirstOrDefault();
            foreach (var openScl in exactOpenSclButtons)
                openScl.Visibility = Visibility.Collapsed;

            foreach (var addButton in buttons.Where(IsGeneralAddIedButton))
                WireAddIedChooser(addButton);

            _connectAllButton ??= buttons.FirstOrDefault(button =>
                ButtonText(button).Equals("Connect All", StringComparison.OrdinalIgnoreCase) ||
                (button.ToolTip?.ToString()?.StartsWith("Fast-connect every saved", StringComparison.OrdinalIgnoreCase) ?? false));
            RefreshConnectAll();
        }

        private static bool IsGeneralAddIedButton(Button button)
        {
            var text = ButtonText(button);
            if (text.Equals("Add IED", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("Add IED by IP", StringComparison.OrdinalIgnoreCase))
                return true;

            return button.ToolTip?.ToString()?.Equals(
                "Add an IEC 61850 IED by IP address",
                StringComparison.OrdinalIgnoreCase) == true;
        }

        private void WireAddIedChooser(Button button)
        {
            if (!_wiredAddButtons.Add(button))
                return;

            foreach (var textBlock in Descendants<TextBlock>(button))
            {
                if (textBlock.Text.Equals("Add IED by IP", StringComparison.OrdinalIgnoreCase))
                    textBlock.Text = "Add IED";
            }
            if (button.Content is string label && label.Equals("Add IED by IP", StringComparison.OrdinalIgnoreCase))
                button.Content = "Add IED";

            button.ToolTip = "Add IED: open an SCL/CID design (recommended) or discover a live IED by IP address.";
            button.PreviewMouseLeftButtonDown += AddIedButton_PreviewMouseLeftButtonDown;
            button.PreviewKeyDown += AddIedButton_PreviewKeyDown;

            if (VisualTreeHelper.GetParent(button) is Grid)
            {
                Grid.SetColumn(button, 0);
                Grid.SetColumnSpan(button, 3);
            }
        }

        private void AddIedButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Button button || e.ChangedButton != MouseButton.Left)
                return;
            e.Handled = true;
            OpenAddIedMenu(button);
        }

        private void AddIedButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not Button button || e.Key is not (Key.Enter or Key.Space or Key.Down))
                return;
            e.Handled = true;
            OpenAddIedMenu(button);
        }

        private void OpenAddIedMenu(Button sourceButton)
        {
            RefreshVisuals();

            var menu = new ContextMenu
            {
                PlacementTarget = sourceButton,
                Placement = PlacementMode.Bottom,
                StaysOpen = false
            };

            var openScl = new MenuItem
            {
                Header = "Open SCL / CID  ·  Recommended",
                ToolTip = "Load the trusted IEC 61850 engineering model first, then bind/connect the IED when needed."
            };
            openScl.Click += (_, _) => RaiseExistingClick(_openSclAuthorityButton);

            var discoverByIp = new MenuItem
            {
                Header = "Discover IED by IP",
                ToolTip = "Use live MMS discovery when no trusted SCL/CID design is available."
            };
            discoverByIp.Click += (_, _) => RaiseExistingClick(sourceButton);

            menu.Items.Add(openScl);
            menu.Items.Add(new Separator());
            menu.Items.Add(discoverByIp);
            sourceButton.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void RefreshConnectAll()
        {
            if (_connectAllButton is null)
                return;

            var state = SmartIedBulkActionPolicy.Evaluate(_window.Devices);
            _connectAllButton.Visibility = state.ShowConnectAll ? Visibility.Visible : Visibility.Collapsed;
            _connectAllButton.ToolTip = state.ToolTip;

            var label = Descendants<TextBlock>(_connectAllButton)
                .FirstOrDefault(text => text.Text.StartsWith("Connect", StringComparison.OrdinalIgnoreCase));
            if (label is not null)
                label.Text = state.Label;
            else if (_connectAllButton.Content is string)
                _connectAllButton.Content = state.Label;
        }

        private static void RaiseExistingClick(Button? button)
        {
            if (button is null)
                return;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _window.ContentRendered -= Window_ContentRendered;
            _window.Closed -= Window_Closed;
            _window.Devices.CollectionChanged -= Devices_CollectionChanged;
            foreach (var device in _subscribedDevices)
            {
                if (device is INotifyPropertyChanged observable)
                    observable.PropertyChanged -= Device_PropertyChanged;
            }
            _subscribedDevices.Clear();
        }

        private static string ButtonText(Button button)
        {
            if (button.Content is string text)
                return text.Trim();
            return Descendants<TextBlock>(button)
                .Select(item => item.Text?.Trim() ?? string.Empty)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty;
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T match)
                    yield return match;
                foreach (var descendant in Descendants<T>(child))
                    yield return descendant;
            }
        }
    }
}
