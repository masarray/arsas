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

        var phaseMatch = PhaseToken().Match(reference);
        if (phaseMatch.Success)
        {
            var phase = phaseMatch.Groups["phase"].Value.ToUpperInvariant();
            var suffix = $"Phs{phase}";
            if (name.EndsWith($" {suffix}", StringComparison.OrdinalIgnoreCase))
                return name;

            return $"{name} {suffix}";
        }

        if (TryExtractLogicalNodeContext(reference, out var logicalNodeClass, out var dataObject)
            && RequiresLogicalNodeOwner(dataObject)
            && name.Equals(dataObject, StringComparison.OrdinalIgnoreCase))
        {
            return $"{logicalNodeClass} {name}";
        }

        return name;
    }

    private static bool TryExtractLogicalNodeContext(
        string reference,
        out string logicalNodeClass,
        out string dataObject)
    {
        logicalNodeClass = string.Empty;
        dataObject = string.Empty;

        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var slashIndex = reference.LastIndexOf('/');
        var tail = slashIndex >= 0 && slashIndex + 1 < reference.Length
            ? reference[(slashIndex + 1)..]
            : reference;

        var dotIndex = tail.IndexOf('.');
        var dollarIndex = tail.IndexOf('$');
        var separatorIndex = dotIndex switch
        {
            >= 0 when dollarIndex >= 0 => Math.Min(dotIndex, dollarIndex),
            >= 0 => dotIndex,
            _ => dollarIndex
        };

        if (separatorIndex <= 0 || separatorIndex + 1 >= tail.Length)
            return false;

        var logicalNodeToken = tail[..separatorIndex].Trim();
        if (!TryExtractLogicalNodeClass(logicalNodeToken, out logicalNodeClass))
            return false;

        var separator = tail[separatorIndex];
        var remainder = tail[(separatorIndex + 1)..];
        if (separator == '$')
        {
            var tokens = remainder.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
                return false;

            var dataObjectIndex = IsFunctionalConstraint(tokens[0]) ? 1 : 0;
            if (dataObjectIndex >= tokens.Length)
                return false;

            dataObject = NormalizeDataObjectToken(tokens[dataObjectIndex]);
        }
        else
        {
            var tokenEnd = remainder.IndexOfAny('.', '$', '[', '(');
            var token = tokenEnd >= 0 ? remainder[..tokenEnd] : remainder;
            dataObject = NormalizeDataObjectToken(token);
        }

        return !string.IsNullOrWhiteSpace(dataObject);
    }

    private static bool TryExtractLogicalNodeClass(string logicalNodeToken, out string logicalNodeClass)
    {
        logicalNodeClass = string.Empty;
        if (string.IsNullOrWhiteSpace(logicalNodeToken))
            return false;

        var token = logicalNodeToken.Trim();
        if (token.Equals("LLN0", StringComparison.OrdinalIgnoreCase))
        {
            logicalNodeClass = "LLN0";
            return true;
        }

        var end = token.Length;
        while (end > 0 && char.IsDigit(token[end - 1]))
            end--;

        if (end == 0)
            return false;

        var withoutInstance = token[..end];
        if (withoutInstance.Length >= 4)
        {
            var candidate = withoutInstance[^4..];
            if (candidate.All(char.IsLetter))
            {
                logicalNodeClass = candidate.ToUpperInvariant();
                return true;
            }
        }

        logicalNodeClass = withoutInstance.ToUpperInvariant();
        return true;
    }

    private static string NormalizeDataObjectToken(string token)
    {
        var value = token.Trim();
        var end = value.IndexOfAny('.', '$', '[', '(');
        return (end >= 0 ? value[..end] : value).Trim();
    }

    private static bool RequiresLogicalNodeOwner(string dataObject)
        => dataObject.ToUpperInvariant() is "POS" or "MOD" or "BEH" or "HEALTH" or "LOC" or "OPCNT";

    private static bool IsFunctionalConstraint(string token)
        => token.ToUpperInvariant() is
            "ST" or "MX" or "CO" or "SP" or "SV" or "CF" or "DC" or "SG" or "SE" or
            "SR" or "OR" or "BL" or "EX" or "RP" or "BR" or "LG" or "GO" or "GS" or
            "MS" or "US";
}
