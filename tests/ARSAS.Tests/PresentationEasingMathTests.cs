using ArIED61850Tester.Services;
using Xunit;

namespace ARSAS.Tests;

public sealed class PresentationEasingMathTests
{
    [Fact]
    public void ExponentialSmoothing_IsTimeBased_NotFrameCountBased()
    {
        const double tau = 80.0;
        var halfStep = PresentationEasingMath.Smooth(0.0, 1.0, 16.67, tau);
        var twoSteps = PresentationEasingMath.Smooth(halfStep, 1.0, 16.67, tau);
        var oneStep = PresentationEasingMath.Smooth(0.0, 1.0, 33.34, tau);
        Assert.Equal(oneStep, twoSteps, 12);
    }

    [Fact]
    public void AngleSmoothing_UsesShortestPathAcrossPlusMinus180()
    {
        Assert.Equal(2.0, PresentationEasingMath.ShortestAngleDeltaDegrees(179.0, -179.0), 10);
        Assert.Equal(-2.0, PresentationEasingMath.ShortestAngleDeltaDegrees(-179.0, 179.0), 10);
    }

    [Fact]
    public void InfiniteElapsedTime_SnapsToTarget()
    {
        Assert.Equal(1.0, PresentationEasingMath.ExponentialAlpha(double.PositiveInfinity, 80.0));
        Assert.Equal(42.0, PresentationEasingMath.Smooth(10.0, 42.0, double.PositiveInfinity, 80.0), 10);
    }

    [Fact]
    public void FrameElapsed_IsBoundedAfterUiStall()
    {
        Assert.Equal(1000.0 / 60.0, PresentationEasingMath.ClampFrameElapsedMilliseconds(0.0), 10);
        Assert.Equal(16.0, PresentationEasingMath.ClampFrameElapsedMilliseconds(16.0), 10);
        Assert.Equal(50.0, PresentationEasingMath.ClampFrameElapsedMilliseconds(250.0), 10);
    }

    [Fact]
    public void IsNear_UsesAbsoluteAndRelativeTolerance()
    {
        Assert.True(PresentationEasingMath.IsNear(1000.0, 1000.05));
        Assert.True(PresentationEasingMath.IsNear(0.0, 5e-7));
        Assert.False(PresentationEasingMath.IsNear(1.0, 1.01));
        Assert.False(PresentationEasingMath.IsNear(double.NaN, 1.0));
    }
}
