namespace ArIED61850Tester.Services;

/// <summary>
/// Presentation-only linear engineering scaling used when the same COMTRADE sample is projected
/// between secondary and primary quantities. The native bridge remains authoritative for channel
/// semantics; this helper only applies the already-cached scale ratio so a representation toggle
/// never needs a synchronous native round-trip on the UI thread.
/// </summary>
internal static class ComtradeRepresentationScaleMath
{
    internal static bool TryConvert(
        double value,
        double sourceScale,
        double targetScale,
        bool magnitude,
        out double converted)
    {
        converted = double.NaN;
        if (!double.IsFinite(value) || !IsUsableScale(sourceScale) || !IsUsableScale(targetScale))
            return false;

        var ratio = targetScale / sourceScale;
        if (!double.IsFinite(ratio))
            return false;
        if (magnitude)
            ratio = Math.Abs(ratio);

        converted = value * ratio;
        return double.IsFinite(converted);
    }

    internal static bool IsUsableScale(double scale)
        => double.IsFinite(scale) && Math.Abs(scale) > 1e-15;
}
