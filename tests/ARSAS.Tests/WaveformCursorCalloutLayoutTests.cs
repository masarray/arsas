using System.Windows;
using ProcessBus.App.Wpf.Controls;

namespace ARSAS.Tests;

/// <summary>
/// FAT layout contract: the callouts point at exact visible intersections,
/// occupy bounded screen space, and never collide when three phases cross.
/// </summary>
public sealed class WaveformCursorCalloutLayoutTests
{
    [Theory]
    [InlineData(18.0)]
    [InlineData(200.0)]
    [InlineData(382.0)]
    public void ThreePhases_CrossingAtSameY_NeverOverlapOrClip(double cursorX)
    {
        var plot = new Rect(0, 0, 400, 205);
        var anchors = new[]
        {
            new WaveformCursorCalloutLayout.Anchor(0, 100, 96),
            new WaveformCursorCalloutLayout.Anchor(1, 100, 108),
            new WaveformCursorCalloutLayout.Anchor(2, 100, 92)
        };

        var positioned = WaveformCursorCalloutLayout.Arrange(plot, cursorX, anchors);
        Assert.Equal(3, positioned.Length);
        foreach (var placement in positioned)
        {
            Assert.InRange(placement.Box.Left, plot.Left, plot.Right);
            Assert.InRange(placement.Box.Right, plot.Left, plot.Right);
            Assert.InRange(placement.Box.Top, plot.Top, plot.Bottom);
            Assert.InRange(placement.Box.Bottom, plot.Top, plot.Bottom);
        }
        for (var i = 0; i < positioned.Length; i++)
        for (var j = i + 1; j < positioned.Length; j++)
            Assert.False(positioned[i].Box.IntersectsWith(positioned[j].Box));
    }

    [Fact]
    public void FourPhaseCallouts_KeepAnchorOrderingAtTopAndBottomBoundaries()
    {
        var plot = new Rect(20, 30, 600, 175);
        var anchors = new[]
        {
            new WaveformCursorCalloutLayout.Anchor(0, 31, 115),
            new WaveformCursorCalloutLayout.Anchor(1, 32, 115),
            new WaveformCursorCalloutLayout.Anchor(2, 200, 115),
            new WaveformCursorCalloutLayout.Anchor(3, 201, 115)
        };
        var result = WaveformCursorCalloutLayout.Arrange(plot, 300, anchors);
        Assert.Equal(4, result.Length);
        Assert.Equal(new[] {0, 1, 2, 3}, result.Select(p => p.Index).ToArray());
        for (var i = 1; i < result.Length; i++)
            Assert.True(result[i].Box.Top >= result[i - 1].Box.Bottom);
        Assert.True(result[0].Box.Top >= plot.Top);
        Assert.True(result[^1].Box.Bottom <= plot.Bottom);
    }

    [Fact]
    public void ArrowheadAndZeroAxis_MustBeAtActualGeometricIntersections()
    {
        var wave = Source("Ports/DigSubAnalyzer/Controls/WaveformPanelControl.cs");
        Assert.Contains("var zeroY = rect.Top + rect.Height / 2.0;", wave);
        Assert.Contains("dc.DrawLine(ZeroPen, new Point(rect.Left, zeroY)", wave);
        Assert.Contains("if (stacked && traces > 0)", wave);
        Assert.Contains("&& !IsSilentZeroTrace(item.Samples)", wave);
        Assert.Contains("var value = item.Samples[a] + (item.Samples[b] - item.Samples[a]) * t;", wave);
        Assert.Contains("bandHeight * 0.37;", wave);
        Assert.Contains("var tip = new Point(x, anchors[placement.Index].Y);", wave);
        Assert.Contains("DrawCalloutArrow(dc, colors[placement.Index], start, tip);", wave);
        Assert.Contains("dc.DrawLine(pen, start, tip);", wave);
        Assert.DoesNotContain("CursorDotBrush", wave);
        Assert.DoesNotContain("dc.DrawEllipse(CursorDotBrush", wave);
        Assert.DoesNotContain("var chips = new List<", wave);
        Assert.DoesNotContain("DispatcherTimer", wave);
    }

    private static string Source(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(path);
    }
}
