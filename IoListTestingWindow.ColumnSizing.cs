using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Keeps the FAT IEC reference column operator-resizable and preserves the operator's most
/// recent width across FAT-window reopen in the current ARSAS session. No project/evidence
/// schema is mutated for a purely visual preference.
/// </summary>
public partial class IoListTestingWindow
{
    private const double DefaultFatIecReferenceWidth = 360d;
    private const double MinimumFatIecReferenceWidth = 250d;
    private const double MaximumFatIecReferenceWidth = 4096d;
    private static double _sessionFatIecReferenceWidth = DefaultFatIecReferenceWidth;

    private DataGridColumn? _trackedFatIecReferenceColumn;
    private bool _fatColumnWidthPersistenceInstalled;

    [ModuleInitializer]
    internal static void RegisterFatColumnSizing()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(FatColumnSizing_Loaded));
    }

    private static void FatColumnSizing_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window)
            return;

        // FAT V2 rebuilds the runtime columns from ContentRendered. Apply this operator
        // sizing contract after that rebuild rather than relying on the original XAML
        // column instances, which would be replaced a moment later.
        window.Dispatcher.BeginInvoke(
            new Action(window.ApplyOperatorFatColumnSizing),
            DispatcherPriority.ApplicationIdle);
    }

    private void ApplyOperatorFatColumnSizing()
    {
        _fatSignalsGrid ??= FindVisualDescendant<DataGrid>(this);
        if (_fatSignalsGrid == null)
            return;

        _fatSignalsGrid.CanUserResizeColumns = true;
        var referenceColumn = _fatSignalsGrid.Columns.FirstOrDefault(column =>
            string.Equals(column.Header?.ToString(), "IEC REFERENCE", StringComparison.OrdinalIgnoreCase));
        if (referenceColumn == null)
            return;

        referenceColumn.MinWidth = Math.Max(referenceColumn.MinWidth, MinimumFatIecReferenceWidth);
        referenceColumn.MaxWidth = MaximumFatIecReferenceWidth;
        referenceColumn.Width = new DataGridLength(ClampFatIecReferenceWidth(_sessionFatIecReferenceWidth));
        TrackFatIecReferenceColumn(referenceColumn);
    }

    private void TrackFatIecReferenceColumn(DataGridColumn column)
    {
        if (ReferenceEquals(_trackedFatIecReferenceColumn, column))
            return;

        if (_trackedFatIecReferenceColumn != null)
        {
            DependencyPropertyDescriptor
                .FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))
                ?.RemoveValueChanged(_trackedFatIecReferenceColumn, FatIecReferenceColumn_WidthChanged);
        }

        _trackedFatIecReferenceColumn = column;
        DependencyPropertyDescriptor
            .FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))
            ?.AddValueChanged(column, FatIecReferenceColumn_WidthChanged);

        if (_fatColumnWidthPersistenceInstalled)
            return;

        _fatColumnWidthPersistenceInstalled = true;
        Closed += FatColumnSizing_Closed;
    }

    private void FatIecReferenceColumn_WidthChanged(object? sender, EventArgs e)
    {
        if (sender is not DataGridColumn column)
            return;

        // ActualWidth reflects star/auto recalculation too. Persist only an explicit pixel
        // width so layout passes cannot silently overwrite the operator's chosen width.
        if (!column.Width.IsAbsolute || double.IsNaN(column.Width.Value) || double.IsInfinity(column.Width.Value))
            return;

        _sessionFatIecReferenceWidth = ClampFatIecReferenceWidth(column.Width.Value);
    }

    private void FatColumnSizing_Closed(object? sender, EventArgs e)
    {
        Closed -= FatColumnSizing_Closed;
        _fatColumnWidthPersistenceInstalled = false;

        if (_trackedFatIecReferenceColumn != null)
        {
            DependencyPropertyDescriptor
                .FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))
                ?.RemoveValueChanged(_trackedFatIecReferenceColumn, FatIecReferenceColumn_WidthChanged);
            _trackedFatIecReferenceColumn = null;
        }
    }

    internal static double ClampFatIecReferenceWidthForTest(double width)
        => ClampFatIecReferenceWidth(width);

    internal static void SetFatIecReferenceSessionWidthForTest(double width)
        => _sessionFatIecReferenceWidth = ClampFatIecReferenceWidth(width);

    internal static double GetFatIecReferenceSessionWidthForTest()
        => _sessionFatIecReferenceWidth;

    private static double ClampFatIecReferenceWidth(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width))
            return DefaultFatIecReferenceWidth;
        return Math.Clamp(width, MinimumFatIecReferenceWidth, MaximumFatIecReferenceWidth);
    }
}
