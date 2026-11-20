using System.Globalization;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Typed scalar editor: converts operator input only after a live GVA preview.
/// No guessed string-to-IEC encoding, no overflow, no culture-dependent
/// decimal separator and no silent Boolean/enum coercion.
/// </summary>
public static class IedBrowserTypedWriteInput
{
    public static bool TryParse(
        MmsTypeSpecificationNode? type,
        string? text,
        out MmsDataValue? value,
        out string error)
    {
        value = null;
        error = "";
        if (type is null)
        {
            error = "Live GVA type has not been inspected.";
            return false;
        }
        var input = (text ?? "").Trim();
        switch (type.MmsType)
        {
            case "boolean":
                if (input.Equals("true", StringComparison.OrdinalIgnoreCase))
                    value = MmsDataValue.Boolean(true);
                else if (input.Equals("false", StringComparison.OrdinalIgnoreCase))
                    value = MmsDataValue.Boolean(false);
                else error = "Enter true or false; 1/0 are not auto-coerced.";
                break;
            case "integer":
                if (long.TryParse(input, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var signed))
                    value = MmsDataValue.Integer(signed);
                else error = "Enter a signed decimal integer.";
                break;
            case "unsigned":
                if (ulong.TryParse(input, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var unsigned))
                    value = MmsDataValue.Unsigned(unsigned);
                else error = "Enter a non-negative unsigned decimal integer.";
                break;
            case "floating-point" when type.Size == 32:
                if (float.TryParse(input, NumberStyles.AllowLeadingSign |
                    NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                    CultureInfo.InvariantCulture, out var number) && float.IsFinite(number))
                    value = MmsDataValue.FloatingPoint(number);
                else error = "Enter a finite, invariant-culture FLOAT32 number (use '.').";
                break;
            case "visible-string":
                value = MmsDataValue.VisibleString(text ?? "");
                break;
            case "mms-string":
                value = MmsDataValue.MmsString(text ?? "");
                break;
            default:
                error = $"Typed text input for {type.MmsType} is not qualified.";
                break;
        }
        if (value is null) return false;
        // Admission again validates exact range, byte widths and string sizes.
        return true;
    }
}
