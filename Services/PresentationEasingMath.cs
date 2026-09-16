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

    public static bool IsSettled(double current, double target, double absoluteTolerance, double relativeTolerance = 0.0)
    {
        if (!double.IsFinite(current) || !double.IsFinite(target)) return current.Equals(target);
        var tolerance = Math.Max(Math.Max(0.0, absoluteTolerance), Math.Abs(target) * Math.Max(0.0, relativeTolerance));
        return Math.Abs(target - current) <= tolerance;
    }

    public static double SmoothAndSnap(
        double current,
        double target,
        double elapsedMilliseconds,
        double timeConstantMilliseconds,
        double absoluteTolerance,
        double relativeTolerance = 0.0)
    {
        var next = Smooth(current, target, elapsedMilliseconds, timeConstantMilliseconds);
        return IsSettled(next, target, absoluteTolerance, relativeTolerance) ? target : next;
    }

    public static double SmoothAngleAndSnapDegrees(
        double currentDegrees,
        double targetDegrees,
        double elapsedMilliseconds,
        double timeConstantMilliseconds,
        double toleranceDegrees = 0.08)
    {
        var next = SmoothAngleDegrees(currentDegrees, targetDegrees, elapsedMilliseconds, timeConstantMilliseconds);
        return Math.Abs(ShortestAngleDeltaDegrees(next, targetDegrees)) <= Math.Max(0.0, toleranceDegrees)
            ? NormalizeAngleDegrees(targetDegrees)
            : next;
    }
}
