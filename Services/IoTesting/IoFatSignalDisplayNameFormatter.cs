using System.Text.RegularExpressions;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Creates a customer-readable signal label without changing the persisted SCL identity.
/// The IEC reference remains the authority; this formatter only enriches the short display
/// label with the IEC 61850 phase context proven by the structured DO/DA path.
/// </summary>
public static partial class IoFatSignalDisplayNameFormatter
{
    [GeneratedRegex(@"(?:^|[.$/])phs(?<phase>AB|BC|CA|A|B|C)(?:$|[.$/])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhaseToken();

    public static string Format(IoTestPointPlan point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return Format(point.SignalName, point.ReportIecReference);
    }

    public static string Format(string? signalName, string? iecReference)
    {
        var name = string.IsNullOrWhiteSpace(signalName) ? "Signal" : signalName.Trim();
        var reference = iecReference?.Trim() ?? string.Empty;
        var match = PhaseToken().Match(reference);
        if (!match.Success)
            return name;

        var phase = match.Groups["phase"].Value.ToUpperInvariant();
        var suffix = $"Phs{phase}";

        // Keep the Data Object family exactly as the IED/SCL names it (A, ThdA, ThdPPV,
        // etc.) and append only the semantic phase context. This is deliberately a display
        // transformation: ReportIecReference/PointKey/FCDA identity are never rewritten.
        if (name.EndsWith($" {suffix}", StringComparison.OrdinalIgnoreCase))
            return name;

        return $"{name} {suffix}";
    }
}
