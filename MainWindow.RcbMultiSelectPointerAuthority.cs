using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Physical pointer/keyboard authority for RCB export. Preview input is intercepted before
/// the legacy XAML Click handler can run, so the production IED-card RCB action always opens
/// the generic multi-select exporter. Inside that exporter every row independently toggles
/// RcbExportRow.IsSelected; DataGrid row selection never limits export scope to one RCB.
/// </summary>
public partial class MainWindow
{
    [ModuleInitializer]
    internal static void RegisterRcbMultiSelectPointerAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(RcbExport_PreviewMouseLeftButtonDown),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(Button),
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(RcbExport_PreviewKeyDown),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(RcbMultiGrid_PreviewMouseLeftButtonDown),
            handledEventsToo: true);
    }

    private static void RcbExport_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not Button button ||
            Window.GetWindow(button) is not MainWindow window || !IsProductionRcbButton(button))
        {
            return;
        }

        // Preview input happens before Button.Click, therefore the legacy singular XAML
        // handler cannot open at all on this operator action.
        e.Handled = true;
        window.IedEditRcbMulti_Click(button, e);
    }

    private static void RcbExport_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || sender is not Button button ||
            Window.GetWindow(button) is not MainWindow window || !IsProductionRcbButton(button))
        {
            return;
        }

        e.Handled = true;
        window.IedEditRcbMulti_Click(button, e);
    }

    private static bool IsProductionRcbButton(Button button)
    {
        var toolTip = button.ToolTip?.ToString() ?? string.Empty;
        return button.Tag != null &&
               toolTip.Contains("RCB Export", StringComparison.OrdinalIgnoreCase);
    }

    private static void RcbMultiGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not DataGrid grid ||
            Window.GetWindow(grid) is not RcbMultiExportWindow ||
            e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var visualRow = FindRcbAncestor<DataGridRow>(source);
        if (visualRow?.DataContext is not RcbExportRow row)
            return;

        // Inclusion is an independent boolean per RCB. Clicking the checkbox OR any body
        // cell toggles exactly that RCB and never clears selections on other rows.
        row.IsSelected = !row.IsSelected;
        grid.SelectedItem = row;
        e.Handled = true;
    }

    private static T? FindRcbAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current != null)
        {
            if (current is T match)
                return match;

            DependencyObject? parent = null;
            try
            {
                parent = VisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
            }

            if (parent == null && current is FrameworkElement element)
                parent = element.Parent;
            current = parent;
        }
        return null;
    }
}
