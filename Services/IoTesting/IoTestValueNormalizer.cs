using System.Globalization;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public static class IoTestValueNormalizer
{
    public static bool? Normalize(IoTestPointPlan point, string? rawValue)
    {
        ArgumentNullException.ThrowIfNull(point);
        var text = NormalizeText(rawValue);
        if (text.Length == 0 || text is "-" or "unknown" or "pending")
            return null;

        if (bool.TryParse(text, out var boolean))
            return boolean;

        if (text.Contains("[10]", StringComparison.Ordinal))
            return true;
        if (text.Contains("[01]", StringComparison.Ordinal))
            return false;
        if (text.Contains("[00]", StringComparison.Ordinal) || text.Contains("[11]", StringComparison.Ordinal))
            return null;

        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            if (number == point.ExpectedOnRaw)
                return true;
            if (number == point.ExpectedOffRaw)
                return false;
        }

        if (MatchesLabel(text, point.ExpectedOnText))
            return true;
        if (MatchesLabel(text, point.ExpectedOffText))
            return false;

        if (text is "on" or "active" or "asserted" or "operated" or "trip" or "tripped")
            return true;
        if (text is "off" or "inactive" or "deasserted" or "normal" or "reset")
            return false;

        return null;
    }

    public static DateTimeOffset? ParseIedTimestamp(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-")
            return null;

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static bool MatchesLabel(string normalizedValue, string? label)
    {
        var normalizedLabel = NormalizeText(label);
        return normalizedLabel.Length > 0 &&
               (normalizedValue == normalizedLabel ||
                normalizedValue.StartsWith(normalizedLabel + " [", StringComparison.Ordinal));
    }

    private static string NormalizeText(string? value)
        => string.Join(
            ' ',
            (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
