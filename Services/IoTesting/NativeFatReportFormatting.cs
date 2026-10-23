using System.Globalization;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Customer-facing formatting authority shared by every native FAT report section.
/// Runtime/cache timestamps keep their original identity; only report presentation is
/// converted to the workstation's local time and rendered with one unambiguous format.
/// </summary>
internal static class NativeFatReportFormatting
{
    internal const string LocalTimestampFormat = "dd/MM/yyyy HH:mm:ss.fff";

    internal static string LocalTimestamp(DateTimeOffset value)
        => value.ToLocalTime().ToString(LocalTimestampFormat, CultureInfo.InvariantCulture);

    internal static string LocalTimestamp(DateTimeOffset? value)
        => value.HasValue ? LocalTimestamp(value.Value) : "—";

    internal static string LocalTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";

        var text = value.Trim();
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var timestamp)
            ? LocalTimestamp(timestamp)
            : text;
    }

    internal static string Quality(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "—";

        var text = value.Trim();
        return text.Equals("good", StringComparison.OrdinalIgnoreCase) ? "Good" : text;
    }
}
