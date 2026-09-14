using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private bool _iedOnboardingConverged;

    [ModuleInitializer]
    internal static void RegisterIedOnboardingConvergence()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(IedOnboarding_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void IedOnboarding_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        // Other modular Loaded handlers may still be building the Explorer hero. Defer
        // one dispatcher turn so this convergence always wins regardless of module-init order.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(window.ConvergeIedOnboardingActions));
    }

    private void ConvergeIedOnboardingActions()
    {
        if (_iedOnboardingConverged)
            return;

        var buttons = EnumerateVisualDescendants<Button>(this).ToArray();
        var onboardingButtons = 0;

        foreach (var addButton in buttons.Where(button => ButtonHasLabel(button, "Add IED")))
        {
            if (addButton.Parent is not Panel parent)
                continue;

            var siblingButtons = parent.Children.OfType<Button>().ToArray();
            var openSclButton = siblingButtons.FirstOrDefault(button => ButtonHasLabel(button, "Open SCL"));
            if (openSclButton == null)
                continue;

            // "Add IED" is the user intent. SCL import and live discovery are routes
            // beneath that intent, not competing top-level actions.
            openSclButton.Visibility = Visibility.Collapsed;
            openSclButton.IsTabStop = false;

            addButton.Click -= AddRelay_Click;
            addButton.Click += AddIedChooser_Click;
            addButton.ToolTip = "Add an IED from SCL (recommended) or discover a live IED by IP";

            if (parent is Grid && siblingButtons.Any(button => ButtonHasLabel(button, "Connect All")))
            {
                Grid.SetColumn(addButton, 0);
                Grid.SetColumnSpan(addButton, 3);
            }

            onboardingButtons++;
        }

        // The first-run hero should expose the same single entry point instead of
        // telling a new user to hunt for an action elsewhere in the window.
        var heroHint = EnumerateVisualDescendants<TextBlock>(this)
            .FirstOrDefault(text => string.Equals(
                text.Text,
                "Use the always-visible Add IED action below the Explorer list.",
                StringComparison.Ordinal));
        if (heroHint?.Parent is WrapPanel heroActions)
        {
            heroHint.Visibility = Visibility.Collapsed;
            if (!heroActions.Children.OfType<Button>().Any(button => ButtonHasLabel(button, "Add IED")))
                heroActions.Children.Insert(0, BuildHeroAddIedButton());
        }

        var heroDescription = EnumerateVisualDescendants<TextBlock>(this)
            .FirstOrDefault(text => text.Text.StartsWith(
                "Connect a relay by IP, let ARSAS discover",
                StringComparison.Ordinal));
        if (heroDescription != null)
        {
            heroDescription.Text =
                "Add an IED from SCL (recommended), or discover a live relay by IP. " +
                "ARSAS keeps each IED independent for engineering, monitoring and FAT.";
        }

        // Only seal the convergence after the sidebar action was actually found. This
        // keeps the class-handler safe if Loaded is observed before the visual tree is ready.
        if (onboardingButtons > 0)
            _iedOnboardingConverged = true;
    }

    private Button BuildHeroAddIedButton()
    {
        var button = new Button
        {
            Style = TryFindResource("PrimaryButton") as Style,
            Padding = new Thickness(14, 7),
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = "Add an IED from SCL (recommended) or discover a live IED by IP",
            Content = new TextBlock
            {
                Text = "+  Add IED",
                FontWeight = FontWeights.SemiBold
            }
        };
        button.Click += AddIedChooser_Click;
        return button;
    }

    private void AddIedChooser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
            MinWidth = Math.Max(310, button.ActualWidth),
            Padding = new Thickness(4)
        };

        var openScl = BuildAddIedMenuItem(
            "Open SCL  •  Recommended",
            "Import SCD, CID, ICD, IID, SSD or XML and add its IED workspace(s).");
        openScl.Click += (_, _) => OpenScl_Click(button, new RoutedEventArgs());
        menu.Items.Add(openScl);

        var discover = BuildAddIedMenuItem(
            "Discover IED",
            "Connect by IP/MMS and discover the live IEC 61850 model.");
        discover.Click += (_, _) => AddRelay_Click(button, new RoutedEventArgs());
        menu.Items.Add(discover);

        button.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static MenuItem BuildAddIedMenuItem(string title, string subtitle)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(3, 2, 10, 2)
        };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(38, 52, 69))
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 10.4,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(96, 112, 134))
        });

        return new MenuItem
        {
            Header = panel,
            Padding = new Thickness(8, 7)
        };
    }

    private static bool ButtonHasLabel(Button button, string label)
    {
        if (button.Content is string raw && string.Equals(raw.Trim(), label, StringComparison.OrdinalIgnoreCase))
            return true;

        return EnumerateVisualDescendants<TextBlock>(button)
            .Any(text => string.Equals(text.Text?.Trim(), label, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<T> EnumerateVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in EnumerateVisualDescendants<T>(child))
                yield return descendant;
        }
    }
}
