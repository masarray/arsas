using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Adds a compact tri-state header checkbox to the FAT TEST column without changing
/// the grid item source or the per-row TestEnabled contract. The header uses the same
/// editability gate as the row checkboxes and persists one bulk plan change.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool IoTestHeaderSelectionClassHandlerRegistered =
        RegisterIoTestHeaderSelectionClassHandler();

    private bool _ioTestHeaderSelectionInstallScheduled;
    private bool _ioTestHeaderSelectionInstalled;
    private CheckBox? _ioTestHeaderSelectionCheckBox;
    private IoTestIedPlan? _ioTestHeaderObservedIed;

    private static bool RegisterIoTestHeaderSelectionClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnIoTestHeaderSelectionWindowLoaded),
            handledEventsToo: true);
        return true;
    }

    private static void OnIoTestHeaderSelectionWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window ||
            window._ioTestHeaderSelectionInstalled ||
            window._ioTestHeaderSelectionInstallScheduled)
        {
            return;
        }

        window._ioTestHeaderSelectionInstallScheduled = true;
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                window._ioTestHeaderSelectionInstallScheduled = false;
                window.EnsureIoTestHeaderSelection();
            }));
    }

    private void EnsureIoTestHeaderSelection()
    {
        if (_ioTestHeaderSelectionInstalled)
        {
            RefreshIoTestHeaderSelection();
            return;
        }

        var grid = FindIoTestSelectionGrid(this);
        if (grid == null)
            return;

        var column = grid.Columns.FirstOrDefault(candidate =>
            string.Equals(candidate.Header?.ToString(), "TEST", StringComparison.OrdinalIgnoreCase));
        if (column == null)
            return;

        var headerCheckBox = new CheckBox
        {
            Width = 16,
            Height = 16,
            IsThreeState = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = "Check / uncheck all TEST rows for the selected IED"
        };
        AutomationProperties.SetName(headerCheckBox, "Toggle all FAT test rows");
        headerCheckBox.Click += IoTestHeaderSelectionCheckBox_Click;

        column.Header = headerCheckBox;
        _ioTestHeaderSelectionCheckBox = headerCheckBox;
        _ioTestHeaderSelectionInstalled = true;

        PropertyChanged += IoTestHeaderSelectionWindow_PropertyChanged;
        Closed += IoTestHeaderSelectionWindow_Closed;
        RewireIoTestHeaderObservedIed();
        RefreshIoTestHeaderSelection();
    }

    private void IoTestHeaderSelectionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        var ied = SelectedIed;
        if (ied == null || !CanEditPlan)
        {
            RefreshIoTestHeaderSelection();
            return;
        }

        var target = GetIoTestHeaderSelectionState(ied) != true;
        foreach (var point in ied.TestPoints)
            point.TestEnabled = target;

        Storage?.ScheduleSave();
        Raise(nameof(SelectedIedSummary));
        RaiseSelectedIedContextProperties();
        RefreshIoTestHeaderSelection();
    }

    private void IoTestHeaderSelectionWindow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedIed))
            RewireIoTestHeaderObservedIed();

        RefreshIoTestHeaderSelection();
    }

    private void IoTestHeaderObservedIed_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RefreshIoTestHeaderSelection();

    private void RewireIoTestHeaderObservedIed()
    {
        if (ReferenceEquals(_ioTestHeaderObservedIed, SelectedIed))
            return;

        if (_ioTestHeaderObservedIed != null)
            _ioTestHeaderObservedIed.PropertyChanged -= IoTestHeaderObservedIed_PropertyChanged;

        _ioTestHeaderObservedIed = SelectedIed;
        if (_ioTestHeaderObservedIed != null)
            _ioTestHeaderObservedIed.PropertyChanged += IoTestHeaderObservedIed_PropertyChanged;
    }

    private void RefreshIoTestHeaderSelection()
    {
        var header = _ioTestHeaderSelectionCheckBox;
        if (header == null)
            return;

        var ied = SelectedIed;
        header.IsEnabled = CanEditPlan && ied?.TestPoints.Count > 0;
        header.IsChecked = ied == null ? false : GetIoTestHeaderSelectionState(ied);
    }

    private static bool? GetIoTestHeaderSelectionState(IoTestIedPlan ied)
    {
        if (ied.TestPoints.Count == 0)
            return false;

        var enabled = ied.TestPoints.Count(point => point.TestEnabled);
        if (enabled == 0)
            return false;
        if (enabled == ied.TestPoints.Count)
            return true;
        return null;
    }

    private void IoTestHeaderSelectionWindow_Closed(object? sender, EventArgs e)
    {
        PropertyChanged -= IoTestHeaderSelectionWindow_PropertyChanged;
        Closed -= IoTestHeaderSelectionWindow_Closed;

        if (_ioTestHeaderObservedIed != null)
            _ioTestHeaderObservedIed.PropertyChanged -= IoTestHeaderObservedIed_PropertyChanged;
        _ioTestHeaderObservedIed = null;

        if (_ioTestHeaderSelectionCheckBox != null)
            _ioTestHeaderSelectionCheckBox.Click -= IoTestHeaderSelectionCheckBox_Click;
    }

    private static DataGrid? FindIoTestSelectionGrid(DependencyObject root)
    {
        foreach (var grid in FindIoTestHeaderVisualChildren<DataGrid>(root))
        {
            if (grid.Columns.Any(column =>
                    string.Equals(column.Header?.ToString(), "TEST", StringComparison.OrdinalIgnoreCase)))
            {
                return grid;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindIoTestHeaderVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root == null)
            yield break;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindIoTestHeaderVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
