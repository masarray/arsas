using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private CheckBox? _clockSyncCheckBox;
    private bool _clockSyncCheckBoxRefreshing;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += ClockSyncUx_Loaded;
    }

    private void ClockSyncUx_Loaded(object sender, RoutedEventArgs e)
    {
        if (_clockSyncCheckBox != null)
        {
            RefreshClockSyncCheckBox();
            return;
        }

        if (WorkspacePreviewToggle.Parent is not Panel actionPanel)
            return;

        var previewIndex = actionPanel.Children.IndexOf(WorkspacePreviewToggle);
        if (previewIndex < 0)
            return;

        var checkBox = new CheckBox
        {
            Name = "ClockSyncEnabledCheckBox",
            Content = "Clock Sync",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Padding = new Thickness(2, 0, 2, 0),
            FontSize = 11.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Ink") as Brush ?? Brushes.DimGray,
            Focusable = false,
            ToolTip = "SNTP laptop → IED. Checked: ARSAS serves and broadcasts laptop time using the SIPROTEC compatibility profile (stratum 2). Unchecked: ARSAS stops its SNTP service. IEC 61850 monitoring is unaffected."
        };

        _clockSyncCheckBox = checkBox;
        RefreshClockSyncCheckBox();
        checkBox.Checked += ClockSyncCheckBox_Changed;
        checkBox.Unchecked += ClockSyncCheckBox_Changed;
        actionPanel.Children.Insert(previewIndex + 1, checkBox);
    }

    private void RefreshClockSyncCheckBox()
    {
        if (_clockSyncCheckBox == null || Owner is not MainWindow mainWindow)
            return;

        _clockSyncCheckBoxRefreshing = true;
        try
        {
            _clockSyncCheckBox.IsChecked = mainWindow.IsClockSyncEnabled;
        }
        finally
        {
            _clockSyncCheckBoxRefreshing = false;
        }
    }

    private async void ClockSyncCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_clockSyncCheckBoxRefreshing ||
            _clockSyncCheckBox == null ||
            Owner is not MainWindow mainWindow)
            return;

        var requested = _clockSyncCheckBox.IsChecked == true;
        _clockSyncCheckBox.IsEnabled = false;
        try
        {
            await mainWindow.SetClockSyncEnabledAsync(requested);
            RefreshClockSyncCheckBox();
        }
        finally
        {
            _clockSyncCheckBox.IsEnabled = true;
        }
    }
}
