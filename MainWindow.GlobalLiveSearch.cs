using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private bool _globalLiveFiltersExpanded;
    private bool _globalLiveHeaderConverged;

    [ModuleInitializer]
    internal static void RegisterGlobalLiveHeaderConvergence()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(GlobalLiveHeader_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void GlobalLiveHeader_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.ConvergeGlobalLiveHeader();
    }

    private void ConvergeGlobalLiveHeader()
    {
        if (_globalLiveHeaderConverged || GlobalLiveSearchBox == null || GlobalLiveFiltersButton == null)
            return;

        if (GlobalLiveFiltersButton.Parent is not Grid actions || actions.Parent is not Grid header)
            return;

        var titlePanel = header.Children
            .OfType<StackPanel>()
            .FirstOrDefault(panel => Grid.GetRow(panel) == 0);
        if (titlePanel == null || actions.ColumnDefinitions.Count < 3)
            return;

        // Match the compact sibling-tab header contract: title/subtitle on the left,
        // bounded search and actions on the same horizontal row. The previous star-sized
        // search consumed the whole workspace width and pushed controls onto a second row.
        header.RowDefinitions.Clear();
        header.ColumnDefinitions.Clear();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetRow(titlePanel, 0);
        Grid.SetColumn(titlePanel, 0);
        titlePanel.VerticalAlignment = VerticalAlignment.Center;

        Grid.SetRow(actions, 0);
        Grid.SetColumn(actions, 1);
        actions.Margin = new Thickness(16, 0, 0, 0);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.VerticalAlignment = VerticalAlignment.Center;

        actions.ColumnDefinitions[0].Width = new GridLength(320);
        actions.ColumnDefinitions[1].Width = new GridLength(8);
        actions.ColumnDefinitions[2].Width = GridLength.Auto;

        _globalLiveHeaderConverged = true;
    }

    private void GlobalLiveSearch_TextChanged(object sender, TextChangedEventArgs e)
        => GridUxBehavior.SetGlobalRapidSearch(GlobalLiveGrid, GlobalLiveSearchBox?.Text);

    private void GlobalLiveSearchClear_Click(object sender, RoutedEventArgs e)
    {
        if (GlobalLiveSearchBox == null)
            return;

        GlobalLiveSearchBox.Clear();
        GlobalLiveSearchBox.Focus();
    }

    private void GlobalLiveFilters_Click(object sender, RoutedEventArgs e)
    {
        _globalLiveFiltersExpanded = !_globalLiveFiltersExpanded;
        GridUxBehavior.SetGlobalRapidFiltersExpanded(GlobalLiveGrid, _globalLiveFiltersExpanded);
        if (GlobalLiveFiltersLabel != null)
            GlobalLiveFiltersLabel.Text = _globalLiveFiltersExpanded ? "Hide filters" : "Filters";
    }
}
