using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ArIED61850Tester;

/// <summary>
/// Visual adapter for the P0 Blue Steel + Cool Blue workstation system.
///
/// Most ARSAS surfaces consume application resource keys. Handle legacy chrome
/// without overwriting scope-specific grid colors, SMV visuals or user window bounds. It deliberately
/// does not alter IEC 61850 workflows, bindings, command handlers, or session state.
/// </summary>
internal static class P2BlueSteelGreigeUx
{
    // Each WPF window receives its visual adapter exactly once. A later
    // dialog activation must never restyle the already-visible MainWindow.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object>
        AppliedWindows = new();

    public static void ApplyToOpenWindows(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (Window window in application.Windows)
        {
            if (!window.IsLoaded || AppliedWindows.TryGetValue(window, out _))
                continue;
            Apply(window);
            AppliedWindows.Add(window, new object());
        }
    }

    private static void Apply(Window window)
    {
        // The bundled AppFontFamily (Inter) is the only window typography
        // authority. A runtime local FontFamily value masked XAML inheritance.

        switch (window)
        {
            case MainWindow mainWindow:
                ApplyMainWindow(mainWindow);
                break;
            case IoListTestingWindow ioFatWindow:
                ApplyIoFatWindow(ioFatWindow);
                break;
        }

        // P2.1 sits above the palette layer: search, Lucide affordances and tighter
        // workstation chrome are visual-only and keep all protocol/session semantics intact.
        P2IndustrialWorkstationUx.Apply(window);
    }

    private static void ApplyMainWindow(MainWindow window)
    {
        if (window.FindName("WorkflowNavShell") is Border navShell)
        {
            navShell.Background = Brush(window, "BlueSteelNavSurface");
            navShell.BorderBrush = Brush(window, "BlueSteelNavBorder");
            navShell.BorderThickness = new Thickness(1);
            navShell.CornerRadius = new CornerRadius(11);
            navShell.Effect = Effect(window, "SteelShellShadow");
        }

        if (window.FindName("WorkflowPill") is Border workflowPill)
            workflowPill.CornerRadius = new CornerRadius(9);
        // NavigationLayoutFix owns all nav/pill height after construction.

        var deviceList = Descendants<ListBox>(window)
            .FirstOrDefault(list => ReferenceEquals(list.ItemsSource, window.Devices));
        if (deviceList != null && Resource(window, "P2MainIedListItemStyle") is Style itemStyle)
            deviceList.ItemContainerStyle = itemStyle;

    }

    private static void ApplyIoFatWindow(IoListTestingWindow window)
    {
        var fatIedList = Descendants<ListBox>(window)
            .FirstOrDefault(list => ReferenceEquals(list.ItemsSource, window.Project.Ieds));
        if (fatIedList != null && Resource(window, "P2FatIedListItemStyle") is Style itemStyle)
            fatIedList.ItemContainerStyle = itemStyle;

        var rowStyle = Resource(window, "P2FatDataGridRow") as Style;
        var cellStyle = Resource(window, "P2FatDataGridCell") as Style;
        var headerStyle = Resource(window, "P2FatDataGridHeader") as Style;

        foreach (var grid in Descendants<DataGrid>(window))
        {
            grid.Background = Brush(window, "PanelSurface");
            grid.BorderBrush = Brush(window, "CoolDivider");
            grid.BorderThickness = new Thickness(1);
            grid.HorizontalGridLinesBrush = Brush(window, "CoolDivider");
            grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            grid.RowBackground = Brush(window, "GridRowPrimary");
            grid.AlternatingRowBackground = Brush(window, "GridRowAlternate");
            grid.AlternationCount = 2;

            if (rowStyle != null)
                grid.RowStyle = rowStyle;
            if (cellStyle != null)
                grid.CellStyle = cellStyle;
            if (headerStyle != null)
                grid.ColumnHeaderStyle = headerStyle;
        }
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