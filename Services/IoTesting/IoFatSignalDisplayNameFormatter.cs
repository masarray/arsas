using System.Text.RegularExpressions;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Creates a customer-readable FAT signal label without changing the persisted IEC identity.
/// Phase information can live in different imported fields depending on whether the source is
/// SCL, a static DataSet member, a live scalar leaf, or a report display reference. Therefore
/// the formatter inspects the complete DO/DA identity set instead of trusting one collapsed
/// reference. ObjectReference/PointKey/FCDA/report binding are presentation-independent.
/// </summary>
public static partial class IoFatSignalDisplayNameFormatter
{
    [GeneratedRegex(@"(?:^|[.$/])phs(?<phase>AB|BC|CA|A|B|C)(?:$|[.$/])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhaseToken();

    [GeneratedRegex(@"(?:^|[.$/])(?<phase>neut)(?:$|[.$/])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NeutralToken();

    public static string Format(IoTestPointPlan point)
    {
        ArgumentNullException.ThrowIfNull(point);

        return FormatFromCandidates(
            point.SignalName,
            point.DataAttribute,
            point.ReportIecReference,
            point.SourceIecReference,
            point.ReportDisplayReference,
            point.EventLogSearchReference,
            point.ObjectReference,
            point.SignalAddress);
    }

    public static string Format(string? signalName, string? iecReference)
        => FormatFromCandidates(signalName, iecReference);

    internal static string FormatFromCandidates(string? signalName, params string?[] identityCandidates)
    {
        var name = string.IsNullOrWhiteSpace(signalName) ? "Signal" : signalName.Trim();

        foreach (var candidate in identityCandidates)
        {
            if (!TryGetPhaseSuffix(candidate, out var suffix))
                continue;

            if (name.EndsWith($" {suffix}", StringComparison.OrdinalIgnoreCase))
                return name;

            return $"{name} {suffix}";
        }

        return name;
    }

    private static bool TryGetPhaseSuffix(string? identity, out string suffix)
    {
        suffix = string.Empty;
        if (string.IsNullOrWhiteSpace(identity))
            return false;

        var text = identity.Trim();
        var match = PhaseToken().Match(text);
        if (match.Success)
        {
            suffix = $"Phs{match.Groups["phase"].Value.ToUpperInvariant()}";
            return true;
        }

        if (NeutralToken().IsMatch(text))
        {
            suffix = "Neut";
            return true;
        }

        return false;
    }
}
