using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Bench-focused FAT UX hardening that is intentionally additive to the existing
/// report-only acquisition and command execution paths. No MMS/RCB/DataSet behavior
/// is changed here.
/// </summary>
public partial class IoListTestingWindow
{
    private bool _fatFieldUxInstalled;
    private bool _fatStopLayoutInstalled;
    private Popup? _fatCommandFailureShout;
    private TextBlock? _fatCommandFailureShoutText;
    private DispatcherTimer? _fatCommandFailureShoutTimer;
    private Iec61850MonitorDevice? _fatFieldCommandDevice;
    private readonly HashSet<SignalDefinition> _fatFieldSubscribedSignals = new();
    private readonly HashSet<SignalDefinition> _fatFieldDefaultsInitialized = new();

    private static readonly bool FatFieldUxClassHandlerRegistered = RegisterFatFieldUxClassHandler();

    private static bool RegisterFatFieldUxClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(FatFieldUxClassLoaded));
        return true;
    }

    private static void FatFieldUxClassLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._fatFieldUxInstalled)
            return;

        window._fatFieldUxInstalled = true;
        window.PropertyChanged += window.FatFieldUxWindow_PropertyChanged;
        window.Closed += window.FatFieldUxWindow_Closed;

        // The existing CommandPanel partial installs itself at ContextIdle. Run after it
        // so this additive layer can use the same panel shell and shared command device.
        window.Dispatcher.BeginInvoke(
            new Action(window.InstallFatFieldUx),
            DispatcherPriority.ApplicationIdle);
    }

    private void InstallFatFieldUx()
    {
        InstallFatStopLayout();
        InstallFatCommandFailureShout();
        RefreshFatFieldCommandSubscriptions();
    }

    private void FatFieldUxWindow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedIed))
            return;

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                InstallFatStopLayout();
                InstallFatCommandFailureShout();
                RefreshFatFieldCommandSubscriptions();
            }),
            DispatcherPriority.ApplicationIdle);
    }

    private void FatFieldUxWindow_Closed(object? sender, EventArgs e)
    {
        PropertyChanged -= FatFieldUxWindow_PropertyChanged;
        Closed -= FatFieldUxWindow_Closed;
        DetachFatFieldCommandDevice();
        _fatCommandFailureShoutTimer?.Stop();
        if (_fatCommandFailureShout != null)
            _fatCommandFailureShout.IsOpen = false;
    }

    /// <summary>
    /// Keep the critical Stop action in its own reserved Grid column. The surrounding
    /// action strip is compacted, but the existing Stop button instance is moved rather
    /// than recreated so its command binding, click handler and lifecycle semantics stay
    /// exactly the same.
    /// </summary>
    private void InstallFatStopLayout()
    {
        if (_fatStopLayoutInstalled)
            return;

        var stop = FindFatCommandVisualChildren<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Stop", StringComparison.Ordinal));
        if (stop?.Parent is not StackPanel actionPanel || actionPanel.Parent is not Grid headerGrid)
            return;
        if (Grid.GetColumn(actionPanel) != 1 || headerGrid.ColumnDefinitions.Count < 2)
            return;

        foreach (var button in actionPanel.Children.OfType<Button>())
        {
            button.Padding = new Thickness(9, 6, 9, 6);
            button.Margin = new Thickness(0, 0, 4, 0);
            button.MinHeight = 30;
        }

        actionPanel.Children.Remove(stop);
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(stop, 2);
        stop.Padding = new Thickness(10, 6, 10, 6);
        stop.Margin = new Thickness(7, 0, 0, 0);
        stop.MinWidth = 70;
        stop.MinHeight = 30;
        stop.HorizontalAlignment = HorizontalAlignment.Right;
        stop.ToolTip = "Stop this IED FAT session and seal its current evidence journal.";
        headerGrid.Children.Add(stop);

        _fatStopLayoutInstalled = true;
    }

    /// <summary>
    /// Prominent non-modal command failure feedback. It is intentionally a Popup so a
    /// rejected/failed command cannot be hidden by a long signal grid or horizontal
    /// scrolling. It auto-dismisses after five seconds and never blocks the operator.
    /// </summary>
    private void InstallFatCommandFailureShout()
    {
        if (_fatCommandFailureShout != null || _fatCommandPanelShell == null)
            return;

        _fatCommandFailureShoutText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.0,
            FontWeight = FontWeights.SemiBold,
            Foreground = FatCommandBrush("#8A1C2A"),
            MaxWidth = 620
        };

        var shell = new Border
        {
            Background = FatCommandBrush("#FFF1F2"),
            BorderBrush = FatCommandBrush("#E45D68"),
            BorderThickness = new Thickness(1.25),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(13, 9, 13, 9),
            Effect = null,
            Child = _fatCommandFailureShoutText
        };

        _fatCommandFailureShout = new Popup
        {
            PlacementTarget = _fatCommandPanelShell,
            Placement = PlacementMode.Top,
            VerticalOffset = -7,
            AllowsTransparency = true,
            StaysOpen = true,
            IsHitTestVisible = false,
            Child = shell
        };

        _fatCommandFailureShoutTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _fatCommandFailureShoutTimer.Tick += (_, _) => HideFatCommandFailureShout();
    }

    private void RefreshFatFieldCommandSubscriptions()
    {
        var device = (Owner as MainWindow)?.ResolveIoFatCommandDevice(SelectedIed) ?? _fatCommandDevice;
        if (!ReferenceEquals(_fatFieldCommandDevice, device))
        {
            DetachFatFieldCommandDevice();
            _fatFieldCommandDevice = device;
            if (_fatFieldCommandDevice != null)
                _fatFieldCommandDevice.CommandSignals.CollectionChanged += FatFieldCommandSignals_CollectionChanged;
        }

        if (_fatFieldCommandDevice == null)
            return;

        foreach (var signal in _fatFieldCommandDevice.CommandSignals)
            AttachFatFieldSignal(signal);
    }

    private void DetachFatFieldCommandDevice()
    {
        if (_fatFieldCommandDevice != null)
            _fatFieldCommandDevice.CommandSignals.CollectionChanged -= FatFieldCommandSignals_CollectionChanged;
        foreach (var signal in _fatFieldSubscribedSignals)
            signal.PropertyChanged -= FatFieldSignal_PropertyChanged;
        _fatFieldSubscribedSignals.Clear();
        _fatFieldCommandDevice = null;
    }

    private void FatFieldCommandSignals_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var signal in e.OldItems.OfType<SignalDefinition>())
            {
                signal.PropertyChanged -= FatFieldSignal_PropertyChanged;
                _fatFieldSubscribedSignals.Remove(signal);
            }
        }

        if (e.NewItems != null)
        {
            foreach (var signal in e.NewItems.OfType<SignalDefinition>())
                AttachFatFieldSignal(signal);
        }
    }

    private void AttachFatFieldSignal(SignalDefinition signal)
    {
        // FAT field default: both IEC 61850 Check bits start enabled. Apply this only the
        // first time a shared SignalDefinition enters this workspace; a later operator
        // toggle is preserved when switching IEDs or refreshing the command collection.
        if (_fatFieldDefaultsInitialized.Add(signal))
        {
            signal.ControlInterlockCheck = true;
            signal.ControlSynchroCheck = true;
        }

        if (_fatFieldSubscribedSignals.Add(signal))
            signal.PropertyChanged += FatFieldSignal_PropertyChanged;
    }

    private void FatFieldSignal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SignalDefinition signal || e.PropertyName != nameof(SignalDefinition.ControlLastResult))
            return;

        var result = signal.ControlLastResult;
        if (!IsFatCommandFailureResult(result))
            return;

        Dispatcher.BeginInvoke(
            new Action(() => ShowFatCommandFailureShout(signal, result)),
            DispatcherPriority.Send);
    }

    private void ShowFatCommandFailureShout(SignalDefinition signal, string result)
    {
        InstallFatCommandFailureShout();
        if (_fatCommandFailureShout == null || _fatCommandFailureShoutText == null)
            return;

        var reason = NormalizeFatCommandFailureReason(result);
        var identity = string.IsNullOrWhiteSpace(signal.Name) ? signal.ObjectReference : signal.Name;
        _fatCommandFailureShoutText.Text = $"COMMAND FAILED  •  {identity}  •  {reason}";
        _fatCommandFailureShout.IsOpen = true;
        _fatCommandFailureShoutTimer?.Stop();
        _fatCommandFailureShoutTimer?.Start();
    }

    private void HideFatCommandFailureShout()
    {
        _fatCommandFailureShoutTimer?.Stop();
        if (_fatCommandFailureShout != null)
            _fatCommandFailureShout.IsOpen = false;
    }

    private static bool IsFatCommandFailureResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return false;

        var text = result.Trim();
        return text.Contains("rejected", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("not accepted", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("not sent", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFatCommandFailureReason(string result)
    {
        var text = result.Trim();
        foreach (var prefix in new[] { "Command failed:", "Command rejected:", "Confirmation rejected:" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..].Trim();
                break;
            }
        }

        return string.IsNullOrWhiteSpace(text) ? "IED did not accept the command." : text;
    }
}
