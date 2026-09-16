using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ArIED61850Tester;

/// <summary>
/// Keeps the first-run Explorer hero useful after the obsolete FAT/Dataset launcher was removed.
/// These are normal Explorer entry points only; they never invoke the legacy IO/FAT bootstrap.
/// </summary>
public partial class MainWindow
{
    private bool _explorerHeroActionsInstalled;

    [ModuleInitializer]
    internal static void RegisterExplorerHeroActions()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ExplorerHeroActions_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void ExplorerHeroActions_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._explorerHeroActionsInstalled)
            return;

        window.InstallExplorerHeroActions();
    }

    private void InstallExplorerHeroActions()
    {
        if (_explorerHeroActionsInstalled || MainTabs.Items.Count == 0)
            return;
        if (MainTabs.Items[0] is not TabItem explorerTab || explorerTab.Content is not Grid explorerGrid)
            return;

        var workspace = explorerGrid.Children
            .OfType<Grid>()
            .FirstOrDefault(child => Grid.GetColumn(child) == 2);
        var emptyState = workspace?.Children
            .OfType<Border>()
            .FirstOrDefault(border =>
                BindingOperations.GetBinding(border, UIElement.VisibilityProperty)?.Path?.Path == nameof(EmptyExplorerVisibility));
        if (emptyState?.Child is not Grid heroGrid)
            return;

        var heroCard = heroGrid.Children
            .OfType<Border>()
            .FirstOrDefault(border => border.Child is StackPanel);
        if (heroCard?.Child is not StackPanel heroContent)
            return;

        var actionPanel = heroContent.Children.OfType<WrapPanel>().FirstOrDefault();
        if (actionPanel == null)
            return;

        // Replace the passive helper sentence with direct, discoverable first-run actions.
        // This intentionally duplicates the always-visible left-rail shortcuts: the empty
        // hero is onboarding surface and should clearly advertise the modern workflows.
        actionPanel.Children.Clear();
        actionPanel.Children.Add(CreateExplorerHeroButton(
            "Add IED",
            "LucidePlus",
            "PrimaryButton",
            AddRelay_Click,
            Brushes.White,
            new Thickness(0, 0, 10, 0)));
        actionPanel.Children.Add(CreateExplorerHeroButton(
            "Open SCL",
            "LucideFileInput",
            "SoftButton",
            OpenScl_Click,
            null,
            new Thickness(0, 0, 10, 0)));
        actionPanel.Children.Add(CreateExplorerHeroButton(
            "Open Project",
            "LucideFolderOpen",
            "SoftButton",
            OpenProject_Click,
            null,
            new Thickness(0)));

        _explorerHeroActionsInstalled = true;
    }

    private Button CreateExplorerHeroButton(
        string text,
        string iconResource,
        string styleResource,
        RoutedEventHandler handler,
        Brush? iconStroke,
        Thickness margin)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = TryFindResource(iconResource) as Geometry,
            Style = TryFindResource("LucideIcon") as Style
        };
        if (iconStroke != null)
            icon.Stroke = iconStroke;

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(new Viewbox
        {
            Width = 15,
            Height = 15,
            Margin = new Thickness(0, 0, 7, 0),
            Child = icon
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold
        });

        var button = new Button
        {
            Style = TryFindResource(styleResource) as Style,
            Content = content,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = margin,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += handler;
        return button;
    }
}
