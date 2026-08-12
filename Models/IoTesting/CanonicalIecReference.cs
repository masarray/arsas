using System.Text;

namespace ArIED61850Tester.Models.IoTesting;

/// <summary>
/// Semantic IEC 61850 reference independent of the display spelling used by an
/// IO-list, SCL browser, or MMS model. The Application wrapper is retained for
/// traceability, while semantic comparison intentionally ignores that wrapper.
/// </summary>
public sealed record CanonicalIecReference(
    string Ied,
    string ApplicationWrapper,
    string LogicalDevice,
    string LogicalNode,
    string DataObject,
    string DataAttribute,
    string FunctionalConstraint)
{
    private static readonly HashSet<string> FunctionalConstraints = new(StringComparer.OrdinalIgnoreCase)
    {
        "ST", "MX", "CO", "CF", "DC", "SP", "SE", "SV", "EX"
    };

    public bool IsWeak => string.IsNullOrWhiteSpace(LogicalDevice) ||
                          string.IsNullOrWhiteSpace(LogicalNode);

    public string SemanticKey => JoinKey(Ied, LogicalDevice, LogicalNode, DataObject, DataAttribute, FunctionalConstraint);

    public string DisplayPath => string.Join("/", new[]
    {
        Ied,
        ApplicationWrapper,
        LogicalDevice,
        LogicalNode,
        DataObject,
        DataAttribute
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public static bool TryParse(
        string? reference,
        string? expectedIed,
        string? fallbackFunctionalConstraint,
        out CanonicalIecReference parsed)
    {
        parsed = Empty;
        var text = RemoveFunctionalConstraintSuffix(reference);
        if (text.Length == 0)
            return false;

        var ied = Clean(expectedIed);
        var application = string.Empty;
        var logicalDevice = string.Empty;
        var logicalNode = string.Empty;
        var path = text;
        var slash = text.IndexOf('/');
        if (slash > 0)
        {
            var domain = text[..slash].Trim();
            path = text[(slash + 1)..].Trim();
            var domainLower = domain.ToLowerInvariant();
            var expectedLower = ied.ToLowerInvariant();
            if (expectedLower.Length > 0 && domainLower.StartsWith(expectedLower, StringComparison.Ordinal))
            {
                var suffix = domain[ied.Length..];
                ied = domain[..ied.Length];
                if (suffix.Equals("Application", StringComparison.OrdinalIgnoreCase))
                    application = suffix;
                else if (suffix.Length > 0)
                    logicalDevice = suffix;
            }
            else if (domain.EndsWith("Application", StringComparison.OrdinalIgnoreCase))
            {
                application = "Application";
                if (ied.Length == 0)
                    ied = domain[..^"Application".Length];
            }
            else
            {
                logicalDevice = domain;
            }
        }

        var tokens = Tokenize(path);
        if (tokens.Count == 0)
            return false;

        var fc = Clean(fallbackFunctionalConstraint);
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            if (!FunctionalConstraints.Contains(tokens[i]))
                continue;
            fc = tokens[i].ToUpperInvariant();
            tokens.RemoveAt(i);
        }

        // A path may carry LD/LN as slash-separated segments, or as the common
        // MMS display form LD/LN.DO.DA. Preserve the first segment before LN.
        if (logicalDevice.Length == 0 && slash > 0 && tokens.Count >= 2 &&
            (application.Length == 0 || tokens.Count >= 3))
        {
            logicalDevice = tokens[0];
            tokens.RemoveAt(0);
        }
        if (tokens.Count >= 2 && LooksLikeLogicalNode(tokens[0]))
        {
            logicalNode = tokens[0];
            tokens.RemoveAt(0);
        }

        // When LN is not standard-looking, retain it anyway for a full path;
        // vendor names must not cause the LD to be discarded.
        if (logicalNode.Length == 0 && tokens.Count >= 3)
        {
            logicalNode = tokens[0];
            tokens.RemoveAt(0);
        }

        var dataObject = tokens.Count > 1 ? tokens[^2] : tokens[0];
        var dataAttribute = tokens.Count > 1 ? tokens[^1] : string.Empty;
        parsed = new CanonicalIecReference(
            Clean(ied),
            Clean(application),
            Clean(logicalDevice),
            Clean(logicalNode),
            Clean(dataObject),
            Clean(dataAttribute),
            fc.ToUpperInvariant());
        return parsed.DataObject.Length > 0;
    }

    public static bool AreEquivalent(
        string? imported,
        string? importedIed,
        string? importedFunctionalConstraint,
        string? observed,
        string? observedIed,
        string? observedFunctionalConstraint)
    {
        if (!TryParse(imported, importedIed, importedFunctionalConstraint, out var left) ||
            !TryParse(observed, observedIed, observedFunctionalConstraint, out var right))
            return false;

        if (!Equal(left.Ied, right.Ied) ||
            !Equal(left.DataObject, right.DataObject) ||
            !Equal(left.DataAttribute, right.DataAttribute) ||
            !EqualOptional(left.FunctionalConstraint, right.FunctionalConstraint))
            return false;

        // A weak workbook reference such as .TCS1Fail is valid when its unique
        // live DO/DA is known. Do not invent an LN/LD when both sides provide one.
        return (left.IsWeak || right.IsWeak ||
                (Equal(left.LogicalDevice, right.LogicalDevice) && Equal(left.LogicalNode, right.LogicalNode)));
    }

    public static string Diagnostics(
        IEnumerable<string> imported,
        string ied,
        string functionalConstraint,
        IEnumerable<string> liveReferences)
    {
        var importedList = imported.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var canonical = importedList
            .Select(value => TryParse(value, ied, functionalConstraint, out var parsed) ? parsed.DisplayPath + (parsed.FunctionalConstraint.Length > 0 ? $" [{parsed.FunctionalConstraint}]" : string.Empty) : "(unparsed)")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var closest = liveReferences
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();

        return $"Imported: {Format(importedList)}\nCanonical imported: {Format(canonical)}\nClosest live references: {Format(closest)}";
    }

    private static readonly CanonicalIecReference Empty = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    private static List<string> Tokenize(string value)
        => value.Replace('$', '.').Replace('/', '.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static string RemoveFunctionalConstraintSuffix(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        var marker = text.LastIndexOf(" [", StringComparison.Ordinal);
        return marker > 0 && text.EndsWith(']') ? text[..marker].TrimEnd() : text;
    }

    private static bool LooksLikeLogicalNode(string value)
        => value.Length >= 3 && value.Any(char.IsLetter) &&
           (value.StartsWith("LLN", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("LPHD", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("GGIO", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("CSWI", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("XCBR", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("XSWI", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("MMXU", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("MMXN", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("PTOC", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("PTRC", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("Q0_", StringComparison.OrdinalIgnoreCase));

    private static string Clean(string? value) => (value ?? string.Empty).Trim().Trim('/');
    private static bool Equal(string left, string right) => left.Equals(right, StringComparison.OrdinalIgnoreCase);
    private static bool EqualOptional(string left, string right) => left.Length == 0 || right.Length == 0 || Equal(left, right);
    private static string JoinKey(params string[] values) => string.Join("|", values.Select(value => value.Trim().ToLowerInvariant()));
    private static string Format(IReadOnlyCollection<string> values) => values.Count == 0 ? "(none)" : string.Join("; ", values);
}
