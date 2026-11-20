namespace ArIED61850Tester.Services;

/// <summary>
/// Single, side-effect-free operator presentation boundary for two-bit MMS
/// status values. The raw association ledger, quality flags, event ordering and
/// typed decoder are not changed. DPC positions use their own CDC-aware path.
/// </summary>
internal static class Iec61850TwoBitStatusPresentation
{
    internal static bool TryFormat(
        string wireValue,
        string? dataType,
        string? category,
        string? reference,
        out string display)
    {
        display = string.Empty;

        // Metadata authority is required. A Measurement that happens to be two
        // bits must not inherit a process-state meaning from its byte shape.
        if (!string.Equals(category?.Trim(), "Status", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = reference.AsSpan().Trim();
        if (!path.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith("$stVal", StringComparison.OrdinalIgnoreCase))
            return false;

        // Fail closed when declared MMS/IEC type contradicts a two-bit status.
        // Unknown is a legacy incomplete-model state, not a vendor exception.
        if (!IsBitStringOrIncompleteType(dataType))
            return false;

        if (!Iec61850ValueFormatter.TryNormalizeTwoBitWireStatus(wireValue, out var code))
            return false;

        display = code switch
        {
            0 => "Bits [00]",
            1 => "Bits [01]",
            2 => "Bits [10]",
            3 => "Bits [11]",
            _ => string.Empty
        };
        return display.Length != 0;
    }

    private static bool IsBitStringOrIncompleteType(string? dataType)
    {
        var type = dataType?.Trim();
        return string.IsNullOrEmpty(type) ||
            type.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("BitString", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Bit String", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Bits", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("MmsBitString", StringComparison.OrdinalIgnoreCase);
    }
}
