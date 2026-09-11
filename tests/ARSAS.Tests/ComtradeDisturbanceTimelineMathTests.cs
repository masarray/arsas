using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeDisturbanceTimelineMathTests
{
    [Theory]
    [InlineData("87L START", true, "Pickup")]
    [InlineData("87L START", false, "Dropoff")]
    [InlineData("MAIN TRIP", true, "Trip")]
    [InlineData("MAIN TRIP", false, "Trip reset")]
    [InlineData("CB OPEN", true, "Open")]
    [InlineData("CB CLOSE", true, "Close")]
    [InlineData("Generic output", true, "Assert")]
    [InlineData("Generic output", false, "Deassert")]
    public void DescribeDigitalEvent_PreservesGenericStateWhileAddingProtectionSemantics(
        string title,
        bool asserted,
        string expected)
    {
        Assert.Equal(expected, ComtradeDisturbanceTimelineMath.DescribeDigitalEvent(title, asserted));
    }

    [Fact]
    public void NearestTimestampIndex_UsesActualComtradeTimestampSpacing()
    {
        var timestamps = new uint[] { 0, 100, 900, 1000 };

        Assert.Equal(1, ComtradeDisturbanceTimelineMath.NearestTimestampIndex(timestamps, 220));
        Assert.Equal(2, ComtradeDisturbanceTimelineMath.NearestTimestampIndex(timestamps, 780));
        Assert.Equal(0, ComtradeDisturbanceTimelineMath.NearestTimestampIndex(timestamps, -10));
        Assert.Equal(3, ComtradeDisturbanceTimelineMath.NearestTimestampIndex(timestamps, 1200));
    }

    [Theory]
    [InlineData(0.0, "0 ms")]
    [InlineData(-12.5, "-12.5 ms")]
    [InlineData(7.25, "+7.25 ms")]
    public void FormatRelativeTime_UsesTriggerCenteredEngineeringNotation(double value, string expected)
    {
        Assert.Equal(expected, ComtradeDisturbanceTimelineMath.FormatRelativeTime(value));
    }

    [Theory]
    [InlineData("87L START")]
    [InlineData("MAIN TRIP")]
    [InlineData("52A OPEN")]
    [InlineData("CB CLOSED")]
    public void UsefulProtectionDigital_RecognizesCommonProtectionAndBreakerNames(string title)
    {
        Assert.True(ComtradeDisturbanceTimelineMath.IsUsefulProtectionDigital(title));
    }
}
