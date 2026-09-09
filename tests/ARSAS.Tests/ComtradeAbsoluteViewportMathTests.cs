using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeAbsoluteViewportMathTests
{
    [Fact]
    public void FullAndNormalize_StayInsideSourceRecord()
    {
        Assert.Equal(new ComtradeSourceViewport(0, 1_000), ComtradeAbsoluteViewportMath.Full(1_000));
        Assert.Equal(new ComtradeSourceViewport(990, 10),
            ComtradeAbsoluteViewportMath.Normalize(new ComtradeSourceViewport(990, ulong.MaxValue), 1_000));
        Assert.Equal(new ComtradeSourceViewport(999, 1),
            ComtradeAbsoluteViewportMath.Normalize(new ComtradeSourceViewport(5_000, 0), 1_000));
        Assert.Equal(new ComtradeSourceViewport(0, 0),
            ComtradeAbsoluteViewportMath.Normalize(new ComtradeSourceViewport(5_000, ulong.MaxValue), 0));
    }

    [Fact]
    public void Zoom_PreservesAnchorAndHonorsMinimumRange()
    {
        var full = ComtradeAbsoluteViewportMath.Full(1_000);
        var zoomed = ComtradeAbsoluteViewportMath.Zoom(full, 1_000, 0.5, 0.2, minimumFrames: 16);

        Assert.Equal((ulong)200, zoomed.FrameCount);
        Assert.InRange(zoomed.StartFrame, 399UL, 400UL);
        Assert.InRange(ComtradeAbsoluteViewportMath.FrameAtFraction(zoomed, 1_000, 0.5), 499UL, 500UL);

        var minimum = ComtradeAbsoluteViewportMath.Zoom(zoomed, 1_000, 0.5, 0.00001, minimumFrames: 16);
        Assert.Equal((ulong)16, minimum.FrameCount);
    }

    [Fact]
    public void Pan_ClampsAtBothRecordEdgesIncludingExtremeDelta()
    {
        var current = new ComtradeSourceViewport(400, 200);

        Assert.Equal(new ComtradeSourceViewport(0, 200),
            ComtradeAbsoluteViewportMath.Pan(current, 1_000, long.MinValue));
        Assert.Equal(new ComtradeSourceViewport(800, 200),
            ComtradeAbsoluteViewportMath.Pan(current, 1_000, long.MaxValue));
        Assert.Equal(new ComtradeSourceViewport(450, 200),
            ComtradeAbsoluteViewportMath.Pan(current, 1_000, 50));
    }

    [Fact]
    public void FromPlotWindow_MapsReducedPointsBackToAbsoluteSourceFrames()
    {
        var sourceFrames = new ulong[] { 0, 10, 25, 60, 99 };

        var viewport = ComtradeAbsoluteViewportMath.FromPlotWindow(
            sourceFrames,
            new ComtradeFrameWindow(1, 3),
            totalFrames: 100);

        Assert.Equal(new ComtradeSourceViewport(10, 51), viewport);
    }

    [Theory]
    [InlineData(0.0, 100UL)]
    [InlineData(0.5, 150UL)]
    [InlineData(1.0, 199UL)]
    public void FrameAtFraction_UsesAbsoluteSourceCoordinates(double fraction, ulong expected)
    {
        var viewport = new ComtradeSourceViewport(100, 100);
        Assert.Equal(expected, ComtradeAbsoluteViewportMath.FrameAtFraction(viewport, 1_000, fraction));
    }
}
