using System;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// M7 compatibility bridge for the canonical seventh Engineering destination.
///
/// MainWindow.xaml owns the FAT tab and navigation button. Production FAT is mounted
/// into that permanent XAML slot by MainWindow.ProductionFatTab.cs. This file deliberately
/// owns no FAT rows, capture commands, evidence state, persistence, preview, export, or timers.
/// It retains only the shared slot index and a deferred navigation-geometry refresh used by
/// the canonical MainWindow shell.
/// </summary>
public partial class MainWindow
{
    private const int NativeFatWorkspaceIndex = 6;

    private void QueueNativeFatNavigationGeometry()
    {
        if (!IsLoaded)
            return;

        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => UpdateNavigationVisuals(MainTabs.SelectedIndex, animate: false)));
    }
}
