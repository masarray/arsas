using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private sealed class FatCommandRowView
    {
        public required Border Container { get; init; }
        public required Grid Grid { get; init; }
        public required FrameworkElement Actions { get; set; }
    }

    private sealed class FatCommandTextConverter : IValueConverter
    {
        public static FatCommandTextConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value?.ToString()?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class FatCommandModelConverter : IValueConverter
    {
        public static FatCommandModelConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => FatCommandModelText(value?.ToString());

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    private sealed class FatCommandCanOperateConverter : IMultiValueConverter
    {
        public static FatCommandCanOperateConverter Instance { get; } = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var supportsOperate = values.ElementAtOrDefault(0) is true;
            var isBusy = values.ElementAtOrDefault(1) is true;
            return supportsOperate && !isBusy;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }

    private Border? _fatCommandPanelShell;
    private StackPanel? _fatCommandRows;
    private TextBlock? _fatCommandSummary;
    private FrameworkElement? _fatCommandEmptyState;
    private Iec61850MonitorDevice? _fatCommandDevice;
    private readonly HashSet<SignalDefinition> _fatCommandSubscribedSignals = new();
    private readonly Dictionary<SignalDefinition, FatCommandRowView> _fatCommandRowViews = new();
    private bool _fatCommandPanelLifecycleInstalled;

    // Register at class level rather than overriding OnInitialized. IoListTestingWindow
    // already owns an initialization override in another partial; FAT command UI must be
    // additive and must not compete with the existing workspace lifecycle.
    private static readonly bool FatCommandPanelClassHandlerRegistered = RegisterFatCommandPanelClassHandler();

    private static bool RegisterFatCommandPanelClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(FatCommandPanelClassLoaded));
        return true;
    }

    private static void FatCommandPanelClassLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._fatCommandPanelLifecycleInstalled)
            return;

        window._fatCommandPanelLifecycleInstalled = true;
        window.PropertyChanged += window.FatCommandPanelWindow_PropertyChanged;
        window.Closed += window.FatCommandPanelWindow_Closed;
        window.Dispatcher.BeginInvoke(new Action(async () =>
        {
            window.InstallFatCommandPanel();
            await window.RefreshFatCommandPanelAsync();
        }), DispatcherPriority.ContextIdle);
    }

    private void FatCommandPanelWindow_Closed(object? sender, EventArgs e)
    {
        PropertyChanged -= FatCommandPanelWindow_PropertyChanged;
        Closed -= FatCommandPanelWindow_Closed;
        DetachFatCommandDevice();
    }

    private void FatCommandPanelWindow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedIed))
            return;

        Dispatcher.BeginInvoke(new Action(async () => await RefreshFatCommandPanelAsync()), DispatcherPriority.Background);
    }

    private void InstallFatCommandPanel()
    {
        if (_fatCommandPanelShell != null)
            return;

        var fatGrid = FindFatCommandVisualChildren<DataGrid>(this)
            .FirstOrDefault(grid =>
                BindingOperations.GetBinding(grid, ItemsControl.ItemsSourceProperty)?.Path?.Path == "SelectedIed.TestPoints")
            ?? FindFatCommandVisualChildren<DataGrid>(this).FirstOrDefault();
        if (fatGrid?.Parent is not Grid hostGrid)
            return;

        // Keep the FAT evidence table as the flexible row. Controls get their own compact,
        // independently scrolling surface below it so a large I/O list remains usable.
        hostGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        hostGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = "IED COMMAND PANEL",
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = FatCommandBrush("#2F6FD6")
        });
        _fatCommandSummary = new TextBlock
        {
            Text = "Select a connected IED to load SCL/DataSet control objects.",
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 10.5,
            Foreground = FatCommandBrush("#697A90")
        };
        heading.Children.Add(_fatCommandSummary);
        header.Children.Add(heading);

        var refresh = FatCommandButton("Refresh values", "SoftButton");
        refresh.Padding = new Thickness(10, 6, 10, 6);
        refresh.Click += async (_, _) => await RefreshFatCommandPanelAsync();
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);

        _fatCommandRows = new StackPanel();
        var scroller = new ScrollViewer
        {
            Margin = new Thickness(0, 9, 0, 0),
            MaxHeight = 174,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _fatCommandRows
        };

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(scroller);

        _fatCommandPanelShell = new Border
        {
            Background = FatCommandBrush("#F7FAFF"),
            BorderBrush = FatCommandBrush("#D7E2F1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(11, 9, 11, 9),
            MaxHeight = 238,
            Child = content
        };

        Grid.SetRow(_fatCommandPanelShell, hostGrid.RowDefinitions.Count - 1);
        hostGrid.Children.Add(_fatCommandPanelShell);
    }

    private async Task RefreshFatCommandPanelAsync()
    {
        if (_fatCommandRows == null || _fatCommandSummary == null)
            return;

        if (Owner is not MainWindow engineeringWindow)
        {
            DetachFatCommandDevice();
            _fatCommandSummary.Text = "Engineering owner unavailable; control is disabled fail-closed.";
            SynchronizeFatCommandRows();
            return;
        }

        var device = engineeringWindow.ResolveIoFatCommandDevice(SelectedIed);
        AttachFatCommandDevice(device);
        SynchronizeFatCommandRows();
        if (device == null)
        {
            _fatCommandSummary.Text = "No shared Engineering IED is bound to the selected FAT device.";
            return;
        }

        if (!device.IsConnected)
        {
            _fatCommandSummary.Text = $"{device.Name} is not connected; command actions remain disabled.";
            return;
        }

        _fatCommandSummary.Text = $"{device.Name} · validating live ctlModel and shared process values…";
        try
        {
            await engineeringWindow.RefreshIoFatCommandValuesAsync(device);
            AttachFatCommandDevice(device);
            SynchronizeFatCommandRows();
        }
        catch (OperationCanceledException)
        {
            _fatCommandSummary.Text = $"{device.Name} · command refresh cancelled.";
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            _fatCommandSummary.Text = $"{device.Name} · command refresh unavailable: {ex.Message}";
        }
    }

    private bool AttachFatCommandDevice(Iec61850MonitorDevice? device)
    {
        if (ReferenceEquals(_fatCommandDevice, device))
            return false;

        DetachFatCommandDevice();
        _fatCommandDevice = device;
        if (_fatCommandDevice != null)
            _fatCommandDevice.CommandSignals.CollectionChanged += FatCommandSignals_CollectionChanged;
        return true;
    }

    private void DetachFatCommandDevice()
    {
        if (_fatCommandDevice != null)
            _fatCommandDevice.CommandSignals.CollectionChanged -= FatCommandSignals_CollectionChanged;

        foreach (var signal in _fatCommandSubscribedSignals)
            signal.PropertyChanged -= FatCommandSignal_PropertyChanged;
        _fatCommandSubscribedSignals.Clear();
        _fatCommandDevice = null;
    }

    private void FatCommandSignals_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(new Action(SynchronizeFatCommandRows), DispatcherPriority.Background);

    private void FatCommandSignal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SignalDefinition signal)
            return;

        // LIVE VALUE, result text and busy/enabled state are WPF bindings on the existing
        // row instance. Only a semantic action-layout transition needs to replace the small
        // action cell; the row and the rest of the panel remain untouched.
        if (e.PropertyName is nameof(SignalDefinition.ControlConfirmationPending)
            or nameof(SignalDefinition.ControlCdc)
            or nameof(SignalDefinition.ControlModelText)
            or nameof(SignalDefinition.ControlModelResolved))
        {
            Dispatcher.BeginInvoke(
                new Action(() => RefreshFatCommandActions(signal)),
                DispatcherPriority.Background);
        }
    }

    private void SynchronizeFatCommandRows()
    {
        if (_fatCommandRows == null || _fatCommandSummary == null)
            return;

        var device = _fatCommandDevice;
        var commands = device?.CommandSignals.ToArray() ?? Array.Empty<SignalDefinition>();
        var commandSet = commands.ToHashSet();

        foreach (var stale in _fatCommandRowViews.Keys.Where(signal => !commandSet.Contains(signal)).ToArray())
            RemoveFatCommandRow(stale);

        if (commands.Length == 0)
        {
            _fatCommandSummary.Text = device == null
                ? "No FAT command device selected."
                : $"{device.Name} · no operable control is proven by live ctlModel. Status-only controls remain read-only.";

            EnsureFatCommandEmptyState(device == null
                ? "No FAT command device selected."
                : "No command action is available. Controls appear only after live ctlModel proves Direct/SBO operation; StatusOnly and unsupported generic types stay fail-closed.");
            return;
        }

        RemoveFatCommandEmptyState();
        _fatCommandSummary.Text = $"{device!.Name} · {commands.Length} operable DataSet control(s) · shared Engineering command backend";

        for (var index = 0; index < commands.Length; index++)
        {
            var signal = commands[index];
            if (!_fatCommandRowViews.TryGetValue(signal, out var view))
            {
                signal.PropertyChanged += FatCommandSignal_PropertyChanged;
                _fatCommandSubscribedSignals.Add(signal);
                view = BuildFatCommandRow(signal);
                _fatCommandRowViews[signal] = view;
                _fatCommandRows.Children.Insert(Math.Min(index, _fatCommandRows.Children.Count), view.Container);
            }

            var currentIndex = _fatCommandRows.Children.IndexOf(view.Container);
            if (currentIndex >= 0 && currentIndex != index)
            {
                _fatCommandRows.Children.RemoveAt(currentIndex);
                _fatCommandRows.Children.Insert(Math.Min(index, _fatCommandRows.Children.Count), view.Container);
            }
        }
    }

    private void EnsureFatCommandEmptyState(string text)
    {
        if (_fatCommandRows == null)
            return;

        if (_fatCommandEmptyState is TextBlock existing)
        {
            existing.Text = text;
            if (!_fatCommandRows.Children.Contains(existing))
                _fatCommandRows.Children.Add(existing);
            return;
        }

        _fatCommandEmptyState = FatCommandEmptyText(text);
        _fatCommandRows.Children.Add(_fatCommandEmptyState);
    }

    private void RemoveFatCommandEmptyState()
    {
        if (_fatCommandRows == null || _fatCommandEmptyState == null)
            return;
        _fatCommandRows.Children.Remove(_fatCommandEmptyState);
        _fatCommandEmptyState = null;
    }

    private void RemoveFatCommandRow(SignalDefinition signal)
    {
        if (_fatCommandSubscribedSignals.Remove(signal))
            signal.PropertyChanged -= FatCommandSignal_PropertyChanged;

        if (!_fatCommandRowViews.Remove(signal, out var view) || _fatCommandRows == null)
            return;
        _fatCommandRows.Children.Remove(view.Container);
    }

    private FatCommandRowView BuildFatCommandRow(SignalDefinition signal)
    {
        var row = new Grid { MinWidth = 960, DataContext = signal };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.25, GridUnitType.Star), MinWidth = 210 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star), MinWidth = 82 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.72, GridUnitType.Star), MinWidth = 70 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star), MinWidth = 145 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star), MinWidth = 150 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star), MinWidth = 190 });

        var reference = FatCommandText(signal.ObjectReference, 11.0, FontWeights.SemiBold);
        reference.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        reference.ToolTip = signal.ObjectReference;
        AddFatCommandCell(row, reference, 0);

        var current = FatCommandText("—", 11.0, FontWeights.SemiBold);
        current.SetBinding(TextBlock.TextProperty, new Binding(nameof(SignalDefinition.ControlCurrentValue))
        {
            Source = signal,
            Mode = BindingMode.OneWay,
            Converter = FatCommandTextConverter.Instance
        });
        current.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(SignalDefinition.ControlLastResult))
        {
            Source = signal,
            Mode = BindingMode.OneWay
        });
        AddFatCommandCell(row, current, 1);

        var cdc = FatCommandText("—", 10.8, FontWeights.SemiBold);
        cdc.SetBinding(TextBlock.TextProperty, new Binding(nameof(SignalDefinition.ControlCdc))
        {
            Source = signal,
            Mode = BindingMode.OneWay,
            Converter = FatCommandTextConverter.Instance
        });
        AddFatCommandCell(row, cdc, 2);

        var model = FatCommandText("Reading…", 10.5, FontWeights.SemiBold);
        model.SetBinding(TextBlock.TextProperty, new Binding(nameof(SignalDefinition.ControlModelText))
        {
            Source = signal,
            Mode = BindingMode.OneWay,
            Converter = FatCommandModelConverter.Instance
        });
        model.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(SignalDefinition.ControlModelText))
        {
            Source = signal,
            Mode = BindingMode.OneWay
        });
        AddFatCommandCell(row, model, 3);
        AddFatCommandCell(row, BuildFatCommandChecks(signal), 4);

        var actions = BuildFatCommandActions(signal);
        AddFatCommandCell(row, actions, 5);

        var container = new Border
        {
            Background = Brushes.White,
            BorderBrush = FatCommandBrush("#E2E8F1"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Child = row
        };

        return new FatCommandRowView
        {
            Container = container,
            Grid = row,
            Actions = actions
        };
    }

    private void RefreshFatCommandActions(SignalDefinition signal)
    {
        if (!_fatCommandRowViews.TryGetValue(signal, out var view))
            return;

        var replacement = BuildFatCommandActions(signal);
        view.Grid.Children.Remove(view.Actions);
        AddFatCommandCell(view.Grid, replacement, 5);
        view.Actions = replacement;
    }

    private FrameworkElement BuildFatCommandChecks(SignalDefinition signal)
    {
        var panel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(FatCommandCheck("Interlock", signal, nameof(SignalDefinition.ControlInterlockCheck)));
        panel.Children.Add(FatCommandCheck("Sync", signal, nameof(SignalDefinition.ControlSynchroCheck)));
        panel.Children.Add(FatCommandCheck("Test", signal, nameof(SignalDefinition.ControlTestMode)));
        return panel;
    }

    private static CheckBox FatCommandCheck(string text, SignalDefinition signal, string propertyName)
    {
        var check = new CheckBox
        {
            Content = text,
            DataContext = signal,
            Margin = new Thickness(0, 0, 7, 0),
            FontSize = 9.6,
            VerticalAlignment = VerticalAlignment.Center
        };
        check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(propertyName)
        {
            Source = signal,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        return check;
    }

    private FrameworkElement BuildFatCommandActions(SignalDefinition signal)
    {
        var panel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

        if (signal.IsPositionControl)
        {
            if (signal.ControlConfirmationPending)
            {
                var confirm = FatCommandButton("Confirm", "PrimaryButton");
                BindFatCommandEnabled(confirm, signal);
                confirm.Click += async (_, _) => await ConfirmFatPositionControlAsync(signal);
                panel.Children.Add(confirm);

                var cancel = FatCommandButton("Cancel", "SoftButton");
                cancel.Margin = new Thickness(6, 0, 0, 0);
                cancel.Click += (_, _) => signal.ClearControlConfirmation();
                panel.Children.Add(cancel);
            }
            else
            {
                var open = FatCommandButton("Open", "CommandOpenButton");
                BindFatCommandEnabled(open, signal);
                open.Click += (_, _) => StageFatPositionControl(signal, "Open [01]", "Open");
                panel.Children.Add(open);

                var close = FatCommandButton("Close", "CommandCloseButton");
                close.Margin = new Thickness(6, 0, 0, 0);
                BindFatCommandEnabled(close, signal);
                close.Click += (_, _) => StageFatPositionControl(signal, "Closed [10]", "Close");
                panel.Children.Add(close);
            }
            return panel;
        }

        if (signal.IsRaiseOnlyControl)
        {
            panel.Children.Add(FatQuickCommandButton(signal, "Raise", "Raise", "PrimaryButton"));
            return panel;
        }
        if (signal.IsLowerOnlyControl)
        {
            panel.Children.Add(FatQuickCommandButton(signal, "Lower", "Lower", "SoftButton"));
            return panel;
        }
        if (signal.IsRaiseLowerControl)
        {
            panel.Children.Add(FatQuickCommandButton(signal, "Raise", "Raise", "PrimaryButton"));
            var lower = FatQuickCommandButton(signal, "Lower", "Lower", "SoftButton");
            lower.Margin = new Thickness(6, 0, 0, 0);
            panel.Children.Add(lower);
            return panel;
        }
        if (signal.IsBooleanControl)
        {
            panel.Children.Add(FatQuickCommandButton(signal, "True", "True", "CommandCloseButton"));
            var off = FatQuickCommandButton(signal, "False", "False", "CommandOpenButton");
            off.Margin = new Thickness(6, 0, 0, 0);
            panel.Children.Add(off);
            return panel;
        }
        if (signal.IsSetPointControl)
        {
            var target = new TextBox
            {
                Width = 88,
                Height = 29,
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = "Target value"
            };
            target.SetBinding(TextBox.TextProperty, new Binding(nameof(SignalDefinition.ControlSetPointText))
            {
                Source = signal,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            panel.Children.Add(target);

            var set = FatCommandButton("Set", "PrimaryButton");
            BindFatCommandEnabled(set, signal);
            set.Click += async (_, _) => await ExecuteFatQuickControlAsync(signal, signal.ControlSetPointText, "Set");
            panel.Children.Add(set);
            return panel;
        }

        panel.Children.Add(FatCommandEmptyText("No safe quick action"));
        return panel;
    }

    private Button FatQuickCommandButton(SignalDefinition signal, string label, string requestedValue, string styleKey)
    {
        var button = FatCommandButton(label, styleKey);
        BindFatCommandEnabled(button, signal);
        button.Click += async (_, _) => await ExecuteFatQuickControlAsync(signal, requestedValue, label);
        return button;
    }

    private static void BindFatCommandEnabled(Button button, SignalDefinition signal)
    {
        var binding = new MultiBinding
        {
            Converter = FatCommandCanOperateConverter.Instance,
            Mode = BindingMode.OneWay
        };
        binding.Bindings.Add(new Binding(nameof(SignalDefinition.ControlSupportsOperate))
        {
            Source = signal,
            Mode = BindingMode.OneWay
        });
        binding.Bindings.Add(new Binding(nameof(SignalDefinition.ControlIsBusy))
        {
            Source = signal,
            Mode = BindingMode.OneWay
        });
        BindingOperations.SetBinding(button, UIElement.IsEnabledProperty, binding);
    }

    private void StageFatPositionControl(SignalDefinition signal, string requestedValue, string actionLabel)
    {
        if (!signal.TryStageControlConfirmation(requestedValue, actionLabel, out var rejectionReason))
        {
            signal.ControlLastResult = $"Command rejected: {rejectionReason}.";
            return;
        }

        RefreshFatCommandActions(signal);
    }

    private async Task ConfirmFatPositionControlAsync(SignalDefinition signal)
    {
        if (Owner is not MainWindow engineeringWindow)
            return;
        if (!signal.TryClaimControlConfirmation(out var claim, out var rejectionReason) || claim == null)
        {
            signal.ControlLastResult = $"Command rejected: {rejectionReason}.";
            return;
        }

        RefreshFatCommandActions(signal);
        await engineeringWindow.ExecuteIoFatControlClaimAsync(signal, claim);
        RefreshFatCommandActions(signal);
    }

    private async Task ExecuteFatQuickControlAsync(SignalDefinition signal, string requestedValue, string actionLabel)
    {
        if (Owner is not MainWindow engineeringWindow)
            return;
        if (!signal.TryBeginDirectControlCommand(requestedValue, actionLabel, out var claim, out var rejectionReason) || claim == null)
        {
            signal.ControlLastResult = $"Command rejected: {rejectionReason}.";
            return;
        }

        await engineeringWindow.ExecuteIoFatControlClaimAsync(signal, claim);
    }

    private Button FatCommandButton(string text, string styleKey)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 29,
            MinWidth = 58,
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 10.2,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (TryFindResource(styleKey) is Style style)
            button.Style = style;
        return button;
    }

    private static void AddFatCommandCell(Grid row, FrameworkElement child, int column)
    {
        child.Margin = column == 0 ? new Thickness(0) : new Thickness(8, 0, 0, 0);
        child.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(child, column);
        row.Children.Add(child);
    }

    private static TextBlock FatCommandText(string text, double size, FontWeight weight)
        => new()
        {
            Text = string.IsNullOrWhiteSpace(text) ? "—" : text,
            FontSize = size,
            FontWeight = weight,
            Foreground = FatCommandBrush("#34465D"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static TextBlock FatCommandEmptyText(string text)
        => new()
        {
            Text = text,
            FontSize = 10.3,
            Foreground = FatCommandBrush("#75859A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 2, 4)
        };

    private static string FatCommandModelText(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Contains("select before operate", StringComparison.OrdinalIgnoreCase) || text.Contains("SBO", StringComparison.OrdinalIgnoreCase))
            return text.Contains("enhanced", StringComparison.OrdinalIgnoreCase) ? "SBO • Enhanced" : "SBO • Normal";
        if (text.Contains("direct", StringComparison.OrdinalIgnoreCase))
            return text.Contains("enhanced", StringComparison.OrdinalIgnoreCase) ? "Direct • Enhanced" : "Direct • Normal";
        if (text.Contains("status", StringComparison.OrdinalIgnoreCase))
            return "Status only";
        return string.IsNullOrWhiteSpace(text) ? "Reading…" : text;
    }

    private static Brush FatCommandBrush(string value)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));

    private static IEnumerable<T> FindFatCommandVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindFatCommandVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
