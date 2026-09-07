using System.Text.RegularExpressions;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Creates a customer-readable signal label without changing the persisted SCL identity.
/// The IEC reference remains the authority; this formatter only enriches the short display
/// label with a phase suffix when the reference proves one exists.
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

        // ThdA/ThdB/ThdC are commonly emitted by SCL tools as the family name even when
        // the leaf itself is phase-qualified. Avoid the awkward "ThdA Phs A" duplication.
        if (name.Equals("ThdA", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ThdB", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ThdC", StringComparison.OrdinalIgnoreCase))
        {
            return $"Thd Phs {phase}";
        }

        if (name.Equals("ThdPPV", StringComparison.OrdinalIgnoreCase))
            return $"Thd PPV Phs {phase}";

        if (name.EndsWith($" Phs {phase}", StringComparison.OrdinalIgnoreCase))
            return name;

        return $"{name} Phs {phase}";
    }
}
