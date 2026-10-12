using System.Globalization;
using ArControl = AR.Iec61850.Control;

namespace ArIED61850Tester.Services;

/// <summary>
/// IEC 61850 BSC ctlVal is directional Tcmd (0 stop, 1 lower, 2 higher,
/// 3 reserved). Not a numeric tap-position setpoint or a DPC Open/Close.
/// No wire request is built without the exact allowed live MMS type.
/// </summary>
internal static class Iec61850BscStepControlPolicy
{
    internal static bool TryBind(
        string mmsType, int? bitWidth, string command,
        out ArControl.Iec61850ControlValue? value,
        out string expected, out string error)
    {
        value = null;
        expected = string.Empty;
        error = string.Empty;
        var supported = mmsType is "integer" or "unsigned" ||
                        (mmsType == "bit-string" && bitWidth == 2);
        if (!supported)
        {
            error = $"BSC Tcmd is not safely supported by live MMS ctlVal type '{mmsType}' (bits={bitWidth}); no control write was sent.";
            return false;
        }

        var direction = (command ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "raise" or "higher" or "up" => 2L,
            "lower" or "down" => 1L,
            _ => -1L
        };
        if (direction < 0)
        {
            error = "BSC TapChg accepts only Raise or Lower. It does not accept tap-position setpoints, Open/Close, Stop, or reserved Tcmd values.";
            return false;
        }

        value = mmsType == "unsigned"
            ? ArControl.Iec61850ControlValue.Unsigned((ulong)direction)
            : ArControl.Iec61850ControlValue.Integer(direction);
        expected = direction == 2 ? "Raise" : "Lower";
        return true;
    }

    internal static bool MovementConfirmed(string requested, string? before, string? after)
    {
        if (!TryExactNumericFeedback(before, out var baseline) ||
            !TryExactNumericFeedback(after, out var observed))
            return false;

        return requested.Trim().ToLowerInvariant() switch
        {
            "raise" or "higher" or "up" => observed > baseline,
            "lower" or "down" => observed < baseline,
            _ => false
        };
    }

    private static bool TryExactNumericFeedback(string? text, out decimal result)
        => decimal.TryParse(text?.Trim(),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out result);
}
