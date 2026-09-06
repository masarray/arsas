using System.Windows;
using System.Windows.Controls;

namespace ArIED61850Tester;

/// <summary>
/// P2 bench-facing header compaction.
///
/// P0 already separates operational actions from evidence/status actions. P2 only tightens
/// that existing hierarchy so the FAT header remains readable on normal engineering laptop
/// widths. Full evidence detail stays in the existing tooltips; protocol, evidence and grid
/// behavior are intentionally untouched.
/// </summary>
public partial class IoListTestingWindow
{
    private void ConfigureP2CompactHeader()
    {
        if (_p0PrimaryHeaderActions == null || _p0SecondaryHeaderActions == null)
            return;

        _p0PrimaryHeaderActions.Margin = new Thickness(0);
        _p0SecondaryHeaderActions.Margin = new Thickness(0, 3, 0, 0);

        foreach (var button in _p0PrimaryHeaderActions.Children.OfType<Button>())
            ApplyP2CompactButtonMetrics(button, secondary: false);

        foreach (var button in _p0SecondaryHeaderActions.Children.OfType<Button>())
            ApplyP2CompactButtonMetrics(button, secondary: true);

        WorkspacePreviewToggle.Content = "Preview";
        if (_cleanSessionButton != null)
            _cleanSessionButton.Content = "Clean FAT";

        ApplyP2CompactStatusMetrics(_clockSyncGlobalStatusText, 118, FontWeights.Medium);
        ApplyP2CompactStatusMetrics(_clockSyncEvidenceText, 188, FontWeights.Normal);
    }

    private static void ApplyP2CompactButtonMetrics(Button button, bool secondary)
    {
        button.Padding = secondary
            ? new Thickness(8, 5, 8, 5)
            : new Thickness(9, 6, 9, 6);
        button.Margin = new Thickness(0, 0, 5, 0);
        button.MinWidth = 0;
        button.MinHeight = secondary ? 27 : 29;
        if (secondary)
        {
            button.FontSize = 10.4;
            button.FontWeight = FontWeights.Medium;
        }
    }

    private static void ApplyP2CompactStatusMetrics(
        TextBlock? text,
        double maxWidth,
        FontWeight fontWeight)
    {
        if (text == null)
            return;

        text.MaxWidth = maxWidth;
        text.TextWrapping = TextWrapping.NoWrap;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.FontWeight = fontWeight;
        text.FontSize = 10.2;
    }
}