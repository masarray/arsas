namespace ArIED61850Tester.Models;

/// <summary>
/// Strict UI-only attention classification. Source validity is never mutated, and a
/// string merely containing "invalid" in an unrelated description is not an alarm.
/// </summary>
public static class QualityInvalidAttention
{
    public static bool IsInvalid(string? quality)
    {
        var value = quality.AsSpan().Trim();
        return value.StartsWith("Invalid".AsSpan(), StringComparison.OrdinalIgnoreCase) &&
               (value.Length == 7 || value[7] is ' ' or '/' or ':' or '(' or '[');
    }
}
