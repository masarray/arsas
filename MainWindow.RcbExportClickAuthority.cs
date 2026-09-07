using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Production authority for the IED-card RCB action. The routed Click is consumed only after
/// its owning device has been resolved. This prevents the physical-bench dead-button failure
/// where the class handler marked the event handled before the Button.Tag binding was ready.
/// </summary>
public partial class MainWindow
{
    [ModuleInitializer]
    internal static void RegisterRcbExportClickAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(RcbExportButton_Click),
            handledEventsToo: true);
    }

    private static void RcbExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            Window.GetWindow(button) is not MainWindow window ||
            !IsProductionRcbExportButton(button))
        {
            return;
        }

        var device = button.Tag as Iec61850MonitorDevice
                     ?? button.DataContext as Iec61850MonitorDevice
                     ?? window.SelectedDevice;
        if (device == null)
        {
            // Do not consume the event when the device cannot yet be resolved. The legacy
            // instance handler is allowed to run rather than turning the RCB button into a no-op.
            return;
        }

        // IedEditRcbMulti_Click intentionally validates Button.Tag. Supply a deterministic
        // sender carrying the already-resolved device, then consume this production click so
        // the legacy single-RCB instance handler cannot run afterwards.
        e.Handled = true;
        var resolvedSender = new Button { Tag = device };
        window.IedEditRcbMulti_Click(resolvedSender, e);
    }

    private static bool IsProductionRcbExportButton(Button button)
    {
        var toolTip = button.ToolTip?.ToString() ?? string.Empty;
        if (toolTip.Contains("RCB Export", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(button.Content?.ToString()?.Trim(), "RCB", StringComparison.OrdinalIgnoreCase);
    }
}
