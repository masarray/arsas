using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ArIED61850Tester;

public partial class FaultRecordWindow
{
    static FaultRecordWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGridRow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnFaultRecordRowLoaded),
            handledEventsToo: true);
    }

    private static void OnFaultRecordRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGridRow row ||
            Window.GetWindow(row) is not FaultRecordWindow)
        {
            return;
        }

        BindingOperations.SetBinding(
            row,
            FrameworkElement.ToolTipProperty,
            new Binding(nameof(FaultRecordRow.Detail))
            {
                Mode = BindingMode.OneWay,
                FallbackValue = string.Empty,
                TargetNullValue = string.Empty
            });
        ToolTipService.SetShowDuration(row, 30_000);
    }
}
