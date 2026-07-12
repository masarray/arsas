using System;
using System.Linq;

namespace ArIED61850Tester.Protocol.Mms;

public readonly record struct MmsObjectReference(string Domain, string Item, string FunctionalConstraint)
{
    public string LogicalDevice => Domain;
    public string Path => Item.Replace('$', '.');

    public static MmsObjectReference Parse(string reference, string functionalConstraint)
        => FromIec61850Reference(reference, functionalConstraint);

    public static MmsObjectReference FromIec61850Reference(string reference, string functionalConstraint)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("IEC 61850 object reference is empty.", nameof(reference));

        var fc = functionalConstraint?.Trim() ?? string.Empty;
        var normalized = CanonicalizeIecReferenceCase(reference.Trim().Replace('$', '.'));
        var slash = normalized.IndexOf('/');
        if (slash <= 0 || slash >= normalized.Length - 1)
            return new MmsObjectReference(string.Empty, normalized.Replace('.', '$'), fc);

        var domain = normalized[..slash];
        var path = normalized[(slash + 1)..];
        var item = BuildMmsItemName(path, fc);
        return new MmsObjectReference(domain, item, fc);
    }

    private static string BuildMmsItemName(string dottedPath, string functionalConstraint)
    {
        var parts = dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return dottedPath.Replace('.', '$');
        if (string.IsNullOrWhiteSpace(functionalConstraint)) return string.Join('$', parts);

        // IEC 61850 MMS named variables normally place FC after LN name:
        // LD0/LLN0.Mod.stVal [ST] -> domain LD0, item LLN0$ST$Mod$stVal
        if (parts.Length == 1) return parts[0];
        if (parts.Length >= 2 && parts[1].Equals(functionalConstraint, StringComparison.OrdinalIgnoreCase))
            return string.Join('$', parts);

        return string.Join('$', new[] { parts[0], functionalConstraint }.Concat(parts.Skip(1)));
    }

    private static string CanonicalizeIecReferenceCase(string reference)
    {
        return (reference ?? string.Empty)
            .Replace(".ValWTr", ".valWTr", StringComparison.OrdinalIgnoreCase)
            .Replace(".CtlDITms", ".CtlDlTms", StringComparison.OrdinalIgnoreCase);
    }

    public MmsObjectReference WithoutFunctionalConstraint()
    {
        if (string.IsNullOrWhiteSpace(FunctionalConstraint))
            return this;

        var marker = $"${FunctionalConstraint}$";
        if (Item.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            var index = Item.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            var item = Item.Remove(index, FunctionalConstraint.Length + 1);
            return new MmsObjectReference(Domain, item, string.Empty);
        }

        return new MmsObjectReference(Domain, Item, string.Empty);
    }

    public string ToMmsVariableName() => Item;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Domain) ? Item : $"{Domain}/{Path} [{FunctionalConstraint}]";
    }
}
