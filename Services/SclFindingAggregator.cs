using AR.Iec61850.Scl.Workspace;

namespace ArIED61850Tester.Services;

internal sealed class SclFindingGroup
{
    public string Severity { get; init; } = "Info";
    public string Code { get; init; } = "SCL_UNKNOWN";
    public string Scope { get; init; } = "document";
    public string RepresentativeMessage { get; init; } = string.Empty;
    public IReadOnlyList<string> ExampleReferences { get; init; } = Array.Empty<string>();
    public int Count { get; init; }

    public string ToDiagnosticMessage()
    {
        if (Count <= 1)
            return RepresentativeMessage;

        var examples = ExampleReferences.Count == 0
            ? string.Empty
            : $" Examples: {string.Join(", ", ExampleReferences)}.";
        return $"{Count} occurrences under {Scope}. {RepresentativeMessage}{examples}";
    }
}

internal static class SclFindingAggregator
{
    public static IReadOnlyList<SclFindingGroup> Group(
        IReadOnlyList<SclWorkspaceFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        return findings
            .GroupBy(
                finding => new FindingKey(
                    NormalizeCode(finding.Code),
                    NormalizeScope(ResolveScope(finding.ObjectReference))),
                FindingKeyComparer.Instance)
            .Select(group => BuildGroup(group.Key, group))
            .OrderByDescending(group => SeverityRank(group.Severity))
            .ThenBy(group => group.Code, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsBlockingSeverity(string? severity)
        => SeverityRank(severity) >= 3;

    public static string ToLogLevel(string? severity)
        => SeverityRank(severity) >= 3
            ? "ERROR"
            : SeverityRank(severity) == 2 ? "WARN" : "INFO";

    private static SclFindingGroup BuildGroup(
        FindingKey key,
        IEnumerable<SclWorkspaceFinding> findings)
    {
        var items = findings.ToArray();
        var representative = items
            .OrderByDescending(item => SeverityRank(item.Severity))
            .ThenBy(item => item.Message, StringComparer.OrdinalIgnoreCase)
            .First();
        var scope = ResolveScope(representative.ObjectReference);
        if (scope.Equals("document", StringComparison.OrdinalIgnoreCase))
            scope = key.Scope;

        return new SclFindingGroup
        {
            Severity = representative.Severity,
            Code = string.IsNullOrWhiteSpace(representative.Code)
                ? key.Code
                : representative.Code.Trim(),
            Scope = string.IsNullOrWhiteSpace(scope) ? "document" : scope,
            RepresentativeMessage = representative.Message,
            Count = items.Length,
            ExampleReferences = items
                .Select(item => item.ObjectReference?.Trim() ?? string.Empty)
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray()
        };
    }

    private static string ResolveScope(string? objectReference)
    {
        var reference = objectReference?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reference))
            return "document";

        var slash = reference.IndexOf('/');
        if (slash > 0)
            return reference[..slash];

        var dollar = reference.IndexOf('$');
        if (dollar > 0)
            return reference[..dollar];

        return reference;
    }

    private static string NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code)
            ? "SCL_UNKNOWN"
            : code.Trim().ToUpperInvariant();

    private static string NormalizeScope(string? scope)
        => string.IsNullOrWhiteSpace(scope)
            ? "DOCUMENT"
            : scope.Trim().ToUpperInvariant();

    private static int SeverityRank(string? severity)
        => severity?.Trim().ToUpperInvariant() switch
        {
            "ERROR" or "HIGH" => 3,
            "WARNING" or "WARN" => 2,
            "INFO" or "INFORMATION" => 1,
            _ => 0
        };

    private readonly record struct FindingKey(string Code, string Scope);

    private sealed class FindingKeyComparer : IEqualityComparer<FindingKey>
    {
        public static FindingKeyComparer Instance { get; } = new();

        public bool Equals(FindingKey left, FindingKey right)
            => string.Equals(left.Code, right.Code, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.Scope, right.Scope, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(FindingKey key)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Code),
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Scope));
    }
}
