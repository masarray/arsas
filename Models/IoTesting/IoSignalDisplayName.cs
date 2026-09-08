using System.Text.RegularExpressions;

namespace ArIED61850Tester.Models.IoTesting;

/// <summary>
/// Canonical ARSAS-owned presentation rule for operator-facing IEC 61850 signal names.
/// Technical identity is never rewritten: callers must continue to persist/use the original
/// IEC reference for binding, FCDA membership, report traceability, and evidence matching.
/// </summary>
public static partial class IoSignalDisplayName
{
    [GeneratedRegex(@"(?:^|[.$/])phs(?<phase>AB|BC|CA|A|B|C)(?:$|[.$/])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhaseToken();

    public static string Format(string? signalName, string? iecReference)
    {
        var name = string.IsNullOrWhiteSpace(signalName) ? "Signal" : signalName.Trim();
        var reference = iecReference?.Trim() ?? string.Empty;
        var match = PhaseToken().Match(reference);
        if (!match.Success)
            return name;

        var phase = match.Groups["phase"].Value.ToUpperInvariant();
        var suffix = $"Phs{phase}";
        if (name.EndsWith($" {suffix}", StringComparison.OrdinalIgnoreCase))
            return name;

        return $"{name} {suffix}";
    }
}
