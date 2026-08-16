using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

/// <summary>
/// Keeps the compact workflow navigation vertically centered at Windows/DPI text
/// metrics where the old fixed 56 px shell could clip the lower button edge.
/// Scoped to MainWindow navigation only; no global Button style is changed.
/// </summary>
internal static class MainWindowNavigationLayoutFix
{
    [ModuleInitializer]
    internal static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        if (window.FindName("WorkflowNavShell") is Border shell)
        {
            shell.Height = 60;
            shell.Padding = new Thickness(5, 6, 5, 6);
            shell.ClipToBounds = false;
        }

        if (window.FindName("WorkflowPill") is Border pill)
        {
            pill.Height = 36;
            pill.VerticalAlignment = VerticalAlignment.Center;
        }

        foreach (var name in new[]
                 {
                     "NavExplorerButton",
                     "NavLiveButton",
                     "NavEventsButton",
                     "NavGooseButton",
                     "NavDiagnosticsButton"
                 })
        {
            if (window.FindName(name) is not Button button)
                continue;

            button.MinHeight = 40;
            button.Margin = new Thickness(1, 1, 1, 1);
            button.VerticalAlignment = VerticalAlignment.Stretch;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.ClipToBounds = false;
        }
    }
}
