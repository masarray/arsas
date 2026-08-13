using System.Text.RegularExpressions;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Conservative IEC 61850 reference matcher used by FAT binding. It understands
/// equivalent MMS/SCL spellings (IED-prefixed domains, the DIGSI Application display
/// wrapper, functional-constraint tokens inside MMS references, verified Siemens
/// functional-group/LN display folders, and exact unique object-leaf recovery for
/// incomplete imported references) but never uses fuzzy text similarity.
/// </summary>
internal static class IoTestReferenceMatcher
{
    private static readonly HashSet<string> FunctionalConstraints = new(StringComparer.OrdinalIgnoreCase)
    {
        "ST", "MX", "SP", "SV", "CF", "DC", "SG", "SE", "SR", "OR", "BL", "EX",
        "CO", "RP", "BR", "LG", "GO", "GS", "MS", "US"
    };

    private static readonly HashSet<string> SafeImplicitValueLeaves = new(StringComparer.OrdinalIgnoreCase)
    {
        "stval", "general", "f", "i", "mag.f", "cval.mag.f", "instcval.mag.f", "valwtr.posval"
    };

    internal const int ExactScore = 100;
    internal const int CanonicalScore = 90;
    internal const int ContainerScore = 70;
    internal const int PartialObjectScore = 60;

    internal static int Score(
        string? importedReference,
        string? observedReference,
        string? importedIedName,
        string? observedDeviceName,
        string? observedSclIedName,
        string? logicalNode)
    {
        var importedRaw = NormalizeRaw(importedReference);
        var observedRaw = NormalizeRaw(observedReference);
        if (importedRaw.Length == 0 || observedRaw.Length == 0)
            return 0;

        if (importedRaw.Equals(observedRaw, StringComparison.OrdinalIgnoreCase))
            return ExactScore;

        var importedForms = ImportedForms(importedReference, importedIedName, logicalNode);
        var observedForms = ObservedForms(observedReference, observedDeviceName, observedSclIedName);
        if (importedForms.Overlaps(observedForms))
            return CanonicalScore;

        foreach (var expected in importedForms)
        {
            foreach (var observed in observedForms)
            {
                if (IsSafeImplicitLeafMatch(expected, observed))
                    return ContainerScore;
            }
        }

        // Some customer FAT sheets contain a valid exact data-object name but have
        // lost the LD/LN path, for example `.TCS1Fail`. Recover that case only by an
        // exact IEC object-leaf boundary match. This deliberately scores below every
        // fully attributable form; the caller still requires one unique best candidate,
        // so duplicate leaves in different logical nodes remain ambiguous and blocked.
        foreach (var expected in importedForms)
        {
            foreach (var observed in observedForms)
            {
                if (IsSafePartialObjectMatch(expected, observed))
                    return PartialObjectScore;
            }
        }

        return 0;
    }

    internal static HashSet<string> ImportedForms(
        string? reference,
        string? iedName,
        string? logicalNode)
    {
        var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddCanonicalForms(forms, reference, new[] { iedName });

        foreach (var form in forms.ToArray())
        {
            var collapsed = CollapseVerifiedDisplayHierarchy(form, logicalNode);
            if (!string.IsNullOrWhiteSpace(collapsed))
                forms.Add(collapsed);
        }

        return forms;
    }

    internal static HashSet<string> ObservedForms(
        string? reference,
        string? deviceName,
        string? sclIedName)
    {
        var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddCanonicalForms(forms, reference, new[] { deviceName, sclIedName });
        return forms;
    }

    internal static string NormalizeRaw(string? reference)
    {
        var value = RemoveFunctionalConstraintSuffix(reference);
        if (value.Length == 0)
            return string.Empty;

        value = NormalizeMmsFunctionalConstraint(value);
        value = value.Replace('\\', '/');
        value = Regex.Replace(value, @"/{2,}", "/");
        value = Regex.Replace(value, @"\.{2,}", ".");
        return value.Trim().TrimEnd('.').ToLowerInvariant();
    }

    internal static string NormalizeTelegram(string? reference, string? iedName)
    {
        var normalized = NormalizeRaw(reference);
        if (normalized.Length == 0)
            return string.Empty;

        var slash = normalized.IndexOf('/');
        var name = (iedName ?? string.Empty).Trim().ToLowerInvariant();
        if (slash <= 0 || name.Length == 0)
            return normalized;

        var domain = normalized[..slash];
        if (!domain.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            return normalized;

        var suffix = domain[name.Length..];
        var path = normalized[(slash + 1)..].TrimStart('/');
        if (suffix.Equals("application", StringComparison.OrdinalIgnoreCase))
            return path;

        return suffix.Length == 0 ? path : suffix + "/" + path;
    }

    private static void AddCanonicalForms(
        ISet<string> forms,
        string? reference,
        IEnumerable<string?> iedNames)
    {
        var raw = NormalizeRaw(reference);
        if (raw.Length == 0)
            return;

        forms.Add(raw);
        foreach (var name in iedNames.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var telegram = NormalizeTelegram(raw, name);
            if (telegram.Length > 0)
                forms.Add(telegram);
        }
    }

    private static string NormalizeMmsFunctionalConstraint(string value)
    {
        if (!value.Contains('$'))
            return value;

        var slash = value.IndexOf('/');
        if (slash < 0 || slash >= value.Length - 1)
            return value.Replace('$', '.');

        var domain = value[..(slash + 1)];
        var path = value[(slash + 1)..];
        var tokens = path.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (tokens.Count >= 3 && FunctionalConstraints.Contains(tokens[1]))
            tokens.RemoveAt(1);

        return domain + string.Join('.', tokens);
    }

    private static string RemoveFunctionalConstraintSuffix(string? reference)
    {
        var value = (reference ?? string.Empty).Trim();
        var marker = value.LastIndexOf(" [", StringComparison.Ordinal);
        if (marker > 0 && value.EndsWith(']'))
        {
            var suffix = value[(marker + 2)..^1].Trim();
            if (FunctionalConstraints.Contains(suffix))
                value = value[..marker].TrimEnd();
        }
        return value;
    }

    private static string CollapseVerifiedDisplayHierarchy(string normalizedTelegram, string? logicalNode)
    {
        var value = (normalizedTelegram ?? string.Empty).Trim();
        var verifiedLn = NormalizeRaw(logicalNode).Trim('/');
        if (value.Length == 0 || verifiedLn.Length == 0)
            return value;

        var firstDot = value.IndexOf('.');
        if (firstDot <= 0)
            return value;

        var logicalNodePath = value[..firstDot];
        var lastSlash = logicalNodePath.LastIndexOf('/');
        if (lastSlash <= 0 || lastSlash >= logicalNodePath.Length - 1)
            return value;

        var terminalLn = logicalNodePath[(lastSlash + 1)..];
        if (!terminalLn.Equals(verifiedLn, StringComparison.OrdinalIgnoreCase))
            return value;

        // Collapse only an imported hierarchy whose terminal segment is the verified LN.
        // The observed/live side is never collapsed; this intentionally prevents a path
        // such as AB/GGIO1 from matching a different A/BGGIO1 boundary by accident.
        return logicalNodePath.Replace("/", string.Empty, StringComparison.Ordinal) + value[firstDot..];
    }

    private static bool IsSafeImplicitLeafMatch(string expected, string observed)
    {
        if (expected.Length == 0 || observed.Length <= expected.Length ||
            !observed.StartsWith(expected + ".", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = observed[(expected.Length + 1)..];
        return SafeImplicitValueLeaves.Contains(suffix);
    }

    private static bool IsSafePartialObjectMatch(string expected, string observed)
    {
        var expectedObject = ParsePartialImportedObject(expected);
        if (expectedObject is null)
            return false;

        var observedObject = ParseObservedObject(observed);
        if (observedObject is null ||
            !expectedObject.ObjectName.Equals(observedObject.ObjectName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expectedObject.ValueSuffix.Length > 0)
        {
            return expectedObject.ValueSuffix.Equals(
                observedObject.ValueSuffix,
                StringComparison.OrdinalIgnoreCase);
        }

        return observedObject.ValueSuffix.Length == 0 ||
               SafeImplicitValueLeaves.Contains(observedObject.ValueSuffix);
    }

    private static ObjectLeaf? ParsePartialImportedObject(string value)
    {
        var normalized = (value ?? string.Empty).Trim().TrimStart('.').TrimEnd('.');
        if (normalized.Length == 0 || normalized.Contains('/'))
            return null;

        var stripped = StripSafeValueSuffix(normalized);
        if (stripped.Base.Length == 0 || stripped.Base.Contains('.') || !IsIecIdentifier(stripped.Base))
            return null;

        return new ObjectLeaf(stripped.Base, stripped.Suffix);
    }

    private static ObjectLeaf? ParseObservedObject(string value)
    {
        var normalized = (value ?? string.Empty).Trim().Trim('.');
        if (normalized.Length == 0)
            return null;

        var stripped = StripSafeValueSuffix(normalized);
        var separator = Math.Max(stripped.Base.LastIndexOf('/'), stripped.Base.LastIndexOf('.'));
        var objectName = separator >= 0 ? stripped.Base[(separator + 1)..] : stripped.Base;
        if (!IsIecIdentifier(objectName))
            return null;

        return new ObjectLeaf(objectName, stripped.Suffix);
    }

    private static (string Base, string Suffix) StripSafeValueSuffix(string value)
    {
        foreach (var leaf in SafeImplicitValueLeaves.OrderByDescending(item => item.Length))
        {
            var suffix = "." + leaf;
            if (!value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            return (value[..^suffix.Length], leaf.ToLowerInvariant());
        }

        return (value, string.Empty);
    }

    private static bool IsIecIdentifier(string value)
        => Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    private sealed record ObjectLeaf(string ObjectName, string ValueSuffix);
}
