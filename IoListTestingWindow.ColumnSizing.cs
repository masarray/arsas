using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Keeps the FAT IEC reference column operator-resizable. IEC object references are often
/// the longest and most important identification field in the FAT grid, so the column must
/// not be capped at the compact-layout width.
/// </summary>
public partial class IoListTestingWindow
{
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

        window.Dispatcher.BeginInvoke(
            new Action(window.ApplyOperatorFatColumnSizing),
            DispatcherPriority.Loaded);
    }

    private void ApplyOperatorFatColumnSizing()
    {
        if (FatSignalsGrid == null)
            return;

        FatSignalsGrid.CanUserResizeColumns = true;
        foreach (var column in FatSignalsGrid.Columns)
        {
            if (!string.Equals(column.Header?.ToString(), "IEC REFERENCE", StringComparison.OrdinalIgnoreCase))
                continue;

            // Preserve the compact initial width, but remove the old ~360 px ceiling.
            // A finite generous cap avoids pathological accidental drags while still letting
            // the operator expose an entire IEC 61850 telegram on wide monitors.
            column.MinWidth = Math.Max(column.MinWidth, 250d);
            column.MaxWidth = 4096d;
            break;
        }
    }
}
