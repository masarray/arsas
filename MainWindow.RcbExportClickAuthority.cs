using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArIED61850Tester;

/// <summary>
/// Rewires the realized production IED-card RCB button itself. The previous implementation
/// intercepted routed input while the XAML instance still owned IedEditRcb_Click, which left
/// a legacy single-RCB route alive. After InitializeComponent/Loaded the legacy instance
/// handler is explicitly removed and the multi-select handler becomes the only Click owner.
/// </summary>
public partial class MainWindow
{
    [ModuleInitializer]
    internal static void RegisterRcbExportClickAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(RcbExportMainWindow_Loaded),
            handledEventsToo: true);
    }

    private static void RcbExportMainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        window.RewireProductionRcbButtons();
    }

    private void RewireProductionRcbButtons()
    {
        foreach (var button in EnumerateRcbButtons(this))
        {
            if (!IsProductionRcbExportButton(button))
                continue;

            button.Click -= IedEditRcb_Click;
            button.Click -= IedEditRcbMulti_Click;
            button.Click += IedEditRcbMulti_Click;
            button.ToolTip = "RCB Export — select any number of native RCBs and export generic interoperable IEC 61850 SCL";
        }
    }

    private static bool IsProductionRcbExportButton(Button button)
    {
        if (button.Tag == null)
            return false;

        var toolTip = button.ToolTip?.ToString() ?? string.Empty;
        return toolTip.Contains("RCB Export", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Button> EnumerateRcbButtons(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button)
                yield return button;

            foreach (var nested in EnumerateRcbButtons(child))
                yield return nested;
        }
    }
}
