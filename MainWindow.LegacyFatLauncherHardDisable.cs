using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Permanent safety boundary for the retired first-run FAT / DATASET VERIFICATION launcher.
///
/// The legacy launcher mutates the Explorer empty-state and exposes the obsolete direct
/// SCL/Excel/project FAT bootstrap path. Normal FAT now lives exclusively in the native FAT
/// tab, so this launcher must never be constructed or remain in the visual tree.
/// </summary>
public partial class MainWindow
{
    private bool _legacyFatLauncherHardDisabled;

    [ModuleInitializer]
    internal static void RegisterLegacyFatLauncherHardDisable()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(LegacyFatLauncher_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void LegacyFatLauncher_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._legacyFatLauncherHardDisabled)
            return;

        window._legacyFatLauncherHardDisabled = true;

        // InstallFirstRunTestingChoices() begins with a non-null guard on this field.
        // MainWindow itself is a FrameworkElement, so using it as a permanent sentinel makes
        // any stale/deferred legacy invocation a no-op without retaining a launcher card.
        window._ioListTestingLauncherCard = window;

        // Purge immediately in case an older bootstrap path ran before Loaded, then repeat
        // after Loaded-priority work so a queued legacy BeginInvoke cannot resurrect it.
        window.RemoveLegacyFatDatasetVerificationCard();
        window.Dispatcher.BeginInvoke(
            new Action(window.RemoveLegacyFatDatasetVerificationCard),
            DispatcherPriority.ContextIdle);
    }

    private void RemoveLegacyFatDatasetVerificationCard()
    {
        if (MainTabs.Items.Count == 0 || MainTabs.Items[0] is not TabItem explorerTab)
            return;

        var root = explorerTab.Content as DependencyObject;
        if (root is null)
            return;

        var legacyCard = FindLegacyFatDatasetVerificationCard(root);
        if (legacyCard is null)
            return;

        // Disable first so no click can enter the retired SCL/Excel FAT bootstrap while the
        // visual tree is being corrected.
        legacyCard.IsEnabled = false;
        legacyCard.Visibility = Visibility.Collapsed;

        if (VisualTreeHelper.GetParent(legacyCard) is Panel parentPanel)
        {
            parentPanel.Children.Remove(legacyCard);

            // The legacy injector wrapped the normal Explorer hero and this card in a
            // two-child WrapPanel. Keep the surviving normal hero but remove the obsolete
            // launcher's reserved width/margin so the empty state remains usable.
            if (parentPanel is WrapPanel chooser && chooser.Children.Count == 1 && chooser.Children[0] is FrameworkElement survivor)
            {
                survivor.Margin = new Thickness(0);
                chooser.MaxWidth = double.PositiveInfinity;
            }
        }
    }

    private static FrameworkElement? FindLegacyFatDatasetVerificationCard(DependencyObject root)
    {
        if (root is Border border && ContainsLegacyFatDatasetVerificationText(border))
            return border;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var match = FindLegacyFatDatasetVerificationCard(VisualTreeHelper.GetChild(root, i));
            if (match is not null)
                return match;
        }

        return null;
    }

    private static bool ContainsLegacyFatDatasetVerificationText(DependencyObject root)
    {
        if (root is TextBlock text &&
            (string.Equals(text.Text, "FAT / DATASET VERIFICATION", StringComparison.Ordinal) ||
             string.Equals(text.Text, "Run FAT directly from SCL", StringComparison.Ordinal)))
        {
            return true;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (ContainsLegacyFatDatasetVerificationText(VisualTreeHelper.GetChild(root, i)))
                return true;
        }

        return false;
    }
}
