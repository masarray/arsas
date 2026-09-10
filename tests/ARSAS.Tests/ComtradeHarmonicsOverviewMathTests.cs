using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeHarmonicsOverviewMathTests
{
    [Fact]
    public void PercentOfFundamental_MakesFundamentalOneHundredPercent()
    {
        Assert.Equal(100.0, ComtradeHarmonicsOverviewMath.PercentOfFundamental(45.39, 45.39), 10);
        Assert.Equal(24.1242564, ComtradeHarmonicsOverviewMath.PercentOfFundamental(10.95, 45.39), 6);
    }

    [Fact]
    public void PercentOfFundamental_HandlesInvalidOrZeroReference()
    {
        Assert.Equal(0.0, ComtradeHarmonicsOverviewMath.PercentOfFundamental(10, 0));
        Assert.Equal(0.0, ComtradeHarmonicsOverviewMath.PercentOfFundamental(double.NaN, 10));
    }

    [Theory]
    [InlineData(25, 10)]
    [InlineData(10, 10)]
    [InlineData(7, 7)]
    [InlineData(-1, 0)]
    public void ClampDisplayOrder_LimitsEngineeringOverviewToH10(int available, int expected)
    {
        Assert.Equal(expected, ComtradeHarmonicsOverviewMath.ClampDisplayOrder(available, 10));
    }

    [Fact]
    public void NiceMagnitudeAxisMaximum_LeavesHeadroomAboveLargestBar()
    {
        var maximum = ComtradeHarmonicsOverviewMath.NiceMagnitudeAxisMaximum(new[] { 10.95, 45.39, 11.17 });
        Assert.True(maximum > 45.39);
        Assert.True(double.IsFinite(maximum));
    }
}
