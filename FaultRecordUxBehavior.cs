using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArIED61850Tester;

/// <summary>
/// Adds the fault-record entry point without coupling MainWindow to file-transfer details.
/// </summary>
internal static class FaultRecordUxBehavior
{
    private const string ButtonName = "FaultRecordTransferButton";
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainWindowLoaded));
    }

    private static void OnMainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow mainWindow)
            return;
        if (FindDescendant<Button>(mainWindow, ButtonName) != null)
            return;

        if (mainWindow.FindName("WorkflowNavShell") is not FrameworkElement navigationShell ||
            navigationShell.Parent is not Grid headerGrid)
        {
            return;
        }

        var actionPanel = headerGrid.Children
            .OfType<WrapPanel>()
            .FirstOrDefault(panel => Grid.GetColumn(panel) == 2);
        if (actionPanel == null)
            return;

        var button = new Button
        {
            Name = ButtonName,
            Content = "Fault Records",
            Height = 32,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            ToolTip = "Browse and download IEC 61850 relay fault records"
        };

        if (Application.Current.TryFindResource("SoftButton") is Style style)
            button.Style = style;

        button.Click += (_, _) => OpenFaultRecordWindow(mainWindow);
        actionPanel.Children.Insert(0, button);
    }

    private static void OpenFaultRecordWindow(MainWindow mainWindow)
    {
        var device = mainWindow.SelectedDevice;
        if (device == null)
        {
            MessageBox.Show(
                mainWindow,
                "Select an IEC 61850 IED first.",
                "Fault Records",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var existing = Application.Current.Windows
            .OfType<FaultRecordWindow>()
            .FirstOrDefault(window => ReferenceEquals(window.Owner, mainWindow));
        if (existing != null)
        {
            if (existing.WindowState == WindowState.Minimized)
                existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var window = new FaultRecordWindow(
            device.Name,
            device.IpAddress,
            device.Port)
        {
            Owner = mainWindow
        };
        window.Show();
    }

    private static T? FindDescendant<T>(DependencyObject root, string name)
        where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match && match.Name.Equals(name, StringComparison.Ordinal))
                return match;

            var nested = FindDescendant<T>(child, name);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
