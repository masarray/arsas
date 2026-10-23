using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeNavigationMathTests
{
    [Fact]
    public void ZoomIn_PreservesAnchorAndStaysInsideRecord()
    {
        var full = ComtradeNavigationMath.Full(1000);
        var zoomed = ComtradeNavigationMath.Zoom(full, 1000, 0.75, 0.5);

        Assert.Equal(500, zoomed.Count);
        Assert.InRange(zoomed.Start, 374, 376);
        Assert.Equal(zoomed.Start + 500, zoomed.EndExclusive);
    }

    [Fact]
    public void ZoomOut_ClampsToFullRecord()
    {
        var current = new ComtradeFrameWindow(400, 500);
        var zoomed = ComtradeNavigationMath.Zoom(current, 1000, 0.5, 20.0);

        Assert.Equal(new ComtradeFrameWindow(0, 1000), zoomed);
    }

    [Fact]
    public void Pan_ClampsAtBothRecordEdges()
    {
        var current = new ComtradeFrameWindow(200, 400);

        Assert.Equal(new ComtradeFrameWindow(0, 200), ComtradeNavigationMath.Pan(current, 1000, -1000));
        Assert.Equal(new ComtradeFrameWindow(800, 1000), ComtradeNavigationMath.Pan(current, 1000, 1000));
    }

    [Fact]
    public void FrameAtFraction_MapsViewportEdgesDeterministically()
    {
        var window = new ComtradeFrameWindow(100, 201);

        Assert.Equal(100, ComtradeNavigationMath.FrameAtFraction(window, 1000, 0.0));
        Assert.Equal(150, ComtradeNavigationMath.FrameAtFraction(window, 1000, 0.5));
        Assert.Equal(200, ComtradeNavigationMath.FrameAtFraction(window, 1000, 1.0));
    }

    [Fact]
    public void Normalize_RepairsInvalidWindowWithoutLeavingRecord()
    {
        Assert.Equal(new ComtradeFrameWindow(0, 1),
            ComtradeNavigationMath.Normalize(new ComtradeFrameWindow(-10, -1), 100));
        Assert.Equal(new ComtradeFrameWindow(99, 100),
            ComtradeNavigationMath.Normalize(new ComtradeFrameWindow(200, 500), 100));
    }
}
