using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ArIED61850Tester;

/// <summary>
/// Promotes the existing card-local edit action into the reusable IED Actions entry point
/// without duplicating the task chooser in XAML. The class handler runs before the legacy
/// instance Click handler, so the old direct signal-editor path is suppressed only for the
/// identified IED-card button. All other buttons keep their existing behavior.
/// </summary>
internal static class IedWorkspaceActionsCardBehavior
{
    private const string LegacyToolTip = "Configure Signals";
    private const string ActionsToolTip = "IED Actions — Static DataSet, Select Signals, RCB Engineering, COMTRADE, Browse Offline";

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnButtonLoaded));
        EventManager.RegisterClassHandler(
            typeof(Button),
            ButtonBase.ClickEvent,
            new RoutedEventHandler(OnButtonClick));
    }

    private static void OnButtonLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button || !HasLegacyOrActionsToolTip(button))
            return;

        // Keep the existing compact pencil/edit visual, but make its expanded purpose
        // explicit. The card stays the stable entry point even after Quick Start is closed.
        button.ToolTip = ActionsToolTip;
    }

    private static void OnButtonClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button || !HasLegacyOrActionsToolTip(button))
            return;
        if (Window.GetWindow(button) is not MainWindow mainWindow)
            return;

        // Class handlers precede the XAML instance handler. Marking the routed Click handled
        // prevents IedConfigureSignals_Click from opening the manual wizard directly.
        args.Handled = true;
        _ = mainWindow.OpenIedWorkspaceActionsFromCardAsync(button);
    }

    private static bool HasLegacyOrActionsToolTip(Button button)
    {
        var text = button.ToolTip?.ToString()?.Trim() ?? string.Empty;
        return text.Equals(LegacyToolTip, StringComparison.Ordinal) ||
               text.Equals(ActionsToolTip, StringComparison.Ordinal);
    }
}
