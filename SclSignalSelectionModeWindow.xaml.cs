using System.Windows;

namespace ArIED61850Tester;

public partial class SclSignalSelectionModeWindow : Window
{
    public SclSignalSelectionModeWindow(int iedCount)
    {
        InitializeComponent();
        ImportScopeText = iedCount == 1
            ? "1 IED WORKSPACE"
            : $"{iedCount} IED WORKSPACES";
        DataContext = this;
    }

    public string ImportScopeText { get; }

    public bool UseStaticDataSet => StaticDataSetChoice.IsChecked == true;

    private void Continue_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;
}
