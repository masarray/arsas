using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeTimeSignalsNavigationMathTests
{
    [Fact]
    public void TriggerFocusedWindow_ShowsEightCyclesWithTriggerAtFortyFivePercent()
    {
        var window = ComtradeTimeSignalsNavigationMath.CreateTriggerFocusedWindow(
            0.0, 5000.0, 1000.0, 50.0);

        Assert.Equal(928.0, window.StartMilliseconds, 9);
        Assert.Equal(1088.0, window.EndMilliseconds, 9);
        Assert.Equal(160.0, window.SpanMilliseconds, 9);
        Assert.Equal(0.45, (1000.0 - window.StartMilliseconds) / window.SpanMilliseconds, 9);
    }

    [Fact]
    public void TriggerFocusedWindow_ClampsAtRecordStartAndEnd()
    {
        var nearStart = ComtradeTimeSignalsNavigationMath.CreateTriggerFocusedWindow(
            0.0, 500.0, 20.0, 50.0);
        var nearEnd = ComtradeTimeSignalsNavigationMath.CreateTriggerFocusedWindow(
            0.0, 500.0, 490.0, 50.0);

        Assert.Equal(0.0, nearStart.StartMilliseconds, 9);
        Assert.Equal(160.0, nearStart.EndMilliseconds, 9);
        Assert.Equal(340.0, nearEnd.StartMilliseconds, 9);
        Assert.Equal(500.0, nearEnd.EndMilliseconds, 9);
    }

    [Theory]
    [InlineData(null, 50.0)]
    [InlineData(1000.0, 0.0)]
    [InlineData(1000.0, double.NaN)]
    public void TriggerFocusedWindow_FallsBackToFullRangeWithoutUsableReference(double? trigger, double frequency)
    {
        var window = ComtradeTimeSignalsNavigationMath.CreateTriggerFocusedWindow(
            100.0, 900.0, trigger, frequency);

        Assert.Equal(100.0, window.StartMilliseconds, 9);
        Assert.Equal(900.0, window.EndMilliseconds, 9);
    }

    [Fact]
    public void InitialCursors_BracketTriggerByOneNominalCycle()
    {
        var cursors = ComtradeTimeSignalsNavigationMath.CreateInitialCursors(
            0.0, 5000.0, 1000.0, 50.0);

        Assert.Equal(980.0, cursors.Cursor1Milliseconds!.Value, 9);
        Assert.Equal(1020.0, cursors.Cursor2Milliseconds!.Value, 9);
    }

    [Fact]
    public void InitialCursors_ClampToAvailableRecord()
    {
        var cursors = ComtradeTimeSignalsNavigationMath.CreateInitialCursors(
            990.0, 1010.0, 1000.0, 50.0);

        Assert.Equal(990.0, cursors.Cursor1Milliseconds!.Value, 9);
        Assert.Equal(1010.0, cursors.Cursor2Milliseconds!.Value, 9);
    }

    [Fact]
    public void SnapTolerance_ConvertsPixelRadiusToCurrentTimeSpan()
    {
        var tolerance = ComtradeTimeSignalsNavigationMath.SnapToleranceMilliseconds(
            160.0, 800.0, 12.0);

        Assert.Equal(2.4, tolerance, 9);
    }
}
