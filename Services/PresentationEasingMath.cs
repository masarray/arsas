namespace ArIED61850Tester.Services;

/// <summary>
/// Allocation-free presentation easing helpers. These functions are intentionally presentation-only:
/// raw COMTRADE/IEC 61850 engineering values remain untouched and authoritative.
/// </summary>
public static class PresentationEasingMath
{
    public static double ExponentialAlpha(double elapsedMilliseconds, double timeConstantMilliseconds)
    {
        if (!double.IsFinite(timeConstantMilliseconds) || timeConstantMilliseconds <= 0.0)
            return 1.0;
        if (double.IsPositiveInfinity(elapsedMilliseconds))
            return 1.0;
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds <= 0.0)
            return 0.0;

        var alpha = 1.0 - Math.Exp(-elapsedMilliseconds / timeConstantMilliseconds);
        return Math.Clamp(alpha, 0.0, 1.0);
    }

    public static double Smooth(double current, double target, double elapsedMilliseconds, double timeConstantMilliseconds)
    {
        if (!double.IsFinite(target)) return current;
        if (!double.IsFinite(current)) return target;
        var alpha = ExponentialAlpha(elapsedMilliseconds, timeConstantMilliseconds);
        return current + ((target - current) * alpha);
    }

    public static double ClampFrameElapsedMilliseconds(
        double elapsedMilliseconds,
        double fallbackMilliseconds = 1000.0 / 60.0,
        double maximumMilliseconds = 50.0)
    {
        var fallback = double.IsFinite(fallbackMilliseconds) && fallbackMilliseconds > 0.0
            ? fallbackMilliseconds
            : 1000.0 / 60.0;
        var maximum = double.IsFinite(maximumMilliseconds) && maximumMilliseconds >= fallback
            ? maximumMilliseconds
            : Math.Max(50.0, fallback);
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds <= 0.0)
            return fallback;
        return Math.Min(elapsedMilliseconds, maximum);
    }

    public static bool IsNear(
        double current,
        double target,
        double relativeTolerance = 1e-4,
        double absoluteTolerance = 1e-6)
    {
        if (!double.IsFinite(current) || !double.IsFinite(target))
            return current.Equals(target);

        var relative = double.IsFinite(relativeTolerance) && relativeTolerance > 0.0
            ? relativeTolerance
            : 0.0;
        var absolute = double.IsFinite(absoluteTolerance) && absoluteTolerance > 0.0
            ? absoluteTolerance
            : 0.0;
        var scale = Math.Max(Math.Abs(current), Math.Abs(target));
        var tolerance = Math.Max(absolute, relative * scale);
        return Math.Abs(current - target) <= tolerance;
    }

    public static double ShortestAngleDeltaDegrees(double currentDegrees, double targetDegrees)
    {
        if (!double.IsFinite(currentDegrees) || !double.IsFinite(targetDegrees))
            return 0.0;
        var delta = (targetDegrees - currentDegrees) % 360.0;
        if (delta >= 180.0) delta -= 360.0;
        if (delta < -180.0) delta += 360.0;
        return delta;
    }

    public static double NormalizeAngleDegrees(double degrees)
    {
        if (!double.IsFinite(degrees)) return 0.0;
        var normalized = degrees % 360.0;
        if (normalized >= 180.0) normalized -= 360.0;
        if (normalized < -180.0) normalized += 360.0;
        return normalized;
    }

    public static double SmoothAngleDegrees(double currentDegrees, double targetDegrees, double elapsedMilliseconds, double timeConstantMilliseconds)
    {
        if (!double.IsFinite(targetDegrees)) return NormalizeAngleDegrees(currentDegrees);
        if (!double.IsFinite(currentDegrees)) return NormalizeAngleDegrees(targetDegrees);
        var alpha = ExponentialAlpha(elapsedMilliseconds, timeConstantMilliseconds);
        return NormalizeAngleDegrees(currentDegrees + (ShortestAngleDeltaDegrees(currentDegrees, targetDegrees) * alpha));
    }
}
