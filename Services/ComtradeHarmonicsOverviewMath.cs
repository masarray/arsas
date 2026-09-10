namespace ArIED61850Tester.Services;

/// <summary>
/// Pure presentation math for the compact P1D.4 harmonic comparison. Native ArdIrec owns all
/// spectral calculations; these helpers only normalize display percentages and readable axes.
/// </summary>
internal static class ComtradeHarmonicsOverviewMath
{
    internal static double PercentOfFundamental(double magnitudeRms, double fundamentalRms)
    {
        if (!double.IsFinite(magnitudeRms) || !double.IsFinite(fundamentalRms) ||
            fundamentalRms <= 1e-12 || magnitudeRms <= 0)
            return 0.0;
        return Math.Max(0.0, magnitudeRms) / fundamentalRms * 100.0;
    }

    internal static int ClampDisplayOrder(int availableMaximumOrder, int displayCap = 10)
    {
        if (displayCap < 0) return 0;
        return Math.Clamp(Math.Max(0, availableMaximumOrder), 0, displayCap);
    }

    internal static double NiceMagnitudeAxisMaximum(IEnumerable<double> magnitudes)
    {
        var measured = magnitudes
            .Where(double.IsFinite)
            .Select(Math.Abs)
            .DefaultIfEmpty(0.0)
            .Max();
        if (measured <= 1e-12) return 1.0;

        var target = measured * 1.12;
        var roughStep = target / 4.0;
        var exponent = Math.Pow(10.0, Math.Floor(Math.Log10(Math.Max(roughStep, 1e-12))));
        var normalized = roughStep / exponent;
        var step = normalized <= 1.0 ? 1.0 :
                   normalized <= 2.0 ? 2.0 :
                   normalized <= 2.5 ? 2.5 :
                   normalized <= 5.0 ? 5.0 : 10.0;
        step *= exponent;
        return Math.Max(step, Math.Ceiling(target / step) * step);
    }
}
