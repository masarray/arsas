using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ArIED61850Tester;

/// <summary>
/// Small visual adapter for the P2 Blue Steel + Light Greige theme.
///
/// Most ARSAS surfaces already consume application resource keys. A few legacy
/// windows still own local white grid/card styles or hard-coded navigation chrome;
/// this adapter replaces only those visual contracts at runtime. It deliberately
/// does not alter IEC 61850 workflows, bindings, command handlers, or session state.
/// </summary>
internal static class P2BlueSteelGreigeUx
{
    private static readonly FontFamily PreferredFont =
        new("Plus Jakarta Sans, Aptos, Segoe UI Variable Text, Segoe UI");

    public static void ApplyToOpenWindows(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (Window window in application.Windows)
            Apply(window);
    }

    private static void Apply(Window window)
    {
        window.FontFamily = PreferredFont;

        switch (window)
        {
            case MainWindow mainWindow:
                ApplyMainWindow(mainWindow);
                break;
            case IoListTestingWindow ioFatWindow:
                ApplyIoFatWindow(ioFatWindow);
                break;
        }
    }

    private static void ApplyMainWindow(MainWindow window)
    {
        // ARSAS is an engineering workstation: use the available display by default.
        if (window.WindowState == WindowState.Normal)
            window.WindowState = WindowState.Maximized;

        if (window.FindName("WorkflowNavShell") is Border navShell)
        {
            navShell.Background = Brush(window, "BlueSteelNavSurface");
            navShell.BorderBrush = Brush(window, "BlueSteelNavBorder");
            navShell.BorderThickness = new Thickness(1);
            navShell.CornerRadius = new CornerRadius(11);
            navShell.Effect = Effect(window, "SteelShellShadow");
        }

        if (window.FindName("WorkflowPill") is Border workflowPill)
        {
            workflowPill.CornerRadius = new CornerRadius(9);
            workflowPill.Height = 36;
        }

        var deviceList = Descendants<ListBox>(window)
            .FirstOrDefault(list => ReferenceEquals(list.ItemsSource, window.Devices));
        if (deviceList != null && Resource(window, "P2MainIedListItemStyle") is Style itemStyle)
            deviceList.ItemContainerStyle = itemStyle;

        foreach (var grid in Descendants<DataGrid>(window))
            ApplyEngineeringGrid(window, grid);
    }

    private static void ApplyIoFatWindow(IoListTestingWindow window)
    {
        window.WindowState = WindowState.Maximized;

        var fatIedList = Descendants<ListBox>(window)
            .FirstOrDefault(list => ReferenceEquals(list.ItemsSource, window.Project.Ieds));
        if (fatIedList != null && Resource(window, "P2FatIedListItemStyle") is Style itemStyle)
            fatIedList.ItemContainerStyle = itemStyle;

        var rowStyle = Resource(window, "P2FatDataGridRow") as Style;
        var cellStyle = Resource(window, "P2FatDataGridCell") as Style;
        var headerStyle = Resource(window, "P2FatDataGridHeader") as Style;

        foreach (var grid in Descendants<DataGrid>(window))
        {
            grid.Background = Brush(window, "GreigeSurface");
            grid.BorderBrush = Brush(window, "SteelDividerStrong");
            grid.BorderThickness = new Thickness(1);
            grid.HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xD0, 0xC9));
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            grid.RowBackground = Brush(window, "GreigeRowA");
            grid.AlternatingRowBackground = Brush(window, "GreigeRowB");
            grid.AlternationCount = 2;

            if (rowStyle != null)
                grid.RowStyle = rowStyle;
            if (cellStyle != null)
                grid.CellStyle = cellStyle;
            if (headerStyle != null)
                grid.ColumnHeaderStyle = headerStyle;
        }
    }

    private static void ApplyEngineeringGrid(FrameworkElement owner, DataGrid grid)
    {
        // Preserve explicit command-grid styles. The regular explorer/live tables
        // already use ModernDataGrid; setting these semantic surfaces locally keeps
        // zebra rows stable even when a legacy window has an old local brush.
        if (grid.Style == Resource(owner, "CommandDataGrid"))
            return;

        grid.RowBackground = Brush(owner, "GreigeRowA");
        grid.AlternatingRowBackground = Brush(owner, "GreigeRowB");
        grid.AlternationCount = 2;
    }

    private static object? Resource(FrameworkElement owner, object key)
        => owner.TryFindResource(key) ?? Application.Current?.TryFindResource(key);

    private static Brush Brush(FrameworkElement owner, object key)
        => Resource(owner, key) as Brush ?? Brushes.Transparent;

    private static Effect? Effect(FrameworkElement owner, object key)
        => Resource(owner, key) as Effect;

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
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