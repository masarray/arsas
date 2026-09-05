using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

/// <summary>
/// Bench-only scroll stability for the FAT v2 grid.
///
/// Keep the proven Build #1868 FAT schema/lifecycle untouched. WPF recycling may briefly
/// reuse a realized cell/row while the operator scrolls, which can paint another signal's
/// Runtime.CurrentValue for one frame. Standard virtualization keeps virtualization enabled
/// but prevents recycled containers from being reassigned across FAT rows. Column
/// virtualization is disabled because FAT has only a small fixed column set and correctness
/// of LIVE VALUE / VALUE 1 / VALUE 2 presentation is more important than recycling 9 cells.
///
/// Deliberately no live-point subscriptions, no Dispatcher loop, no MMS read, no RCB/DataSet
/// mutation, and no evidence mutation are introduced here.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool FatScrollStabilityClassHandlerRegistered =
        RegisterFatScrollStabilityClassHandler();

    private static bool RegisterFatScrollStabilityClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyFatScrollStability));
        return true;
    }

    private static void ApplyFatScrollStability(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window)
            return;

        var grid = window._fatSignalsGrid ?? FindVisualDescendant<DataGrid>(window);
        if (grid == null)
            return;

        grid.EnableRowVirtualization = true;
        grid.EnableColumnVirtualization = false;
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Standard);
    }
}
