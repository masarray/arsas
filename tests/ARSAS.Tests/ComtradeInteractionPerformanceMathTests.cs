using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeInteractionPerformanceMathTests
{
    [Fact]
    public void NearestSortedIndex_FindsClosestEdgeWithoutLinearScan()
    {
        var values = new[] { -20.0, -1.25, 0.0, 4.5, 40.0 };

        Assert.Equal(1, ComtradeInteractionPerformanceMath.NearestSortedIndex(values, -1.0));
        Assert.Equal(3, ComtradeInteractionPerformanceMath.NearestSortedIndex(values, 5.0));
        Assert.Equal(0, ComtradeInteractionPerformanceMath.NearestSortedIndex(values, -100.0));
        Assert.Equal(4, ComtradeInteractionPerformanceMath.NearestSortedIndex(values, 100.0));
    }

    [Fact]
    public void TrySnapSorted_RespectsCurrentPixelDerivedTolerance()
    {
        var values = new[] { 0.0, 10.0, 20.0 };

        Assert.True(ComtradeInteractionPerformanceMath.TrySnapSorted(values, 10.7, 1.0, out var snapped));
        Assert.Equal(10.0, snapped, 9);
        Assert.False(ComtradeInteractionPerformanceMath.TrySnapSorted(values, 12.0, 1.0, out _));
    }

    [Theory]
    [InlineData(25, 1000, 42, 2)]
    [InlineData(10, 1000, 42, 1)]
    [InlineData(40, 420, 42, 4)]
    public void LabelStride_AdaptsToAvailablePixels(int itemCount, double width, double minimumSpacing, int expected)
    {
        Assert.Equal(expected, ComtradeInteractionPerformanceMath.LabelStride(itemCount, width, minimumSpacing));
    }

    [Fact]
    public void TransitionGlyphSpacing_SuppressesUnreadableTextNoise()
    {
        Assert.True(ComtradeInteractionPerformanceMath.ShouldDrawTransitionGlyph(100, double.NegativeInfinity, 28));
        Assert.False(ComtradeInteractionPerformanceMath.ShouldDrawTransitionGlyph(115, 100, 28));
        Assert.True(ComtradeInteractionPerformanceMath.ShouldDrawTransitionGlyph(129, 100, 28));
    }
}
