namespace ArIED61850Tester.Services.IoTesting;

internal static class IoFatValuePresentation
{
    /// <summary>
    /// FAT uses one stable Boolean presentation from the first frame onward. Formatting-only
    /// changes such as false -> False or true -> True must never look like process changes.
    /// Non-Boolean IEC values (Open/Closed, DbPos, enum text, analog values) are preserved.
    /// </summary>
    internal static string Canonicalize(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
            return "-";

        if (bool.TryParse(text, out var boolean))
            return boolean ? "True" : "False";

        return text;
    }

    internal static bool IsFormattingOnlyBooleanChange(string? left, string? right)
    {
        if (!bool.TryParse((left ?? string.Empty).Trim(), out var leftBoolean) ||
            !bool.TryParse((right ?? string.Empty).Trim(), out var rightBoolean))
        {
            return false;
        }

        return leftBoolean == rightBoolean;
    }
}
