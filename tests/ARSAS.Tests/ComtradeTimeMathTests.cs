using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeTimeMathTests
{
    [Fact]
    public void TriggerOffset_UsesComtradeTimestampTexts()
    {
        Assert.True(ComtradeTimeMath.TryGetTriggerOffsetMilliseconds(
            "01/01/2026,00:00:00.000000",
            "01/01/2026,00:00:00.002500",
            out var offset));

        Assert.Equal(2.5, offset, 8);
    }

    [Fact]
    public void TriggerOffset_AcceptsLegacyUsDateOrdering()
    {
        Assert.True(ComtradeTimeMath.TryGetTriggerOffsetMilliseconds(
            "12/31/2025,23:59:59.999000",
            "01/01/2026,00:00:00.001000",
            out var offset));

        Assert.Equal(2.0, offset, 8);
    }

    [Fact]
    public void FractionForFrame_UsesTimestampRatherThanFrameSpacing()
    {
        var timestamps = new uint[] { 0, 100, 900, 1000 };
        var window = new ComtradeFrameWindow(0, 4);

        Assert.Equal(0.1, ComtradeTimeMath.FractionForFrame(timestamps, window, 1), 8);
        Assert.Equal(0.9, ComtradeTimeMath.FractionForFrame(timestamps, window, 2), 8);
    }

    [Fact]
    public void FrameAtFraction_ReturnsNearestTimestamp()
    {
        var timestamps = new uint[] { 0, 100, 900, 1000 };
        var window = new ComtradeFrameWindow(0, 4);

        Assert.Equal(1, ComtradeTimeMath.FrameAtFraction(timestamps, window, 0.2));
        Assert.Equal(2, ComtradeTimeMath.FrameAtFraction(timestamps, window, 0.8));
    }

    [Fact]
    public void ToMilliseconds_AppliesTimeMultiplier()
    {
        Assert.Equal(5.0, ComtradeTimeMath.ToMilliseconds(1000, 5.0), 8);
        Assert.Equal(1.0, ComtradeTimeMath.ToMilliseconds(1000, double.NaN), 8);
    }
}
