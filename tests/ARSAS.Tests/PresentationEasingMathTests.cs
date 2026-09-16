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
    public void SmoothAndSnap_ConvergesExactlyInsideTolerance()
    {
        var value = PresentationEasingMath.SmoothAndSnap(9.9999, 10.0, 16.67, 60.0, 0.001);
        Assert.Equal(10.0, value, 12);
        Assert.True(PresentationEasingMath.IsSettled(value, 10.0, 0.001));
    }

    [Fact]
    public void AngleSmoothAndSnap_UsesShortestPathAndSettlesExactly()
    {
        var value = PresentationEasingMath.SmoothAngleAndSnapDegrees(179.99, -179.99, 16.67, 60.0, 0.05);
        Assert.Equal(-179.99, value, 8);
    }
}
