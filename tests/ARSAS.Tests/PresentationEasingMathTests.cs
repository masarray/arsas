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
}
