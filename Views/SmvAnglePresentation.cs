using System.Globalization;

namespace ArIED61850Tester.Views;

/// <summary>
/// Pure presentation policy. DFT phase, measured displacement, and sample
/// polarity are never changed: only human-readable angle formatting wraps
/// values into [0°, 360°), with zero/360 rounding normalized to exactly 0°.
/// </summary>
public static class SmvAnglePresentation
{
    public static double Normalize360(double degrees)
    {
        if (!double.IsFinite(degrees))
            return double.NaN;
        var angle = (degrees % 360.0 + 360.0) % 360.0;
        var rounded = Math.Round(angle, 1, MidpointRounding.AwayFromZero);
        return rounded >= 360.0 ? 0.0 : rounded;
    }

    public static string Format(double degrees)
    {
        var normalized = Normalize360(degrees);
        return double.IsFinite(normalized)
            ? "∠" + normalized.ToString("0.0", CultureInfo.InvariantCulture) + "°"
            : "—";
    }
}
