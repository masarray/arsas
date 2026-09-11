using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeSelectionTransitionPolicyTests
{
    [Fact]
    public void Capture_PreservesViewportAndBothCursorsForSameSourceWindow()
    {
        var snapshot = ComtradeSelectionNavigationSnapshot.Capture(
            -72.0,
            61.238,
            -72.0,
            61.238,
            100,
            250);

        Assert.True(snapshot.IsValid);
        Assert.Equal(-72.0, snapshot.ViewStartMilliseconds);
        Assert.Equal(61.238, snapshot.ViewEndMilliseconds);
        Assert.Equal(-72.0, snapshot.Cursor1Milliseconds);
        Assert.Equal(61.238, snapshot.Cursor2Milliseconds);
        Assert.True(snapshot.MatchesSource(100, 250));
        Assert.False(snapshot.MatchesSource(101, 250));
        Assert.False(snapshot.MatchesSource(100, 249));
    }

    [Fact]
    public void Capture_RejectsInvalidOrEmptyNavigationState()
    {
        Assert.False(ComtradeSelectionNavigationSnapshot.Capture(0, 0, null, null, 0, 250).IsValid);
        Assert.False(ComtradeSelectionNavigationSnapshot.Capture(10, 5, null, null, 0, 250).IsValid);
        Assert.False(ComtradeSelectionNavigationSnapshot.Capture(0, 10, null, null, 0, 0).IsValid);
        Assert.False(ComtradeSelectionNavigationSnapshot.Capture(double.NaN, 10, null, null, 0, 250).IsValid);
    }

    [Fact]
    public void Capture_DropsNonFiniteCursorValuesWithoutInvalidatingViewport()
    {
        var snapshot = ComtradeSelectionNavigationSnapshot.Capture(
            -20,
            20,
            double.NaN,
            double.PositiveInfinity,
            0,
            2210);

        Assert.True(snapshot.IsValid);
        Assert.Null(snapshot.Cursor1Milliseconds);
        Assert.Null(snapshot.Cursor2Milliseconds);
        Assert.True(snapshot.MatchesSource(0, 2210));
    }
}
