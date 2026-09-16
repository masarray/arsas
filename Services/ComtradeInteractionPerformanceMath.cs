namespace ArIED61850Tester.Services;

internal static class ComtradeInteractionPerformanceMath
{
    internal static int NearestSortedIndex(IReadOnlyList<double> sortedValues, double target)
    {
        if (sortedValues is null || sortedValues.Count == 0 || !double.IsFinite(target))
            return -1;

        var low = 0;
        var high = sortedValues.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var value = sortedValues[mid];
            if (value < target)
                low = mid + 1;
            else if (value > target)
                high = mid - 1;
            else
                return mid;
        }

        if (low <= 0) return 0;
        if (low >= sortedValues.Count) return sortedValues.Count - 1;
        return Math.Abs(sortedValues[low] - target) < Math.Abs(sortedValues[low - 1] - target)
            ? low
            : low - 1;
    }

    internal static bool TrySnapSorted(
        IReadOnlyList<double> sortedValues,
        double target,
        double tolerance,
        out double snapped)
    {
        snapped = target;
        if (tolerance <= 0 || !double.IsFinite(tolerance))
            return false;

        var index = NearestSortedIndex(sortedValues, target);
        if (index < 0)
            return false;

        var candidate = sortedValues[index];
        if (Math.Abs(candidate - target) > tolerance)
            return false;

        snapped = candidate;
        return true;
    }

    internal static int LabelStride(int itemCount, double plotWidth, double minimumPixelsPerLabel)
    {
        if (itemCount <= 1) return 1;
        if (!double.IsFinite(plotWidth) || plotWidth <= 0) return itemCount;
        var capacity = Math.Max(1, (int)Math.Floor(plotWidth / Math.Max(1.0, minimumPixelsPerLabel)));
        return Math.Max(1, (int)Math.Ceiling(itemCount / (double)capacity));
    }

    internal static bool ShouldDrawTransitionGlyph(
        double x,
        double lastGlyphX,
        double minimumSpacingPixels)
        => double.IsFinite(x) && (!double.IsFinite(lastGlyphX) || x - lastGlyphX >= minimumSpacingPixels);
}
