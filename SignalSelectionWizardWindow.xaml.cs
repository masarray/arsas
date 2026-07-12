using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class SignalSelectionWizardWindow : Window, INotifyPropertyChanged
{
    private readonly Iec61850MonitorDevice _device;
    private readonly HashSet<string> _originalSelection;
    private readonly DispatcherTimer _filterTimer;
    private readonly Dictionary<string, string> _columnFilters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WeakReference<TextBox>> _filterTextBoxes = new(StringComparer.OrdinalIgnoreCase);
    private string _globalFilter = string.Empty;
    private string _quickFilter = "All";
    private bool _accepted;
    private bool _bulkSelectionUpdate;
    private string _visibleCountText = "0 visible";
    private bool? _visibleSelectionState;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICollectionView SignalsView { get; }
    public string DialogTitle => $"IEC 61850 Signal Selection — {_device.Name} — {_device.EndpointText}";
    public string RestoreMessage { get; }
    public string SelectionCountText => _device.SelectedControlSignalCount > 0
        ? $"{_device.SelectedSignalCount:N0} selected • {_device.SelectedLiveSignalCount:N0} live • {_device.SelectedControlSignalCount:N0} control"
        : $"{_device.SelectedSignalCount:N0} selected";
    public string VisibleCountText => _visibleCountText;
    public bool? VisibleSelectionState => _visibleSelectionState;
    public string ActiveFiltersText
    {
        get
        {
            var count = _columnFilters.Count(item => Tokenize(item.Value).Length > 0) +
                        (Tokenize(_globalFilter).Length == 0 ? 0 : 1) +
                        (_quickFilter.Equals("All", StringComparison.OrdinalIgnoreCase) ? 0 : 1);
            return count == 1 ? "1 active filter" : $"{count} active filters";
        }
    }
    public Visibility FilterStatusVisibility => HasActiveFilters ? Visibility.Visible : Visibility.Collapsed;
    private bool HasActiveFilters => Tokenize(_globalFilter).Length > 0 ||
                                     !_quickFilter.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                                     _columnFilters.Any(item => Tokenize(item.Value).Length > 0);

    public SignalSelectionWizardWindow(Iec61850MonitorDevice device, int restoredSelectionCount)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _originalSelection = device.Signals
            .Where(signal => signal.IsSelected)
            .Select(signal => NormalizeReference(signal.ObjectReference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RestoreMessage = restoredSelectionCount > 0
            ? $"Restored {restoredSelectionCount} previous selection(s) for this IED."
            : "No saved selection was found.";

        SignalsView = CollectionViewSource.GetDefaultView(device.Signals);
        SignalsView.Filter = FilterSignal;
        ApplyDefaultSortDescriptions();

        foreach (var signal in device.Signals)
        {
            signal.DisplayReference = Iec61850MonitorPoint.StripIedNamePrefix(signal.ObjectReference, device.Name);
            signal.PropertyChanged += Signal_PropertyChanged;
        }

        _filterTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            SignalsView.Refresh();
            RefreshViewState();
        };

        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) =>
        {
            SignalsView.Refresh();
            RefreshViewState();
        };
    }

    private bool FilterSignal(object item)
    {
        if (item is not SignalDefinition signal)
            return false;

        if (signal.IsControlSignal && !signal.IsValidControlObject)
            return false;

        if (!MatchesQuickFilter(signal))
            return false;

        if (!MatchesTokens(BuildGlobalSearchText(signal), Tokenize(_globalFilter)))
            return false;

        foreach (var (key, rawFilter) in _columnFilters)
        {
            var field = GetFilterField(signal, key);
            if (!MatchesTokens(field, Tokenize(rawFilter)))
                return false;
        }

        return true;
    }

    private static string BuildGlobalSearchText(SignalDefinition signal)
        => string.Join(" ", new[]
        {
            signal.DisplayReference,
            signal.ObjectReference,
            signal.Name,
            signal.LogicalNode,
            signal.LogicalNodeClass,
            signal.FunctionalConstraint,
            signal.DataType,
            signal.ControlCdc,
            signal.Category,
            signal.ReportPlan,
            signal.ControlActionLabel
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string GetFilterField(SignalDefinition signal, string key)
        => key switch
        {
            "Signal" => signal.Name,
            "LogicalNode" => $"{signal.LogicalNode} {signal.LogicalNodeClass}",
            "Telegram" => $"{signal.DisplayReference} {signal.ObjectReference}",
            "FC" => signal.FunctionalConstraint,
            "Type" => $"{signal.DataType} {signal.ControlCdc}",
            "Category" => signal.Category,
            "Acquisition" => signal.ReportPlan,
            _ => string.Empty
        };

    private bool MatchesQuickFilter(SignalDefinition signal)
        => _quickFilter switch
        {
            "Live" => !signal.IsControlSignal,
            "Control" => signal.IsValidControlObject,
            "Selected" => signal.IsSelected,
            _ => true
        };

    private static bool MatchesTokens(string? field, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return true;
        var value = field ?? string.Empty;
        return tokens.All(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] Tokenize(string? text)
        => Regex.Matches(text ?? string.Empty, @"[\p{L}\p{N}_]+")
            .Select(match => match.Value)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();

    private void GlobalFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        _globalFilter = sender is TextBox textBox ? textBox.Text ?? string.Empty : string.Empty;
        ScheduleFilterRefresh();
    }

    private void ColumnFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.Tag is not string key)
            return;

        _columnFilters[key] = textBox.Text ?? string.Empty;
        _filterTextBoxes[key] = new WeakReference<TextBox>(textBox);
        ScheduleFilterRefresh();
    }

    private void ScheduleFilterRefresh()
    {
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void QuickFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected || selected.Tag is not string key)
            return;

        _quickFilter = key;
        selected.IsChecked = true;
        foreach (var button in new[] { QuickAllButton, QuickLiveButton, QuickControlButton, QuickSelectedButton })
        {
            if (!ReferenceEquals(button, selected))
                button.IsChecked = false;
        }

        ScheduleFilterRefresh();
    }

    private void ClearGlobalFilter_Click(object sender, RoutedEventArgs e)
    {
        GlobalSearchTextBox.Clear();
        GlobalSearchTextBox.Focus();
    }

    private void ClearColumnFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key)
            return;

        _columnFilters.Remove(key);
        if (_filterTextBoxes.TryGetValue(key, out var weak) && weak.TryGetTarget(out var textBox))
            textBox.Clear();
        else
            ScheduleFilterRefresh();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _filterTimer.Stop();
        _globalFilter = string.Empty;
        _quickFilter = "All";
        GlobalSearchTextBox.Clear();
        QuickAllButton.IsChecked = true;
        QuickLiveButton.IsChecked = false;
        QuickControlButton.IsChecked = false;
        QuickSelectedButton.IsChecked = false;
        _columnFilters.Clear();
        foreach (var weak in _filterTextBoxes.Values)
        {
            if (weak.TryGetTarget(out var textBox))
                textBox.Clear();
        }
        SignalsView.Refresh();
        RefreshViewState();
    }

    private void FilterTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (e.Key == Key.Escape)
        {
            textBox.Clear();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Down)
        {
            ApplyPendingFilterImmediately();
            FocusFirstVisibleSignal();
            e.Handled = true;
        }
    }

    private void GlobalSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            GlobalSearchTextBox.Clear();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Down)
        {
            ApplyPendingFilterImmediately();
            FocusFirstVisibleSignal();
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            GlobalSearchTextBox.Focus();
            GlobalSearchTextBox.SelectAll();
            e.Handled = true;
        }
    }

    private void SignalsGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) != null ||
            FindAncestor<TextBox>(e.OriginalSource as DependencyObject) != null ||
            FindAncestor<ScrollBar>(e.OriginalSource as DependencyObject) != null ||
            FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject) != null)
        {
            return;
        }

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not SignalDefinition signal)
            return;

        SignalsGrid.SelectedItem = signal;
        SignalsGrid.CurrentItem = signal;
        row.Focus();
        signal.IsSelected = !signal.IsSelected;
        e.Handled = true;
    }

    private void SignalsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            SetVisibleSelection(true);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.A)
        {
            SetVisibleSelection(false);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && SignalsGrid.CurrentItem is SignalDefinition signal)
        {
            signal.IsSelected = !signal.IsSelected;
            e.Handled = true;
        }
    }

    private void SignalsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        var sortPath = e.Column.SortMemberPath;
        if (string.IsNullOrWhiteSpace(sortPath))
            return;

        e.Handled = true;
        var nextDirection = e.Column.SortDirection switch
        {
            null => ListSortDirection.Ascending,
            ListSortDirection.Ascending => ListSortDirection.Descending,
            _ => (ListSortDirection?)null
        };

        using (SignalsView.DeferRefresh())
        {
            SignalsView.SortDescriptions.Clear();
            foreach (var column in SignalsGrid.Columns)
                column.SortDirection = null;

            if (nextDirection.HasValue)
            {
                e.Column.SortDirection = nextDirection;
                SignalsView.SortDescriptions.Add(new SortDescription(sortPath, nextDirection.Value));
            }
            else
            {
                ApplyDefaultSortDescriptions();
            }
        }

        RefreshViewState();
    }

    private void VisibleSelectionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox)
            return;

        // An indeterminate header means "some visible rows are selected"; clicking it selects all.
        SetVisibleSelection(VisibleSelectionState != true);
        e.Handled = true;
    }

    private void Signal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SignalDefinition.IsSelected) || _bulkSelectionUpdate)
            return;

        Raise(nameof(SelectionCountText));
        Raise(nameof(VisibleCountText));
        if (_quickFilter.Equals("Selected", StringComparison.OrdinalIgnoreCase))
            ScheduleFilterRefresh();
        else
            RefreshViewState();
    }

    private void SetVisibleSelection(bool isSelected)
    {
        var visibleSignals = SignalsView.Cast<object>()
            .OfType<SignalDefinition>()
            .ToArray();

        _bulkSelectionUpdate = true;
        _device.BeginBulkSignalSelection();
        try
        {
            foreach (var signal in visibleSignals)
                signal.IsSelected = isSelected;
        }
        finally
        {
            _bulkSelectionUpdate = false;
            _device.EndBulkSignalSelection();
        }

        Raise(nameof(SelectionCountText));
        RefreshViewState();
    }

    private void RefreshViewState()
    {
        if (!IsLoaded)
            return;

        var visibleCount = 0;
        var selectedCount = 0;
        foreach (var signal in SignalsView.Cast<object>().OfType<SignalDefinition>())
        {
            visibleCount++;
            if (signal.IsSelected)
                selectedCount++;
        }

        _visibleCountText = $"{visibleCount:N0} visible of {_device.SignalCount:N0} • {selectedCount:N0} selected";
        _visibleSelectionState = visibleCount == 0
            ? false
            : selectedCount == 0
                ? false
                : selectedCount == visibleCount
                    ? true
                    : null;

        Raise(nameof(VisibleCountText));
        Raise(nameof(VisibleSelectionState));
        Raise(nameof(ActiveFiltersText));
        Raise(nameof(FilterStatusVisibility));
    }

    private void ApplyPendingFilterImmediately()
    {
        _filterTimer.Stop();
        SignalsView.Refresh();
        RefreshViewState();
    }

    private void FocusFirstVisibleSignal()
    {
        var first = SignalsView.Cast<object>().OfType<SignalDefinition>().FirstOrDefault();
        if (first == null)
            return;

        SignalsGrid.SelectedItem = first;
        SignalsGrid.CurrentItem = first;
        SignalsGrid.ScrollIntoView(first);
        SignalsGrid.UpdateLayout();
        (SignalsGrid.ItemContainerGenerator.ContainerFromItem(first) as DataGridRow)?.Focus();
    }

    private void ApplyDefaultSortDescriptions()
    {
        SignalsView.SortDescriptions.Add(new SortDescription(nameof(SignalDefinition.SortPriority), ListSortDirection.Ascending));
        SignalsView.SortDescriptions.Add(new SortDescription(nameof(SignalDefinition.LogicalNode), ListSortDirection.Ascending));
        SignalsView.SortDescriptions.Add(new SortDescription(nameof(SignalDefinition.Name), ListSortDirection.Ascending));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _accepted = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        RestoreOriginalSelection();
        DialogResult = false;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _filterTimer.Stop();
        foreach (var signal in _device.Signals)
            signal.PropertyChanged -= Signal_PropertyChanged;
        if (!_accepted)
            RestoreOriginalSelection();
    }

    private void RestoreOriginalSelection()
    {
        _device.BeginBulkSignalSelection();
        try
        {
            foreach (var signal in _device.Signals)
                signal.IsSelected = _originalSelection.Contains(NormalizeReference(signal.ObjectReference));
        }
        finally
        {
            _device.EndBulkSignalSelection();
        }
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T match)
                return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.').Replace("..", ".").ToLowerInvariant();

    private void Raise(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
