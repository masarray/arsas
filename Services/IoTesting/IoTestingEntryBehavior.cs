using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ArIED61850Tester.Services.IoTesting;

public static class IoTestingEntryBehavior
{
    private const string LauncherTag = "ARSAS_IO_LIST_TESTING_LAUNCHER";
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
            return;
        _installed = true;

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(MainWindow_Loaded));
    }

    private static void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow mainWindow)
            return;

        var headerActions = FindHeaderActions(mainWindow);
        if (headerActions == null || headerActions.Children.OfType<Button>().Any(button => Equals(button.Tag, LauncherTag)))
            return;

        var launcher = new Button
        {
            Tag = LauncherTag,
            Content = "IO List Testing",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(13, 8, 13, 8),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            ToolTip = "Import an ARSAS FAT IO workbook and enter the dedicated IO List Testing workspace"
        };
        launcher.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        launcher.Click += (_, _) => OpenIoListTesting(mainWindow);
        headerActions.Children.Insert(0, launcher);
    }

    private static WrapPanel? FindHeaderActions(MainWindow mainWindow)
    {
        if (VisualTreeHelper.GetChildrenCount(mainWindow) == 0)
            return null;
        if (VisualTreeHelper.GetChild(mainWindow, 0) is not Grid root)
            return null;

        var header = root.Children.OfType<Grid>().FirstOrDefault(grid => Grid.GetRow(grid) == 0);
        if (header == null)
            return null;

        return header.Children.OfType<WrapPanel>().FirstOrDefault(panel => Grid.GetColumn(panel) == 2);
    }

    private static void OpenIoListTesting(MainWindow mainWindow)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import ARSAS FAT IO workbook",
            Filter = "ARSAS FAT IO workbook (*.xlsx)|*.xlsx|Excel workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(mainWindow) != true)
            return;

        try
        {
            var project = new IoTestWorkbookImporter().Import(dialog.FileName);
            var window = new IoListTestingWindow(project)
            {
                Owner = mainWindow
            };
            window.Closed += (_, _) =>
            {
                if (!mainWindow.Dispatcher.HasShutdownStarted)
                {
                    mainWindow.Show();
                    mainWindow.Activate();
                }
            };

            mainWindow.Hide();
            window.Show();
            window.Activate();
        }
        catch (IoTestWorkbookImportException ex)
        {
            MessageBox.Show(
                mainWindow,
                ex.Message,
                "IO List import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                mainWindow,
                $"ARSAS could not open the IO List Testing workspace.\n\n{ex.Message}",
                "IO List Testing",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
