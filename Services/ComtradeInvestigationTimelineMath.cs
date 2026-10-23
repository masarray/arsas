namespace ArIED61850Tester.Services;

internal readonly record struct ComtradeTimelineTick(double AbsoluteMilliseconds, double RelativeMilliseconds);

internal static class ComtradeInvestigationTimelineMath
{
    internal static IReadOnlyList<ComtradeTimelineTick> BuildTriggerAnchoredTicks(
        double viewStartMilliseconds,
        double viewEndMilliseconds,
        double? triggerMilliseconds,
        int approximateTickCount)
    {
        if (!double.IsFinite(viewStartMilliseconds) || !double.IsFinite(viewEndMilliseconds) ||
            viewEndMilliseconds <= viewStartMilliseconds)
            return Array.Empty<ComtradeTimelineTick>();

        var span = viewEndMilliseconds - viewStartMilliseconds;
        approximateTickCount = Math.Clamp(approximateTickCount, 2, 20);
        var step = NiceStep(span / approximateTickCount);
        if (!double.IsFinite(step) || step <= 0)
            return Array.Empty<ComtradeTimelineTick>();

        var origin = triggerMilliseconds is { } trigger && double.IsFinite(trigger) ? trigger : 0.0;
        var relativeStart = viewStartMilliseconds - origin;
        var relativeEnd = viewEndMilliseconds - origin;
        var firstIndex = (long)Math.Ceiling(relativeStart / step - 1e-10);
        var lastIndex = (long)Math.Floor(relativeEnd / step + 1e-10);
        if (lastIndex < firstIndex)
            return Array.Empty<ComtradeTimelineTick>();

        var count = (int)Math.Min(64, lastIndex - firstIndex + 1);
        var ticks = new List<ComtradeTimelineTick>(count);
        for (var offset = 0; offset < count; offset++)
        {
            var index = firstIndex + offset;
            var relative = index * step;
            if (Math.Abs(relative) < step * 1e-10)
                relative = 0;
            ticks.Add(new ComtradeTimelineTick(origin + relative, relative));
        }
        return ticks;
    }

    internal static double NiceStep(double rawStep)
    {
        if (!double.IsFinite(rawStep) || rawStep <= 0)
            return 1.0;
        var exponent = Math.Floor(Math.Log10(rawStep));
        var scale = Math.Pow(10.0, exponent);
        var normalized = rawStep / scale;
        var nice = normalized <= 1.0 ? 1.0
            : normalized <= 2.0 ? 2.0
            : normalized <= 2.5 ? 2.5
            : normalized <= 5.0 ? 5.0
            : 10.0;
        return nice * scale;
    }

    internal static double ClampToRecord(double milliseconds, double fullStartMilliseconds, double fullEndMilliseconds)
    {
        if (!double.IsFinite(milliseconds))
            return fullStartMilliseconds;
        if (!double.IsFinite(fullStartMilliseconds) || !double.IsFinite(fullEndMilliseconds) || fullEndMilliseconds < fullStartMilliseconds)
            return milliseconds;
        return Math.Clamp(milliseconds, fullStartMilliseconds, fullEndMilliseconds);
    }
}
