using System.Windows;

namespace ArIED61850Tester;

/// <summary>
/// P1 first-paint guard for the FAT workspace.
///
/// The golden/P0 workspace still declares legacy relay-time columns in XAML and replaces
/// them with the V2 schema from the ContentRendered path. Installing the existing V2
/// workspace during Loaded keeps the visual tree available while ensuring the first
/// visible FAT paint already uses LIVE VALUE / VALUE 1 / VALUE 2.
///
/// InstallFatV2WorkspaceUx is intentionally reused and remains idempotent. No runtime
/// virtualization, protocol acquisition, command, evidence, or ARIEC61850 behavior is
/// changed here.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool P1FirstPaintRegistered = RegisterP1FirstPaint();

    private static bool RegisterP1FirstPaint()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P1FirstPaint_Loaded));
        return true;
    }

    private static void P1FirstPaint_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window ||
            !ReferenceEquals(e.OriginalSource, window))
        {
            return;
        }

        window.InstallFatV2WorkspaceUx();
    }
}
