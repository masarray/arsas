namespace ArIED61850Tester.Services;

internal readonly record struct ComtradeFrameWindow(int Start, int EndExclusive)
{
    public int Count => Math.Max(0, EndExclusive - Start);
}

internal static class ComtradeNavigationMath
{
    private const int DefaultMinimumFrames = 16;

    public static ComtradeFrameWindow Full(int totalFrames)
        => totalFrames <= 0 ? new ComtradeFrameWindow(0, 0) : new ComtradeFrameWindow(0, totalFrames);

    public static ComtradeFrameWindow Normalize(ComtradeFrameWindow window, int totalFrames)
    {
        if (totalFrames <= 0)
            return new ComtradeFrameWindow(0, 0);

        var start = Math.Clamp(window.Start, 0, totalFrames - 1);
        var end = Math.Clamp(window.EndExclusive, start + 1, totalFrames);
        return new ComtradeFrameWindow(start, end);
    }

    public static ComtradeFrameWindow Zoom(
        ComtradeFrameWindow current,
        int totalFrames,
        double anchorFraction,
        double scale,
        int minimumFrames = DefaultMinimumFrames)
    {
        current = Normalize(current, totalFrames);
        if (current.Count <= 0)
            return current;
        if (!double.IsFinite(anchorFraction))
            anchorFraction = 0.5;
        if (!double.IsFinite(scale) || scale <= 0)
            return current;

        anchorFraction = Math.Clamp(anchorFraction, 0.0, 1.0);
        minimumFrames = Math.Clamp(minimumFrames, 1, Math.Max(1, totalFrames));
        var targetCount = (int)Math.Round(current.Count * scale, MidpointRounding.AwayFromZero);
        targetCount = Math.Clamp(targetCount, Math.Min(minimumFrames, totalFrames), totalFrames);
        if (targetCount == current.Count)
            return current;

        var anchorIndex = current.Start + (current.Count - 1) * anchorFraction;
        var targetStart = (int)Math.Round(anchorIndex - (targetCount - 1) * anchorFraction, MidpointRounding.AwayFromZero);
        targetStart = Math.Clamp(targetStart, 0, totalFrames - targetCount);
        return new ComtradeFrameWindow(targetStart, targetStart + targetCount);
    }

    public static ComtradeFrameWindow Pan(ComtradeFrameWindow current, int totalFrames, int deltaFrames)
    {
        current = Normalize(current, totalFrames);
        if (current.Count <= 0 || deltaFrames == 0 || current.Count >= totalFrames)
            return current;

        var maxStart = totalFrames - current.Count;
        var targetStart = Math.Clamp(current.Start + deltaFrames, 0, maxStart);
        return new ComtradeFrameWindow(targetStart, targetStart + current.Count);
    }

    public static int FrameAtFraction(ComtradeFrameWindow window, int totalFrames, double fraction)
    {
        window = Normalize(window, totalFrames);
        if (window.Count <= 0)
            return -1;
        if (!double.IsFinite(fraction))
            fraction = 0.0;

        fraction = Math.Clamp(fraction, 0.0, 1.0);
        var offset = (int)Math.Round((window.Count - 1) * fraction, MidpointRounding.AwayFromZero);
        return Math.Clamp(window.Start + offset, window.Start, window.EndExclusive - 1);
    }
}
