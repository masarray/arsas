using System.Globalization;
using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Display-only IEC status decoding of an already observed exact FC=ST leaf.
/// Generic SCL synthesized labels such as value1 are NOT the normative IEC
/// Beh/Mod/Health symbols. Never touch raw canonical MMS/Report observations.
/// </summary>
public static class IedBrowserStandardStatusPresentation
{
    public static string Format(string reference, string fc, string raw)
    {
        if (!string.Equals(fc, "ST", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(raw))
            return raw;
        var path = IedBrowserLiveValues.CanonicalReference(reference);
        var slash = path.IndexOf('/');
        if (slash < 1) return raw;
        var parts = path[(slash + 1)..].Split('.');
        if (parts.Length != 3 || !string.Equals(parts[2], "stVal", StringComparison.Ordinal))
            return raw;
        var obj = parts[1];
        if (obj is not ("Mod" or "Beh" or "Health" or "PhyHealth"))
            return raw;
        var input = raw.Trim();
        if (input.StartsWith("value", StringComparison.OrdinalIgnoreCase))
            input = input["value".Length..];
        if (!long.TryParse(input, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var ordinal))
            return raw;
        if (!Iec61850StandardEnumRegistry.TryResolve(
                "", obj, obj is "Health" or "PhyHealth" ? "ENS" : "ENC",
                "stVal", out var definition))
            return raw;
        var value = definition.Values.FirstOrDefault(v => v.Ord == ordinal);
        return value is null ? raw : value.Symbol switch
        {
            "ok" => "Ok",
            var text => text
        };
    }
}
