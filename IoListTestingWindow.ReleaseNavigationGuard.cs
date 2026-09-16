using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Release guard for the obsolete compatibility navigation control. Native FAT now lives in
/// the Engineering workstation itself, so the old "Engineering" return button has no valid
/// user-facing purpose and can re-enter the compatibility mount/unmount path unexpectedly.
/// Keep the legacy host code available for explicit compatibility work, but remove its risky
/// navigation button from the shipped UI.
/// </summary>
public partial class IoListTestingWindow
{
    private bool _releaseObsoleteEngineeringButtonHidden;

    [ModuleInitializer]
    internal static void RegisterIoListFatReleaseNavigationGuard()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(IoListFatReleaseNavigationGuard_Loaded),
            handledEventsToo: true);
    }

    private static void IoListFatReleaseNavigationGuard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._releaseObsoleteEngineeringButtonHidden)
            return;

        window._releaseObsoleteEngineeringButtonHidden = true;
        window.Dispatcher.BeginInvoke(
            new Action(() => HideObsoleteEngineeringNavigation(window)),
            DispatcherPriority.Loaded);
    }

    private static void HideObsoleteEngineeringNavigation(DependencyObject root)
    {
        if (root is Button button &&
            string.Equals(button.Content?.ToString()?.Trim(), "Engineering", StringComparison.OrdinalIgnoreCase))
        {
            button.IsEnabled = false;
            button.Focusable = false;
            button.Visibility = Visibility.Collapsed;
            return;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
            HideObsoleteEngineeringNavigation(VisualTreeHelper.GetChild(root, index));
    }
}
