using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Hardens the parent containers around the workflow navigation.
/// The responsive nav helper owns horizontal sizing; this guard makes sure the parent
/// header never clips the rounded shell, focus border, or shadow when live status chips
/// and the workspace switch are present after an IED connects.
/// </summary>
internal static class MainWindowTopBarContainerHardening
{
    [ModuleInitializer]
    internal static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        window.SizeChanged -= Window_SizeChanged;
        window.SizeChanged += Window_SizeChanged;
        QueueApply(window);
    }

    private static void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is MainWindow window)
            QueueApply(window);
    }

    private static void QueueApply(MainWindow window)
    {
        // Run after all Loaded/SizeChanged layout helpers so the parent geometry is the
        // final authority. This specifically avoids the after-connect shell being cut by
        // an Auto-sized header row or another late visual-density pass.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => Apply(window)));
    }

    private static void Apply(MainWindow window)
    {
        if (window.Content is not Grid root)
            return;

        root.ClipToBounds = false;

        var header = root.Children
            .OfType<Grid>()
            .FirstOrDefault(child => Grid.GetRow(child) == 0);
        if (header == null)
            return;

        header.ClipToBounds = false;
        header.MinHeight = 68d;
        Panel.SetZIndex(header, 50);

        if (window.FindName("WorkflowNavShell") is Border shell)
        {
            shell.Height = 64d;
            shell.MinHeight = 64d;
            shell.Margin = new Thickness(0, 2, 0, 2);
            shell.Padding = new Thickness(5, 7, 5, 7);
            shell.ClipToBounds = false;
            Panel.SetZIndex(shell, 60);
        }

        if (window.FindName("WorkflowNavGrid") is Grid navGrid)
            navGrid.ClipToBounds = false;

        foreach (var panel in header.Children.OfType<Panel>())
            panel.ClipToBounds = false;
    }
}
