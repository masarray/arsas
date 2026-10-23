using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeRepresentationScaleMathTests
{
    [Fact]
    public void TryConvert_SecondaryToPrimary_AppliesTransformerRatio()
    {
        Assert.True(ComtradeRepresentationScaleMath.TryConvert(
            value: 1.25,
            sourceScale: 1.0,
            targetScale: 1500.0,
            magnitude: false,
            out var converted));

        Assert.Equal(1875.0, converted, 9);
    }

    [Fact]
    public void TryConvert_PrimaryToSecondary_IsExactlyReversibleForLinearScale()
    {
        Assert.True(ComtradeRepresentationScaleMath.TryConvert(
            value: -1875.0,
            sourceScale: 1500.0,
            targetScale: 1.0,
            magnitude: false,
            out var converted));

        Assert.Equal(-1.25, converted, 9);
    }

    [Fact]
    public void TryConvert_RmsMagnitude_UsesAbsoluteScaleRatio()
    {
        Assert.True(ComtradeRepresentationScaleMath.TryConvert(
            value: 2.0,
            sourceScale: -2.0,
            targetScale: 10.0,
            magnitude: true,
            out var converted));

        Assert.Equal(10.0, converted, 9);
    }

    [Theory]
    [InlineData(double.NaN, 1.0, 2.0)]
    [InlineData(1.0, 0.0, 2.0)]
    [InlineData(1.0, 1.0, double.PositiveInfinity)]
    public void TryConvert_RejectsMalformedPresentationScale(double value, double sourceScale, double targetScale)
    {
        Assert.False(ComtradeRepresentationScaleMath.TryConvert(
            value,
            sourceScale,
            targetScale,
            magnitude: false,
            out _));
    }
}
