using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private void GlobalLiveSearch_TextChanged(object sender, TextChangedEventArgs e)
        => GridUxBehavior.SetGlobalRapidSearch(GlobalLiveGrid, GlobalLiveSearchBox?.Text);

    private void GlobalLiveSearchClear_Click(object sender, RoutedEventArgs e)
    {
        if (GlobalLiveSearchBox == null)
            return;

        GlobalLiveSearchBox.Clear();
        GlobalLiveSearchBox.Focus();
    }
}
