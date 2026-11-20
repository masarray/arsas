using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

/// <summary>
/// Makes the production IED-card RCB button route directly to the multi-select generic SCL
/// workflow. The older singular Legacy SAS handler remains available as an internal staging
/// exporter, but it must never own the operator-facing click path.
/// </summary>
public partial class MainWindow
{
    // A normal static-field initializer on a partial Window type is not a reliable WPF
    // registration point: MainWindow is eligible for beforefieldinit and the routed Click
    // can be raised before that field is touched. Register at module load so the production
    // RCB button can never fall through to the legacy one-RCB XAML handler.
    [ModuleInitializer]
    internal static void RegisterRcbExportClickAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(RcbExportClickAuthority_Click),
            handledEventsToo: false);
    }

    private static void RcbExportClickAuthority_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            Window.GetWindow(button) is not MainWindow window ||
            !IsProductionRcbExportButton(button))
        {
            return;
        }

        // Class handlers execute before the XAML instance Click handler. Marking this routed
        // event handled prevents IedEditRcb_Click from opening the legacy one-RCB dialog.
        e.Handled = true;
        window.IedEditRcbMulti_Click(button, e);
    }

    private static bool IsProductionRcbExportButton(Button button)
    {
        var toolTip = button.ToolTip?.ToString() ?? string.Empty;
        if (toolTip.Contains("RCB Export Filter", StringComparison.OrdinalIgnoreCase) ||
            toolTip.Contains("RCB Export", StringComparison.OrdinalIgnoreCase))
        {
            return button.Tag != null;
        }

        return false;
    }
}
