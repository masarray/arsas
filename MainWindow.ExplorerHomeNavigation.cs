using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Restores the IED Explorer section header as a true Home affordance. Returning Home is a
/// presentation-only action: connected IEDs, monitoring sessions and a loaded FAT project
/// stay alive; only the selected detail card is cleared so the existing ARSAS first-run
/// launcher becomes visible again.
/// </summary>
public partial class MainWindow
{
    [ModuleInitializer]
    internal static void RegisterExplorerHomeNavigation()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ExplorerHomeNavigation_Loaded));
    }

    private static void ExplorerHomeNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        window.Dispatcher.BeginInvoke(
            new Action(window.InstallExplorerHomeNavigation),
            DispatcherPriority.Loaded);
    }

    private void InstallExplorerHomeNavigation()
    {
        var title = ExplorerHomeVisualDescendants<TextBlock>(this)
            .FirstOrDefault(text => string.Equals(
                text.Text?.Trim(),
                "IED Explorer",
                StringComparison.OrdinalIgnoreCase));
        if (title?.Parent is not StackPanel header)
            return;

        if (Equals(header.Tag, "ARSAS_EXPLORER_HOME"))
            return;

        header.Tag = "ARSAS_EXPLORER_HOME";
        header.Cursor = Cursors.Hand;
        header.ToolTip = "ARSAS Home — show the IEC 61850 Engineering / FAT launcher without unloading the current workspace";
        header.MouseLeftButtonUp += ExplorerHomeHeader_MouseLeftButtonUp;
    }

    private void ExplorerHomeHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        e.Handled = true;

        // The launcher is installed once and remains underneath SelectedExplorerVisibility.
        // Reassert its contract defensively before exposing it; this does not import, close,
        // disconnect or mutate any IED/FAT state.
        InstallFirstRunTestingChoices();
        RestoreFirstRunLauncherContract();

        MainTabs.SelectedIndex = 0;
        SelectedDevice = null;

        SetStatus(_loadedIoFatWindow is { IsLoaded: true }
            ? "ARSAS Home · Engineering workspace and loaded IO List FAT project remain active."
            : "ARSAS Home · current Engineering devices remain loaded.");
    }

    private static IEnumerable<T> ExplorerHomeVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var nested in ExplorerHomeVisualDescendants<T>(child))
                yield return nested;
        }
    }
}
