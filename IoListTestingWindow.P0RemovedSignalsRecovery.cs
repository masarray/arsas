using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// P0 recovery for the FAT disposition overlay.
///
/// Remove from FAT is deliberately reversible. The original V2 installer anchored its
/// restore button to an exact Button.Content == "Engineering" comparison; once the visible
/// caption became "Engineering Workspace" the restore entry point silently disappeared,
/// even though IsIncludedInFat=false and the RemovedFatSignalsWindow were still persisted.
/// This fallback uses semantic/prefix matching and a header-action fallback instead of an
/// exact caption contract.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool P0RemovedSignalsRecoveryRegistered = RegisterP0RemovedSignalsRecovery();
    private bool _p0RemovedSignalsRecoveryInstalled;

    private static bool RegisterP0RemovedSignalsRecovery()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0RemovedSignalsRecovery_Loaded));
        return true;
    }

    private static void P0RemovedSignalsRecovery_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._p0RemovedSignalsRecoveryInstalled)
            return;

        window._p0RemovedSignalsRecoveryInstalled = true;
        window.Dispatcher.BeginInvoke(
            new Action(window.EnsureP0RemovedSignalsEntryPoint),
            DispatcherPriority.ContextIdle);
    }

    private void EnsureP0RemovedSignalsEntryPoint()
    {
        // The normal V2 path may already have installed it when the legacy caption is still
        // present. Never create a second button.
        if (_removedSignalsButton != null)
        {
            RefreshFatV2WorkspaceUx();
            return;
        }

        var engineering = FindVisualDescendants<Button>(this)
            .FirstOrDefault(button =>
                P0ButtonText(button).StartsWith("Engineering", StringComparison.OrdinalIgnoreCase));

        Panel? panel = null;
        var insertIndex = -1;
        if (engineering != null && VisualTreeHelper.GetParent(engineering) is Panel engineeringPanel)
        {
            panel = engineeringPanel;
            insertIndex = panel.Children.IndexOf(engineering);
        }

        // Defensive fallback: locate the top workspace action panel through the stable
        // export action. This keeps restore reachable even if the Engineering caption is
        // localized or renamed again.
        if (panel == null)
        {
            var export = FindVisualDescendants<Button>(this)
                .FirstOrDefault(button =>
                    P0ButtonText(button).Contains("Export .arsas", StringComparison.OrdinalIgnoreCase));
            if (export != null && VisualTreeHelper.GetParent(export) is Panel exportPanel)
            {
                panel = exportPanel;
                insertIndex = panel.Children.Count;
            }
        }

        if (panel == null)
            return;

        _removedSignalsButton = new Button
        {
            Content = "Removed Signals",
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Review and restore signals removed from the active FAT scope"
        };
        if (TryFindResource("SoftButton") is Style softButton)
            _removedSignalsButton.Style = softButton;
        _removedSignalsButton.Click += RemovedSignals_Click;

        panel.Children.Insert(Math.Clamp(insertIndex, 0, panel.Children.Count), _removedSignalsButton);
        RefreshFatV2WorkspaceUx();
    }

    private static string P0ButtonText(Button button)
    {
        if (button.Content is string text)
            return text.Trim();
        if (button.Content is TextBlock block)
            return block.Text?.Trim() ?? string.Empty;
        if (button.Content is Panel panel)
        {
            return string.Join(
                " ",
                panel.Children
                    .OfType<TextBlock>()
                    .Select(child => child.Text?.Trim())
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
        }
        return button.Content?.ToString()?.Trim() ?? string.Empty;
    }
}
