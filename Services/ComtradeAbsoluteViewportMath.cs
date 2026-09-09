namespace ArIED61850Tester.Services;

internal readonly record struct ComtradeSourceViewport(ulong StartFrame, ulong FrameCount)
{
    internal ulong EndExclusive => StartFrame + FrameCount;
}

internal static class ComtradeAbsoluteViewportMath
{
    private const ulong DefaultMinimumFrames = 16;

    internal static ComtradeSourceViewport Full(ulong totalFrames)
        => new(0, totalFrames);

    internal static ComtradeSourceViewport Normalize(ComtradeSourceViewport viewport, ulong totalFrames)
    {
        if (totalFrames == 0)
            return new ComtradeSourceViewport(0, 0);
        if (viewport.FrameCount == 0)
            return new ComtradeSourceViewport(Math.Min(viewport.StartFrame, totalFrames - 1), 1);

        var start = Math.Min(viewport.StartFrame, totalFrames - 1);
        var available = totalFrames - start;
        var count = Math.Min(viewport.FrameCount, available);
        return new ComtradeSourceViewport(start, Math.Max(1UL, count));
    }

    internal static ComtradeSourceViewport Zoom(
        ComtradeSourceViewport current,
        ulong totalFrames,
        double anchorFraction,
        double scale,
        ulong minimumFrames = DefaultMinimumFrames)
    {
        current = Normalize(current, totalFrames);
        if (current.FrameCount == 0 || totalFrames == 0)
            return current;
        if (!double.IsFinite(scale) || scale <= 0)
            return current;
        if (!double.IsFinite(anchorFraction))
            anchorFraction = 0.5;

        anchorFraction = Math.Clamp(anchorFraction, 0.0, 1.0);
        minimumFrames = Math.Clamp(minimumFrames, 1UL, totalFrames);
        var targetCount = ScaleCount(current.FrameCount, scale, minimumFrames, totalFrames);
        if (targetCount == current.FrameCount)
            return current;

        var anchorOffset = FractionOffset(current.FrameCount, anchorFraction);
        var anchorFrame = current.StartFrame + anchorOffset;
        var targetAnchorOffset = FractionOffset(targetCount, anchorFraction);
        var maxStart = totalFrames - targetCount;
        var targetStart = anchorFrame >= targetAnchorOffset
            ? anchorFrame - targetAnchorOffset
            : 0;
        targetStart = Math.Min(targetStart, maxStart);
        return new ComtradeSourceViewport(targetStart, targetCount);
    }

    internal static ComtradeSourceViewport Pan(
        ComtradeSourceViewport current,
        ulong totalFrames,
        long deltaFrames)
    {
        current = Normalize(current, totalFrames);
        if (current.FrameCount == 0 || current.FrameCount >= totalFrames || deltaFrames == 0)
            return current;

        var maxStart = totalFrames - current.FrameCount;
        ulong targetStart;
        if (deltaFrames < 0)
        {
            var magnitude = deltaFrames == long.MinValue
                ? 1UL << 63
                : checked((ulong)(-deltaFrames));
            targetStart = magnitude >= current.StartFrame ? 0 : current.StartFrame - magnitude;
        }
        else
        {
            var magnitude = checked((ulong)deltaFrames);
            var remaining = maxStart - Math.Min(current.StartFrame, maxStart);
            targetStart = magnitude >= remaining ? maxStart : current.StartFrame + magnitude;
        }

        return new ComtradeSourceViewport(targetStart, current.FrameCount);
    }

    internal static ulong FrameAtFraction(ComtradeSourceViewport viewport, ulong totalFrames, double fraction)
    {
        viewport = Normalize(viewport, totalFrames);
        if (viewport.FrameCount == 0)
            return 0;
        if (!double.IsFinite(fraction))
            fraction = 0;
        fraction = Math.Clamp(fraction, 0.0, 1.0);
        return viewport.StartFrame + FractionOffset(viewport.FrameCount, fraction);
    }

    internal static ComtradeSourceViewport FromPlotWindow(
        ulong[] sourceFrames,
        ComtradeFrameWindow plotWindow,
        ulong totalFrames)
    {
        ArgumentNullException.ThrowIfNull(sourceFrames);
        if (sourceFrames.Length == 0 || totalFrames == 0)
            return new ComtradeSourceViewport(0, 0);

        plotWindow = ComtradeNavigationMath.Normalize(plotWindow, sourceFrames.Length);
        if (plotWindow.Count <= 0)
            return new ComtradeSourceViewport(0, 0);

        var start = Math.Min(sourceFrames[plotWindow.Start], totalFrames - 1);
        var last = Math.Min(sourceFrames[plotWindow.EndExclusive - 1], totalFrames - 1);
        if (last < start)
            last = start;
        var count = last - start + 1;
        return new ComtradeSourceViewport(start, count);
    }

    private static ulong ScaleCount(ulong current, double scale, ulong minimum, ulong maximum)
    {
        var scaled = current * scale;
        if (!double.IsFinite(scaled) || scaled >= maximum)
            return maximum;
        if (scaled <= minimum)
            return minimum;
        return Math.Clamp(checked((ulong)Math.Round(scaled, MidpointRounding.AwayFromZero)), minimum, maximum);
    }

    private static ulong FractionOffset(ulong frameCount, double fraction)
    {
        if (frameCount <= 1)
            return 0;
        var lastOffset = frameCount - 1;
        var scaled = lastOffset * fraction;
        if (scaled <= 0)
            return 0;
        if (scaled >= lastOffset)
            return lastOffset;
        return checked((ulong)Math.Round(scaled, MidpointRounding.AwayFromZero));
    }
}
