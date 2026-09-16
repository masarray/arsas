namespace ArIED61850Tester.Services;

internal readonly record struct ComtradeTimeWindow(double StartMilliseconds, double EndMilliseconds)
{
    internal double SpanMilliseconds => Math.Max(0.0, EndMilliseconds - StartMilliseconds);
}

internal readonly record struct ComtradeInitialCursors(double? Cursor1Milliseconds, double? Cursor2Milliseconds);

internal static class ComtradeTimeSignalsNavigationMath
{
    internal const double DefaultVisibleCycles = 8.0;
    internal const double DefaultTriggerFraction = 0.45;

    internal static ComtradeTimeWindow CreateTriggerFocusedWindow(
        double fullStartMilliseconds,
        double fullEndMilliseconds,
        double? triggerMilliseconds,
        double nominalFrequencyHz,
        double visibleCycles = DefaultVisibleCycles,
        double triggerFraction = DefaultTriggerFraction)
    {
        if (!double.IsFinite(fullStartMilliseconds) || !double.IsFinite(fullEndMilliseconds) || fullEndMilliseconds <= fullStartMilliseconds)
            return new ComtradeTimeWindow(0.0, 1.0);

        var fullSpan = fullEndMilliseconds - fullStartMilliseconds;
        if (triggerMilliseconds is not { } trigger || !double.IsFinite(trigger) ||
            nominalFrequencyHz <= 0 || !double.IsFinite(nominalFrequencyHz) ||
            visibleCycles <= 0 || !double.IsFinite(visibleCycles))
        {
            return new ComtradeTimeWindow(fullStartMilliseconds, fullEndMilliseconds);
        }

        triggerFraction = Math.Clamp(triggerFraction, 0.05, 0.95);
        var cycleMilliseconds = 1000.0 / nominalFrequencyHz;
        var targetSpan = Math.Min(fullSpan, Math.Max(cycleMilliseconds, cycleMilliseconds * visibleCycles));
        var start = trigger - targetSpan * triggerFraction;
        var end = start + targetSpan;

        if (start < fullStartMilliseconds)
        {
            start = fullStartMilliseconds;
            end = start + targetSpan;
        }
        if (end > fullEndMilliseconds)
        {
            end = fullEndMilliseconds;
            start = end - targetSpan;
        }

        start = Math.Max(fullStartMilliseconds, start);
        end = Math.Min(fullEndMilliseconds, end);
        return end > start
            ? new ComtradeTimeWindow(start, end)
            : new ComtradeTimeWindow(fullStartMilliseconds, fullEndMilliseconds);
    }

    internal static ComtradeInitialCursors CreateInitialCursors(
        double fullStartMilliseconds,
        double fullEndMilliseconds,
        double? triggerMilliseconds,
        double nominalFrequencyHz)
    {
        if (!double.IsFinite(fullStartMilliseconds) || !double.IsFinite(fullEndMilliseconds) || fullEndMilliseconds <= fullStartMilliseconds ||
            triggerMilliseconds is not { } trigger || !double.IsFinite(trigger) ||
            nominalFrequencyHz <= 0 || !double.IsFinite(nominalFrequencyHz))
        {
            return new ComtradeInitialCursors(null, null);
        }

        var cycleMilliseconds = 1000.0 / nominalFrequencyHz;
        var c1 = Math.Clamp(trigger - cycleMilliseconds, fullStartMilliseconds, fullEndMilliseconds);
        var c2 = Math.Clamp(trigger + cycleMilliseconds, fullStartMilliseconds, fullEndMilliseconds);
        return new ComtradeInitialCursors(c1, c2);
    }

    internal static double SnapToleranceMilliseconds(double visibleSpanMilliseconds, double plotWidthPixels, double tolerancePixels = 12.0)
    {
        if (!double.IsFinite(visibleSpanMilliseconds) || visibleSpanMilliseconds <= 0 ||
            !double.IsFinite(plotWidthPixels) || plotWidthPixels <= 0 ||
            !double.IsFinite(tolerancePixels) || tolerancePixels <= 0)
            return 0.0;

        return visibleSpanMilliseconds * tolerancePixels / plotWidthPixels;
    }
}
