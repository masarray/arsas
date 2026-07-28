using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArIED61850Tester.Services;

internal static class FatSatWorkspaceBootstrap
{
    private const string ButtonName = "P2FatSatWorkspaceButton";

    [ModuleInitializer]
    internal static void Initialize()
        => EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainWindow_Loaded));

    private static void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window.FindName(ButtonName) is not null)
            return;
        if (window.FindName("WorkflowNavShell") is not FrameworkElement navigation || VisualTreeHelper.GetParent(navigation) is not Grid topNavigation)
            return;

        var actions = topNavigation.Children
            .OfType<WrapPanel>()
            .FirstOrDefault(panel => Grid.GetColumn(panel) == 2);
        if (actions is null)
            return;

        var button = new Button
        {
            Name = ButtonName,
            Content = "FAT/SAT Workspace",
            Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Open the schema-versioned IEC 61850 FAT/SAT test and evidence workspace."
        };
        if (window.TryFindResource("SoftButton") is Style style)
            button.Style = style;
        button.Click += (_, _) =>
        {
            var workspace = new FatSatWorkspaceWindow { Owner = window };
            workspace.ShowDialog();
        };

        actions.Children.Insert(0, button);
        window.RegisterName(ButtonName, button);
    }
}
