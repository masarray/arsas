namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Keeps FAT file-name checks explicit about comparison semantics when the BCL only
/// exposes the char StartsWith overload without StringComparison.
/// </summary>
internal static class IoFatStringCompatibilityExtensions
{
    public static bool StartsWith(this string value, char prefix, StringComparison comparison)
        => value.StartsWith(prefix.ToString(), comparison);
}
