using System.Text.RegularExpressions;

namespace ArIED61850Tester.Services;

/// <summary>
/// Final consumer-side safety boundary for event-driven report values.
/// The IEC 61850 engine remains authoritative for report decoding, but a report
/// sample that is impossible for the selected signal type must never overwrite a
/// previously verified process value or create a false SOE edge. Rejected report
/// samples are left to the existing MMS verification/fallback path.
/// </summary>
public static class ReportProcessValueSafety
{
    private static readonly Regex DbposBitString = new(
        @"^\s*bits\(\s*(?:0x)?[0-9a-f]{2}\s*,\s*unused\s*=\s*6\s*\)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsSafe(
        string? rawValue,
        string? formattedValue,
        string? dataType,
        string? reference,
        out string rejectionReason)
    {
        rejectionReason = string.Empty;
        var raw = (rawValue ?? string.Empty).Trim();
        var formatted = (formattedValue ?? string.Empty).Trim();
        var type = (dataType ?? string.Empty).Trim();
        var signalReference = (reference ?? string.Empty).Trim();

        if (raw.Length == 0 || formatted.Length == 0)
            return true;

        var rawIsBits = raw.StartsWith("bits(", StringComparison.OrdinalIgnoreCase);
        var formattedIsBits = formatted.StartsWith("bits(", StringComparison.OrdinalIgnoreCase);
        var rawIsContainer = IsContainer(raw);
        var formattedIsContainer = IsContainer(formatted);

        if (IsBoolean(type))
        {
            if (rawIsBits || formattedIsBits)
                return Reject($"Boolean signal {signalReference} received a BIT STRING report value.", out rejectionReason);
            if (formattedIsContainer)
                return Reject($"Boolean signal {signalReference} remained a structured report value after scalar projection.", out rejectionReason);
            return true;
        }

        if (IsDbpos(type))
        {
            // A DPC/Dbpos process value is exactly two significant bits in one MMS
            // BIT STRING octet (unused=6). Larger bitmaps are report metadata such as
            // inclusion/OptFlds and must never be accepted as the process state.
            if (rawIsBits && !DbposBitString.IsMatch(raw))
                return Reject($"DPC/Dbpos signal {signalReference} received a non-2-bit BIT STRING report value.", out rejectionReason);
            if (formattedIsBits)
                return Reject($"DPC/Dbpos signal {signalReference} could not normalize its BIT STRING report value.", out rejectionReason);
            if (formattedIsContainer)
                return Reject($"DPC/Dbpos signal {signalReference} remained a structured report value after scalar projection.", out rejectionReason);
            return true;
        }

        if (IsNativeBitString(type))
            return true;

        if (IsScalar(type))
        {
            if (formattedIsBits)
                return Reject($"Scalar signal {signalReference} received report BIT STRING metadata instead of its process value.", out rejectionReason);
            if (formattedIsContainer)
                return Reject($"Scalar signal {signalReference} remained a structured/array report value after projection.", out rejectionReason);
        }

        // If metadata names are absent or vendor-specific, do not invent a type.
        // The engine's strict frame mapper remains the primary authority.
        // We only fail closed where ARSAS already has enough signal typing evidence.
        _ = rawIsContainer;
        return true;
    }

    private static bool Reject(string reason, out string rejectionReason)
    {
        rejectionReason = reason;
        return false;
    }

    private static bool IsContainer(string value)
        => value.StartsWith("Structure(", StringComparison.OrdinalIgnoreCase) ||
           value.StartsWith("Struct(", StringComparison.OrdinalIgnoreCase) ||
           value.StartsWith("Array(", StringComparison.OrdinalIgnoreCase);

    private static bool IsBoolean(string dataType)
    {
        var normalized = dataType.Trim().ToLowerInvariant();
        return normalized is "bool" or "boolean" or "sps" or "singlepointstatus";
    }

    private static bool IsDbpos(string dataType)
    {
        var normalized = dataType.Trim().ToLowerInvariant();
        return normalized is "dbpos" or "dpc" or "doublepointstatus";
    }

    private static bool IsNativeBitString(string dataType)
    {
        var normalized = dataType.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        return normalized.Contains("bitstring", StringComparison.Ordinal) ||
               normalized is "bit-string" or "bits";
    }

    private static bool IsScalar(string dataType)
    {
        var normalized = dataType.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            return false;

        return IsBoolean(dataType) || IsDbpos(dataType) ||
               normalized.Contains("int", StringComparison.Ordinal) ||
               normalized.Contains("uint", StringComparison.Ordinal) ||
               normalized.Contains("float", StringComparison.Ordinal) ||
               normalized.Contains("double", StringComparison.Ordinal) ||
               normalized.Contains("decimal", StringComparison.Ordinal) ||
               normalized.Contains("counter", StringComparison.Ordinal) ||
               normalized.Contains("bcr", StringComparison.Ordinal) ||
               normalized is "enum" or "enumerated" or "quality" or "timestamp";
    }
}
