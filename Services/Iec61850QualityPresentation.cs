using ArIED61850Tester.Protocol.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Operator presentation over verified Quality evidence. Raw MMS text remains
/// in the observation ledger; absence of individual flags is not false.
/// </summary>
public static class Iec61850QualityPresentation
{
    public static string FormatObserved(string? observed, string evidenceField = "")
    {
        if (MmsQualityDecoder.TryDecodeRendered(observed, out var typed))
            return typed.FormatField(evidenceField);

        var value = (observed ?? "").Trim();
        // Legacy report projection may retain only the validity word. It is
        // not evidence for any other Quality bit (including a zero bit).
        var separator = value.IndexOf(" / ", StringComparison.Ordinal);
        var validity = separator < 0 ? value : value[..separator];
        if (validity.Equals("Good", StringComparison.OrdinalIgnoreCase) ||
            validity.Equals("Invalid", StringComparison.OrdinalIgnoreCase) ||
            validity.Equals("Reserved", StringComparison.OrdinalIgnoreCase) ||
            validity.Equals("Questionable", StringComparison.OrdinalIgnoreCase))
        {
            if (evidenceField == "Validity")
                return validity.ToLowerInvariant();
            return evidenceField.Length == 0 ? value : "Not exposed";
        }

        // The malformed renderer text is still preserved for diagnostics,
        // never presented as a trusted live IEC state.
        return evidenceField.Length == 0 &&
            value.Length > 0 && !value.StartsWith("bits(", StringComparison.OrdinalIgnoreCase)
                ? value : "Not exposed";
    }
}
