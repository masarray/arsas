using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Keeps the P1 multi-RCB selection model authoritative when the legacy DataGrid virtualizes
/// and recycles checkbox containers. A recycled CheckBox re-applies IsChecked from the bound
/// RcbExportRow.IsSelected value, which raises Checked/Unchecked even though the operator did
/// not change selection. The original single-select instance handlers interpret that visual
/// rehydration as a user action and call SelectOnly, silently clearing the other selected RCBs.
///
/// P1 pointer/keyboard handling already owns real operator selection changes, so these routed
/// state-change events are presentation-only for this grid and must not reach the stale
/// single-select handlers.
/// </summary>
public partial class RcbExportFilterWindow
{
    [ModuleInitializer]
    internal static void RegisterLegacyRcbVirtualizedSelectionAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(CheckBox),
            ToggleButton.CheckedEvent,
            new RoutedEventHandler(SuppressLegacyRcbSingleSelectStateHandler),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(CheckBox),
            ToggleButton.UncheckedEvent,
            new RoutedEventHandler(SuppressLegacyRcbSingleSelectStateHandler),
            handledEventsToo: true);
    }

    private static void SuppressLegacyRcbSingleSelectStateHandler(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox ||
            checkBox.DataContext is not RcbExportRow ||
            Window.GetWindow(checkBox) is not RcbExportFilterWindow window ||
            FindP1LegacyRcbAncestor<DataGrid>(checkBox) is not { } grid ||
            !ReferenceEquals(grid, window.RcbGrid))
        {
            return;
        }

        // Do not allow the original XAML Checked/Unchecked instance handlers to translate
        // DataGrid container materialization/recycling into a single-selection mutation.
        // RcbExportRow.IsSelected remains the durable selection authority.
        e.Handled = true;
    }
}
