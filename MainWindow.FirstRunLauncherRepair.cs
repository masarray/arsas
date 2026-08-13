using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Keeps the original first-run engineering/FAT launcher contract intact when a
/// decorative workstation overlay is installed before the Loaded-priority launcher.
/// The launcher is operational UI, so visual adapters must never make it disappear.
/// </summary>
public partial class MainWindow
{
    private static readonly bool FirstRunLauncherRepairRegistered = RegisterFirstRunLauncherRepair();

    private static bool RegisterFirstRunLauncherRepair()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(FirstRunLauncherRepair_Loaded));
        return true;
    }

    private static void FirstRunLauncherRepair_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        // MainWindow.IoTesting queues its normal launcher at Loaded priority. Run after
        // that attempt so this path is only a compatibility repair, never a replacement.
        window.Dispatcher.BeginInvoke(
            new Action(window.RestoreFirstRunLauncherContract),
            DispatcherPriority.ContextIdle);
    }

    private void RestoreFirstRunLauncherContract()
    {
        if (_ioListTestingLauncherCard == null)
        {
            var heroGrid = FindFirstRunHeroGrid();
            var tint = heroGrid?.Children
                .OfType<Border>()
                .FirstOrDefault(border => Equals(border.Tag, "P2IndustrialHeroTint"));

            // P2.1 originally inserted its tint as a Border. The legacy launcher
            // intentionally looked for the single Border that represented the general
            // testing card, so the decorative Border made SingleOrDefault fail.
            if (heroGrid != null && tint != null)
                heroGrid.Children.Remove(tint);

            InstallFirstRunTestingChoices();

            if (_ioListTestingLauncherCard == null)
                return;

            // Re-apply the visual adapter only after the operational cards exist. It can
            // now tint the hero and assign Z-order without changing launcher discovery.
            P2IndustrialWorkstationUx.Apply(this);
        }

        RestoreFirstRunLauncherActions();
    }

    private Grid? FindFirstRunHeroGrid()
    {
        if (MainTabs.Items.Count == 0 ||
            MainTabs.Items[0] is not TabItem explorerTab ||
            explorerTab.Content is not Grid explorerGrid)
        {
            return null;
        }

        var workspace = explorerGrid.Children
            .OfType<Grid>()
            .FirstOrDefault(child => Grid.GetColumn(child) == 2);
        var emptyState = workspace?.Children
            .OfType<Border>()
            .FirstOrDefault(border =>
                System.Windows.Data.BindingOperations.GetBinding(border, UIElement.VisibilityProperty)?.Path?.Path == nameof(EmptyExplorerVisibility));

        return emptyState?.Child as Grid;
    }

    private void RestoreFirstRunLauncherActions()
    {
        if (_ioListTestingLauncherCard?.Parent is not WrapPanel chooser)
            return;

        Panel.SetZIndex(chooser, 2);

        var generalCard = chooser.Children
            .OfType<Border>()
            .FirstOrDefault(card => !ReferenceEquals(card, _ioListTestingLauncherCard));
        if (generalCard?.Child is StackPanel generalContent)
        {
            var title = generalContent.Children
                .OfType<TextBlock>()
                .FirstOrDefault(text => text.Text.Contains("Add an IED", StringComparison.OrdinalIgnoreCase));
            var description = generalContent.Children
                .OfType<TextBlock>()
                .FirstOrDefault(text => text.Text.Contains("Connect a relay", StringComparison.OrdinalIgnoreCase));
            var actions = generalContent.Children.OfType<WrapPanel>().FirstOrDefault();

            if (title != null)
                title.Text = "Add IED by SCL or IP address";
            if (description != null)
            {
                description.Text =
                    "Import IEC 61850 endpoints from SCD, CID, ICD, IID or SSD, or enter an IP address directly, then verify the live model before testing.";
            }

            if (actions != null)
            {
                actions.Children.Clear();
                actions.Children.Add(CreateLauncherButton(
                    "Open SCL",
                    "LucideFileInput",
                    "SoftButton",
                    OpenScl_Click,
                    null,
                    new Thickness(0, 0, 8, 0)));
                actions.Children.Add(CreateLauncherButton(
                    "Add IED by IP",
                    "LucidePlus",
                    "PrimaryButton",
                    AddRelay_Click,
                    Brushes.White,
                    new Thickness(0, 0, 8, 0)));
                actions.Children.Add(CreateLauncherButton(
                    "Open Project",
                    "LucideFolderOpen",
                    "SoftButton",
                    OpenProject_Click,
                    null,
                    new Thickness(0)));
            }
        }

        // Keep the FAT card's existing import handler and icon, but restore the explicit
        // Excel affordance that operators used to recognize immediately.
        var importButton = DescendantButtons(_ioListTestingLauncherCard)
            .FirstOrDefault(button =>
                GetLauncherButtonText(button).Equals("Open IO List Workbook", StringComparison.OrdinalIgnoreCase));
        if (importButton != null && importButton.Content is StackPanel importContent)
        {
            var label = importContent.Children.OfType<TextBlock>().FirstOrDefault();
            if (label != null)
                label.Text = "Import Excel IO List";
            importButton.ToolTip = "Import the ARSAS IO List FAT Excel workbook (.xlsx)";
        }
    }

    private static IEnumerable<Button> DescendantButtons(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button)
                yield return button;

            foreach (var descendant in DescendantButtons(child))
                yield return descendant;
        }
    }

    private static string GetLauncherButtonText(Button button)
        => button.Content is StackPanel panel
            ? panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty
            : button.Content?.ToString() ?? string.Empty;
}
