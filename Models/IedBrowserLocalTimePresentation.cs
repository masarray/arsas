using System.Globalization;
using System.Text.RegularExpressions;

namespace ArIED61850Tester.Models;

/// <summary>
/// Operator-local IEC UTC display only. Never alter the original MMS value,
/// wire time-quality or comparison/evidence source. Unknown timezone means
/// the value may already be local, so refuse to shift it a second time.
/// </summary>
public static class IedBrowserLocalTimePresentation
{
    private static readonly Regex ExplicitZone = new(
        @"^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z| UTC| ?[+-]\d{2}:\d{2})$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Format(string? raw, TimeZoneInfo? localZone = null)
    {
        var value = (raw ?? "").Trim();
        if (!ExplicitZone.IsMatch(value))
            return value;
        var parsable = value.EndsWith(" UTC", StringComparison.Ordinal)
            ? value[..^4] + "+00:00" : value;
        if (!DateTimeOffset.TryParse(parsable, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var instant))
            return value;
        var local = TimeZoneInfo.ConvertTime(instant, localZone ?? TimeZoneInfo.Local);
        return local.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);
    }
}
