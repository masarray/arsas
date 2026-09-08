using System.Text.RegularExpressions;

namespace ArIED61850Tester.Models.IoTesting;

/// <summary>
/// Canonical ARSAS-owned presentation rule for operator-facing IEC 61850 signal names.
/// Technical identity is never rewritten: callers must continue to persist/use the original
/// IEC reference for binding, FCDA membership, report traceability, and evidence matching.
/// </summary>
public static partial class IoSignalDisplayName
{
    private static readonly HashSet<string> OwnerQualifiedLogicalNodeClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "XCBR",
        "XSWI",
        "CSWI",
        "CILO"
    };

    [GeneratedRegex(@"(?:^|[.$/])phs(?<phase>AB|BC|CA|A|B|C)(?:$|[.$/])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhaseToken();

    [GeneratedRegex(@"(?<class>[A-Z]{4})(?<instance>\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalNodeClassToken();

    public static string Format(string? signalName, string? iecReference)
        => Format(signalName, iecReference, logicalNode: null, dataObject: null, dataAttribute: null);

    /// <summary>
    /// Builds one compact engineering display name from the canonical signal identity.
    /// FAT may provide parsed LN/DO/DA metadata while Engineering may provide only a complete
    /// IEC reference; both routes intentionally converge on this same presentation rule.
    /// </summary>
    public static string Format(
        string? signalName,
        string? iecReference,
        string? logicalNode,
        string? dataObject,
        string? dataAttribute)
    {
        var name = string.IsNullOrWhiteSpace(signalName) ? "Signal" : signalName.Trim();
        var reference = iecReference?.Trim() ?? string.Empty;

        // Phase is DA/sub-DO context. FAT imports can retain it either in the canonical
        // source reference or in DataAttribute even when a report/event lookup reference
        // was normalized to a shorter form.
        var phaseSource = string.IsNullOrWhiteSpace(dataAttribute)
            ? reference
            : reference + "." + dataAttribute.Trim();
        var phaseMatch = PhaseToken().Match(phaseSource);
        if (phaseMatch.Success)
        {
            var phase = phaseMatch.Groups["phase"].Value.ToUpperInvariant();
            var suffix = $"Phs{phase}";
            if (!name.EndsWith($" {suffix}", StringComparison.OrdinalIgnoreCase))
                name = $"{name} {suffix}";
        }

        // Position/control objects are not human-unique without their owning LN class.
        // Keep measurement names compact (A PhsA, PhV PhsB, Hz), but qualify switching
        // and interlocking objects such as XCBR.Pos and CSWI.Pos.
        var ownerClass = ResolveLogicalNodeClass(logicalNode, reference);
        if (ownerClass.Length > 0 &&
            OwnerQualifiedLogicalNodeClasses.Contains(ownerClass) &&
            !name.StartsWith(ownerClass, StringComparison.OrdinalIgnoreCase))
        {
            name = $"{ownerClass} {name}";
        }

        return name;
    }

    private static string ResolveLogicalNodeClass(string? logicalNode, string reference)
    {
        var explicitClass = ExtractLogicalNodeClass(logicalNode);
        if (explicitClass.Length > 0)
            return explicitClass;

        if (reference.Length == 0)
            return string.Empty;

        foreach (var token in reference.Split(['/', '$', '.'], StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = ExtractLogicalNodeClass(token);
            if (candidate.Length > 0)
                return candidate;
        }

        return string.Empty;
    }

    private static string ExtractLogicalNodeClass(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var match = LogicalNodeClassToken().Match(value.Trim().ToUpperInvariant());
        return match.Success ? match.Groups["class"].Value : string.Empty;
    }
}
