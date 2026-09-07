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
    private static readonly bool RcbExportClickAuthorityRegistered = RegisterRcbExportClickAuthority();

    private static bool RegisterRcbExportClickAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(RcbExportClickAuthority_Click),
            handledEventsToo: false);
        return true;
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
