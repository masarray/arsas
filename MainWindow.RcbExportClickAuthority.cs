using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

/// <summary>
/// Production authority for the IED-card RCB action. The legacy XAML still carries the old
/// singular instance handler, so the routed Click is intercepted at Button class level before
/// any instance handler can run. Realized RCB buttons are also rewired on Loaded as a second
/// deterministic guard. No Tag/DataContext requirement is used because the physical bench
/// proved that relying on a bound Tag could leave the Legacy SAS route active.
/// </summary>
public partial class MainWindow
{
    [ModuleInitializer]
    internal static void RegisterRcbExportClickAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(RcbExportButton_Loaded),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(RcbExportButton_Click),
            handledEventsToo: true);
    }

    private static void RcbExportButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            Window.GetWindow(button) is not MainWindow window ||
            !IsProductionRcbExportButton(button))
        {
            return;
        }

        // Instance handlers are already attached when Loaded is raised. Remove the singular
        // handler from the realized production control itself and install the generic multi
        // workflow. This is intentionally repeated-safe for virtualized/reloaded templates.
        button.Click -= window.IedEditRcb_Click;
        button.Click -= window.IedEditRcbMulti_Click;
        button.Click += window.IedEditRcbMulti_Click;
        button.ToolTip = "RCB Export — select any number of native RCBs and export generic interoperable IEC 61850 SCL";
    }

    private static void RcbExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            Window.GetWindow(button) is not MainWindow window ||
            !IsProductionRcbExportButton(button))
        {
            return;
        }

        // WPF invokes class handlers before normal instance Click handlers. Consume the
        // event and open the multi-select workflow exactly once, making it impossible for
        // IedEditRcb_Click to surface the Legacy SAS single-RCB dialog.
        e.Handled = true;
        window.IedEditRcbMulti_Click(button, e);
    }

    private static bool IsProductionRcbExportButton(Button button)
    {
        var toolTip = button.ToolTip?.ToString() ?? string.Empty;
        if (toolTip.Contains("RCB Export", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(button.Content?.ToString()?.Trim(), "RCB", StringComparison.OrdinalIgnoreCase);
    }
}
