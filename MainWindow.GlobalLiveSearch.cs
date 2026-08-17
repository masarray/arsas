using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private bool _globalLiveFiltersExpanded;

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
