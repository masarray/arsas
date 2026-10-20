using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private sealed class Marker { }

    private sealed class TactileButtonState
    {
        public Transform? OriginalTransform { get; set; }
        public Point OriginalOrigin { get; set; }
        public bool IsPressed { get; set; }
    }

    private sealed class CommandButtonEnabledConverter : IMultiValueConverter
    {
        public static CommandButtonEnabledConverter Instance { get; } = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var busy = values.ElementAtOrDefault(0) is true;
            var supportsOperate = values.ElementAtOrDefault(1) is true;
            // Fast workflow is always armed. Runtime ctlModel/type validation remains the
            // authority, while Open/Close gains an explicit row-level confirmation step.
            return supportsOperate && !busy;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }

    private sealed class ControlModelColumnConverter : IValueConverter
    {
        public static ControlModelColumnConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value?.ToString()?.Trim() ?? string.Empty;
            if (text.Contains("status only", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("statusonly", StringComparison.OrdinalIgnoreCase))
            {
                return "Status only";
            }

            if ((text.Contains("select before operate", StringComparison.OrdinalIgnoreCase) || text.Contains("SBO", StringComparison.OrdinalIgnoreCase)) && text.Contains("enhanced", StringComparison.OrdinalIgnoreCase)) return "SBO • Enhanced security";
            if (text.Contains("select before operate", StringComparison.OrdinalIgnoreCase) || text.Contains("SBO", StringComparison.OrdinalIgnoreCase)) return "SBO • Normal security";
            if ((text.Contains("direct operate", StringComparison.OrdinalIgnoreCase) || text.Contains("(DO)", StringComparison.OrdinalIgnoreCase)) && text.Contains("enhanced", StringComparison.OrdinalIgnoreCase)) return "Direct • Enhanced security";
            if (text.Contains("direct operate", StringComparison.OrdinalIgnoreCase) || text.Contains("(DO)", StringComparison.OrdinalIgnoreCase)) return "Direct • Normal security";

            if (text.Contains("auto-detect", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(text))
            {
                return "Reading…";
            }

            if (text.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                return "Not available";

            return text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    private readonly ConditionalWeakTable<Button, Marker> _configuredCommandButtons = new();
    private readonly ConditionalWeakTable<Button, TactileButtonState> _tactileButtonStates = new();
    private readonly ConditionalWeakTable<SignalDefinition, Marker> _controlSafetyDefaultsApplied = new();
    private readonly HashSet<string> _controlModelPreloadAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _controlModelPreloadGate = new(1, 1);
    private bool _controlModelPreloadPending;

    private readonly HashSet<DataGrid> _configuredCommandGrids = new();
    private readonly ConditionalWeakTable<DataGridRow, Marker> _configuredCommandRows = new();
    private Button? _pressedTactileButton;
    private bool _commandPanelUxInstalled;

    private void InstallCommandPanelUx()
    {
        if (_commandPanelUxInstalled)
            return;

        _commandPanelUxInstalled = true;

        AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(TactileButton_PreviewMouseLeftButtonDown), true);
        AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(TactileButton_PreviewMouseLeftButtonUp), true);

        Deactivated += (_, _) => ReleasePressedTactileButton();

        // A workspace navigation or 2.5-second timer is not an IEC 61850
        // control lifecycle event. Initialize once and react only to an
        // actual row becoming realized or selected IED changing.
        _ = PreloadControlModelsAsync();
    }

    private void InstallCommandGridBehavior(DataGrid? grid)
    {
        if (grid == null || !_configuredCommandGrids.Add(grid))
            return;

        foreach (var column in grid.Columns)
        {
            if (string.Equals(column.Header?.ToString(), "Control model", StringComparison.OrdinalIgnoreCase))
                ConfigureControlModelColumn(column);
        }

        grid.LoadingRow += CommandGrid_LoadingRow;
        // Loaded can fire after the DataGrid has already generated its first
        // containers. Attach the initial realized rows once; future recycled
        // rows use LoadingRow. No periodic scan of the window or other tabs.
        foreach (var row in FindVisualChildren<DataGridRow>(grid))
            AttachCommandRow(row);
    }

    private void CommandGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        => AttachCommandRow(e.Row);

    private void AttachCommandRow(DataGridRow row)
    {
        if (_configuredCommandRows.TryGetValue(row, out _))
            return;
        _configuredCommandRows.Add(row, new Marker());
        row.Loaded += CommandGridRow_Loaded;
        if (row.IsLoaded)
            ApplyCommandRowVisuals(row);
    }

    private void CommandGridRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DataGridRow row)
            ApplyCommandRowVisuals(row);
    }

    private void ApplyCommandRowVisuals(DataGridRow row)
    {
        if (row.DataContext is SignalDefinition signal)
            EnsureDefaultControlSafetyChecks(signal);
        foreach (var button in FindVisualChildren<Button>(row))
            ConfigureCommandPanelButton(button);
    }

    private static void ConfigureControlModelColumn(DataGridColumn column)
    {
        if (column is not DataGridTextColumn textColumn)
            return;

        if (textColumn.Binding is Binding existing && existing.Converter is ControlModelColumnConverter)
            return;

        textColumn.Binding = new Binding(nameof(SignalDefinition.ControlModelText))
        {
            Converter = ControlModelColumnConverter.Instance,
            Mode = BindingMode.OneWay
        };

        var elementStyle = new Style(typeof(TextBlock));
        elementStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        elementStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        elementStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
            new Binding(nameof(SignalDefinition.ControlModelText))));
        textColumn.ElementStyle = elementStyle;
    }

    private void ConfigureCommandPanelButton(Button button)
    {
        if (button.DataContext is not SignalDefinition signal)
            return;

        EnsureDefaultControlSafetyChecks(signal);

        var content = button.Content?.ToString()?.Trim() ?? string.Empty;
        if (content.Equals("Technical details", StringComparison.OrdinalIgnoreCase) ||
            content.Equals("Not available", StringComparison.OrdinalIgnoreCase))
        {
            ConfigureTechnicalDetailsSlot(button, signal);
            return;
        }

        if (content.Equals("Details", StringComparison.OrdinalIgnoreCase))
        {
            button.Visibility = signal.ControlModelResolved && !signal.IsReadOnlyControl
                ? Visibility.Visible
                : Visibility.Collapsed;
            return;
        }

        if (!IsCommandActionButton(content))
            return;

        if (content is "Open" or "Close" or "True" or "False")
            button.MinWidth = 74;
        else
            button.MinWidth = Math.Max(button.MinWidth, 66);

        button.MinHeight = Math.Max(button.MinHeight, 32);
        button.VerticalAlignment = VerticalAlignment.Center;

        if (_configuredCommandButtons.TryGetValue(button, out _))
            return;

        // Position Open/Close have an intentional typed preflight in XAML:
        // ControlCanInspectPosition permits a *read-only* inspection before
        // a control has received verified CDC evidence. Do not overwrite its
        // IsEnabled binding with ControlSupportsOperate (that would disable
        // the first-click preflight and create a dead control row).
        if (content is "Open" or "Close")
        {
            // Position actions retain the typed preflight defined in XAML.
            // ctlModel is NOT a command action; its dedicated XAML style
            // owns the persistent glyph, and its session-bound Enabled state
            // is never rewritten here.
            _configuredCommandButtons.Add(button, new Marker());
            return;
        }

        var enabledBinding = new MultiBinding
        {
            Converter = CommandButtonEnabledConverter.Instance,
            Mode = BindingMode.OneWay
        };
        enabledBinding.Bindings.Add(new Binding(nameof(SignalDefinition.ControlIsBusy)));
        enabledBinding.Bindings.Add(new Binding(nameof(SignalDefinition.ControlSupportsOperate)));
        BindingOperations.SetBinding(button, UIElement.IsEnabledProperty, enabledBinding);

        _configuredCommandButtons.Add(button, new Marker());
    }

    private void EnsureDefaultControlSafetyChecks(SignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _controlSafetyDefaultsApplied.GetValue(signal, current =>
        {
            // Safe command defaults are applied to the model once, not painted into XAML.
            // After this first initialization the operator remains free to clear either flag;
            // the periodic command-panel UX refresh must never force a user choice back on.
            current.ControlInterlockCheck = true;
            current.ControlSynchroCheck = true;
            return new Marker();
        });
    }

    private static bool IsCommandActionButton(string content)
        => content is "Open" or "Close" or "True" or "False" or "Raise" or "Lower" or "Set";

    private static void ConfigureTechnicalDetailsSlot(Button button, SignalDefinition signal)
    {
        if (signal.IsReadOnlyControl)
        {
            button.Content = "Not available";
            button.IsEnabled = false;
            button.IsHitTestVisible = false;
            button.Focusable = false;
            button.Cursor = Cursors.Arrow;
            button.Background = Brushes.Transparent;
            button.Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133));
            button.BorderThickness = new Thickness(0);
            button.Padding = new Thickness(0);
            return;
        }

        button.Content = "Technical details";
        button.ClearValue(UIElement.IsEnabledProperty);
        button.IsHitTestVisible = true;
        button.Focusable = true;
        button.Cursor = Cursors.Hand;
        button.ClearValue(Control.BackgroundProperty);
        button.ClearValue(Control.ForegroundProperty);
        button.ClearValue(Control.BorderThicknessProperty);
        button.ClearValue(Control.PaddingProperty);
    }

    private async Task PreloadControlModelsAsync()
    {
        if (!await _controlModelPreloadGate.WaitAsync(0))
        {
            // Connection/selection changes during an active sweep require a
            // second pass, not a competing MMS read queue or a dropped event.
            _controlModelPreloadPending = true;
            return;
        }

        try
        {
            do
            {
                _controlModelPreloadPending = false;
                foreach (var device in Devices.Where(device => device.IsConnected && device.SelectedControlSignalCount > 0))
            {
                foreach (var signal in device.Signals.Where(signal => signal.IsSelected && signal.IsValidControlObject))
                    EnsureDefaultControlSafetyChecks(signal);

                // One MMS association is serialized. Do not queue background ctlModel
                // inspection while an operator command owns the session.
                if (device.CommandSignals.Any(signal => signal.ControlCommandBusy))
                    continue;

                // Cache aliases of the same DataObject share one ctlModel probe.
                var candidates = Iec61850ControlIdentity.InspectionRepresentatives(device.Signals);

                if (candidates.Length == 0)
                    continue;

                foreach (var signal in candidates)
                {
                    // A single MMS association already serializes all reads.
                    // Never build a parallel queue of pending inspections.
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    if (!device.IsConnected || _applicationCancellation.IsCancellationRequested ||
                        device.Signals.Any(candidate => candidate.ControlCommandBusy))
                        break;
                    if (signal.ControlModelResolved || signal.ControlInspectionBusy || signal.ControlCommandBusy)
                        continue;
                    // Do not reserve a later target before it is actually
                    // admitted; if a command interrupts the sweep, remaining
                    // objects can still be inspected on the next real event.
                    if (!_controlModelPreloadAttempts.Add(ControlModelAttemptKey(device, signal)))
                        continue;
                    signal.ControlInspectionBusy = true;
                    try
                    {
                        var capabilities = await _runtime.InspectControlAsync(
                            device.DeviceId,
                            signal,
                            _applicationCancellation.Token);

                        if (!signal.ControlCommandBusy)
                        {
                            signal.ControlCurrentValue = capabilities.CurrentValue;
                            signal.ControlLastResult = capabilities.SupportsOperate
                                ? string.Empty
                                : "Not available";
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Application shutdown or device removal.
                    }
                    catch (Exception ex)
                    {
                        // SignalDefinition parses ctlModel evidence from this message. In
                        // particular, ctlModel=StatusOnly resolves the row as read-only.
                        if (!signal.ControlCommandBusy)
                        {
                            // Diagnostic prose must never grant or infer ctlModel.
                            signal.ControlLastResult = $"Control model unavailable: {ex.Message}";
                        }
                    }
                    finally
                    {
                        signal.ControlInspectionBusy = false;
                    }
                }

                    device.RefreshCommandSignalProjection();
                    RebuildControlFeedbackIndex(device);
                }
            }
            while (_controlModelPreloadPending && !_applicationCancellation.IsCancellationRequested);
        }
        finally
        {
            _controlModelPreloadGate.Release();
        }
    }

    private void ResetControlPreloadAttempts(Iec61850MonitorDevice device)
    {
        // The cache belongs to one association, not to the persisted device ID.
        // Reconnect must re-prove typed ctlModel and CDC on the new session.
        var prefix = device.DeviceId + "|";
        _controlModelPreloadAttempts.RemoveWhere(key =>
            key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string ControlModelAttemptKey(Iec61850MonitorDevice device, SignalDefinition signal)
        => $"{device.DeviceId}|{Iec61850ControlIdentity.Normalize(signal.ObjectReference)}";

    private void TactileButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var button = FindAncestorButton(e.OriginalSource as DependencyObject);
        if (button == null || !button.IsEnabled)
            return;

        ReleasePressedTactileButton();
        _pressedTactileButton = button;

        var state = _tactileButtonStates.GetValue(button, _ => new TactileButtonState());
        if (state.IsPressed)
            return;

        state.OriginalTransform = button.RenderTransform;
        state.OriginalOrigin = button.RenderTransformOrigin;
        state.IsPressed = true;

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(0.965, 0.965));
        transform.Children.Add(new TranslateTransform(0, 1.4));
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        button.RenderTransform = transform;
    }

    private void TactileButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => ReleasePressedTactileButton();

    private void ReleasePressedTactileButton()
    {
        var button = _pressedTactileButton;
        _pressedTactileButton = null;
        if (button == null || !_tactileButtonStates.TryGetValue(button, out var state) || !state.IsPressed)
            return;

        button.RenderTransform = state.OriginalTransform ?? Transform.Identity;
        button.RenderTransformOrigin = state.OriginalOrigin;
        state.IsPressed = false;
    }

    private static Button? FindAncestorButton(DependencyObject? current)
    {
        while (current != null)
        {
            if (current is Button button)
                return button;

            try
            {
                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
