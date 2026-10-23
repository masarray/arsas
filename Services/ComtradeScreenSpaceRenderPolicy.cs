namespace ArIED61850Tester.Services;

/// <summary>
/// Deterministic screen-space LOD policy for COMTRADE waveform rendering.
/// Mirrors the proven ArdIrec workstation strategy: use O(log n) visible-range lookup,
/// preserve full/sparse detail while it is cheap, and switch to extrema-preserving
/// pixel buckets when source density is much higher than the display can represent.
/// </summary>
internal static class ComtradeScreenSpaceRenderPolicy
{
    internal const double DenseSamplesPerPixel = 8.0;
    internal const double SparsePointsPerPixel = 2.0;
    internal const int MinimumSparsePoints = 256;
    internal const int MaximumPixelBuckets = 16_384;

    internal static ComtradeVisibleSampleRange FindVisibleRange(
        uint[] timestamps,
        int count,
        double startRawTimestamp,
        double endRawTimestamp)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        count = Math.Clamp(count, 0, timestamps.Length);
        if (count == 0 || !double.IsFinite(startRawTimestamp) || !double.IsFinite(endRawTimestamp))
            return default;
        if (endRawTimestamp < startRawTimestamp)
            (startRawTimestamp, endRawTimestamp) = (endRawTimestamp, startRawTimestamp);

        // COMTRADE source timestamps are expected to be monotonic. If a malformed source violates
        // even the endpoint ordering, degrade safely to the validated common range rather than
        // applying binary search to an invalid domain.
        if (count > 1 && timestamps[0] > timestamps[count - 1])
            return new ComtradeVisibleSampleRange(0, count);

        var first = LowerBound(timestamps, count, startRawTimestamp);
        var endExclusive = UpperBound(timestamps, count, endRawTimestamp);

        // Keep one neighbor on each side so line continuity at viewport edges is preserved.
        if (first > 0) first--;
        if (endExclusive < count) endExclusive++;
        first = Math.Clamp(first, 0, count);
        endExclusive = Math.Clamp(endExclusive, first, count);
        return new ComtradeVisibleSampleRange(first, endExclusive);
    }

    internal static bool UseEnvelope(int visibleSampleCount, double pixelWidth)
    {
        if (visibleSampleCount <= 0 || !double.IsFinite(pixelWidth) || pixelWidth <= 0)
            return false;
        return visibleSampleCount > Math.Max(1.0, pixelWidth) * DenseSamplesPerPixel;
    }

    internal static int SparseStride(int visibleSampleCount, double pixelWidth, bool preserveAllPoints)
    {
        if (visibleSampleCount <= 1)
            return 1;
        if (preserveAllPoints)
            return 1;

        var width = double.IsFinite(pixelWidth) ? Math.Max(1.0, pixelWidth) : 1.0;
        var target = Math.Max(MinimumSparsePoints, (int)Math.Ceiling(width * SparsePointsPerPixel));
        return Math.Max(1, (visibleSampleCount + target - 1) / target);
    }

    internal static int PixelBucketCount(double pixelWidth)
    {
        var width = double.IsFinite(pixelWidth) ? Math.Max(1.0, pixelWidth) : 1.0;
        return Math.Clamp((int)Math.Ceiling(width), 1, MaximumPixelBuckets);
    }

    private static int LowerBound(uint[] values, int count, double target)
    {
        var low = 0;
        var high = count;
        while (low < high)
        {
            var mid = low + ((high - low) >> 1);
            if (values[mid] < target)
                low = mid + 1;
            else
                high = mid;
        }
        return low;
    }

    private static int UpperBound(uint[] values, int count, double target)
    {
        var low = 0;
        var high = count;
        while (low < high)
        {
            var mid = low + ((high - low) >> 1);
            if (values[mid] <= target)
                low = mid + 1;
            else
                high = mid;
        }
        return low;
    }
}

internal readonly record struct ComtradeVisibleSampleRange(int StartIndex, int EndExclusive)
{
    internal int Count => Math.Max(0, EndExclusive - StartIndex);
    internal bool IsEmpty => EndExclusive <= StartIndex;
}
