using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;

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
    private readonly HashSet<string> _controlModelPreloadAttempts = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _controlModelPreloadGate = new(1, 1);

    private DispatcherTimer? _commandPanelUxTimer;
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
        Closed += (_, _) => _commandPanelUxTimer?.Stop();

        _commandPanelUxTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(2500)
        };
        _commandPanelUxTimer.Tick += CommandPanelUxTimer_Tick;
        _commandPanelUxTimer.Start();

        ApplyCommandPanelVisualTweaks();
        _ = PreloadControlModelsAsync();
    }

    private async void CommandPanelUxTimer_Tick(object? sender, EventArgs e)
    {
        ApplyCommandPanelVisualTweaks();
        await PreloadControlModelsAsync();
    }

    private void ApplyCommandPanelVisualTweaks()
    {
        // Only attach stable bindings to realized controls. Do not mutate row heights,
        // columns, or child visibility after the Expander has painted; that was the
        // source of the first-open layout flicker.
        var commandGrid = FindVisualChildren<DataGrid>(this).FirstOrDefault(IsCommandDataGrid);
        if (commandGrid == null)
            return;

        foreach (var column in commandGrid.Columns)
        {
            if ((column.Header?.ToString() ?? string.Empty)
                .Equals("Control model", StringComparison.OrdinalIgnoreCase))
            {
                ConfigureControlModelColumn(column);
            }
        }

        foreach (var button in FindVisualChildren<Button>(commandGrid))
            ConfigureCommandPanelButton(button);
    }

    private static bool IsCommandDataGrid(DataGrid grid)
    {
        var headers = grid.Columns
            .Select(column => column.Header?.ToString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return headers.Contains("Control model") && headers.Contains("CDC / Type") && headers.Contains("Control");
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
            return;

        try
        {
            foreach (var device in Devices.Where(device => device.IsConnected && device.SelectedControlSignalCount > 0))
            {
                // One MMS association is serialized. Do not queue background ctlModel
                // inspection while an operator command owns the session.
                if (device.CommandSignals.Any(signal => signal.ControlIsBusy))
                    continue;

                var candidates = device.Signals
                    .Where(signal => signal.IsSelected && signal.IsValidControlObject)
                    .Where(signal => !signal.ControlModelResolved)
                    .Where(signal => _controlModelPreloadAttempts.Add(ControlModelAttemptKey(device, signal)))
                    .ToArray();

                if (candidates.Length == 0)
                {
                    device.RefreshCommandSignalProjection();
                    continue;
                }

                using var throttle = new SemaphoreSlim(1, 1);
                await Task.WhenAll(candidates.Select(async signal =>
                {
                    await throttle.WaitAsync(_applicationCancellation.Token);
                    signal.ControlIsBusy = true;
                    try
                    {
                        var capabilities = await _runtime.InspectControlAsync(
                            device.DeviceId,
                            signal,
                            _applicationCancellation.Token);

                        signal.ControlCurrentValue = capabilities.CurrentValue;
                        signal.ControlLastResult = capabilities.SupportsOperate
                            ? string.Empty
                            : "Not available";
                    }
                    catch (OperationCanceledException)
                    {
                        // Application shutdown or device removal.
                    }
                    catch (Exception ex)
                    {
                        // SignalDefinition parses ctlModel evidence from this message. In
                        // particular, ctlModel=StatusOnly resolves the row as read-only.
                        signal.ControlLastResult = $"Control model unavailable: {ex.Message}";
                    }
                    finally
                    {
                        signal.ControlIsBusy = false;
                        throttle.Release();
                    }
                }));

                device.RefreshCommandSignalProjection();
                RebuildControlFeedbackIndex(device);
            }
        }
        finally
        {
            _controlModelPreloadGate.Release();
        }
    }

    private static string ControlModelAttemptKey(Iec61850MonitorDevice device, SignalDefinition signal)
        => $"{device.DeviceId}|{RuntimeHelpers.GetHashCode(signal)}|{NormalizeReference(signal.ObjectReference)}";

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
