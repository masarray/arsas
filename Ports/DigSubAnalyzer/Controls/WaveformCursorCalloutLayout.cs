using System.Windows;

namespace ProcessBus.App.Wpf.Controls;

/// <summary>
/// Deterministic geometry for live SV cursor callouts. The input is limited to
/// one lane (at most four phase/neutral curves); no scheduling, animation,
/// packet work or canvas measurements are performed here.
/// </summary>
public static class WaveformCursorCalloutLayout
{
    public readonly record struct Anchor(int Index, double Y, double Width);
    public readonly record struct Placement(int Index, Rect Box, bool LabelRight);

    public static Placement[] Arrange(Rect plot, double cursorX, IReadOnlyList<Anchor> anchors)
    {
        if (anchors.Count == 0 || plot.Width <= 0 || plot.Height <= 0)
            return [];

        const double height = 22;
        const double gutter = 24;
        const double inset = 5;
        var sorted = new Anchor[anchors.Count];
        for (var i = 0; i < sorted.Length; i++)
            sorted[i] = anchors[i];
        Array.Sort(sorted, static (a, b) =>
        {
            var order = a.Y.CompareTo(b.Y);
            return order != 0 ? order : a.Index.CompareTo(b.Index);
        });

        var rightSpace = plot.Right - cursorX - gutter - inset;
        var leftSpace = cursorX - plot.Left - gutter - inset;
        var maxWidth = 0.0;
        for (var i = 0; i < sorted.Length; i++)
            maxWidth = Math.Max(maxWidth, sorted[i].Width);
        var labelRight = rightSpace >= maxWidth ||
                         (rightSpace >= leftSpace && leftSpace < maxWidth);
        var widthLimit = Math.Max(35, Math.Min(plot.Width - inset * 2,
            labelRight ? rightSpace : leftSpace));

        var topMin = plot.Top + inset;
        var bottomMax = plot.Bottom - inset;
        var gap = Math.Max(0, Math.Min(5.0, (bottomMax - topMin - sorted.Length * height) /
            Math.Max(1, sorted.Length - 1)));
        var positions = new double[sorted.Length];
        for (var i = 0; i < sorted.Length; i++)
        {
            var proposed = Math.Clamp(sorted[i].Y - height / 2, topMin,
                Math.Max(topMin, bottomMax - height));
            positions[i] = i == 0 ? proposed : Math.Max(proposed, positions[i - 1] + height + gap);
        }
        // Move any overflowing group upwards, preserving order and separation.
        if (positions[^1] + height > bottomMax)
        {
            positions[^1] = bottomMax - height;
            for (var i = positions.Length - 2; i >= 0; i--)
                positions[i] = Math.Min(positions[i], positions[i + 1] - height - gap);
        }

        var output = new Placement[sorted.Length];
        for (var i = 0; i < sorted.Length; i++)
        {
            var width = Math.Min(sorted[i].Width, widthLimit);
            // Keep labels in the plot even when the cursor approaches a boundary.
            var x = labelRight
                ? Math.Min(cursorX + gutter, plot.Right - inset - width)
                : Math.Max(plot.Left + inset, cursorX - gutter - width);
            output[i] = new Placement(sorted[i].Index,
                new Rect(x, Math.Max(topMin, positions[i]), width, height), labelRight);
        }
        return output;
    }
}
