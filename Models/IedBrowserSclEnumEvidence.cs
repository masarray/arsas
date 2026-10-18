using System.Globalization;
using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Models;

/// <summary>
/// Read-only SCL EnumType declaration authority. Symbols are accepted only from
/// the original SHA-256-verified CID/SCL source, its exact selected IED's
/// DataTypeTemplates binding and an exact online typed SG attribute identity.
/// No ordinal-to-symbol mappings are shared between unrelated enum types.
/// This contains declarative metadata ONLY, never MMS observations.
/// </summary>
public sealed class IedBrowserSclEnumEvidence
{
    private sealed record BoundEnum(
        string TypeId, IReadOnlyDictionary<long, string> Symbols);
    private readonly IReadOnlyDictionary<string, BoundEnum> _bindings;
    public string SourceSha256 { get; }
    public int BoundAttributeCount => _bindings.Count;

    private IedBrowserSclEnumEvidence(
        string sha256, IReadOnlyDictionary<string, BoundEnum> bindings)
    {
        SourceSha256 = sha256;
        _bindings = bindings;
    }

    public static IedBrowserSclEnumEvidence Build(
        VerifiedSclSource source, SclIedWorkspace selectedIed,
        LiveIedModelDiscoveryDocument? onlineModel)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selectedIed);
        var bound = new Dictionary<string, BoundEnum>(StringComparer.Ordinal);
        // A source with no unique selected IED is not a type authority.
        var xml = XDocument.Parse(source.Xml, LoadOptions.None);
        var root = xml.Root;
        if (root is null || string.IsNullOrWhiteSpace(selectedIed.IedName) ||
            onlineModel is null)
            return new IedBrowserSclEnumEvidence(source.Sha256, bound);
        var ieds = root.Elements().Where(x => NameIs(x, "IED") &&
            string.Equals(Attr(x, "name"), selectedIed.IedName,
                StringComparison.Ordinal)).Take(2).ToArray();
        if (ieds.Length != 1)
            return new IedBrowserSclEnumEvidence(source.Sha256, bound);
        // The SCL document can contain many IEDs. Only declared logical
        // devices belonging to the exact selected IED may bind SG semantics.
        var acceptedDomains = ieds[0].Descendants().Where(x => NameIs(x, "LDevice"))
            .Select(x => selectedIed.IedName + Attr(x, "inst"))
            .ToHashSet(StringComparer.Ordinal);
        if (acceptedDomains.Count == 0)
            return new IedBrowserSclEnumEvidence(source.Sha256, bound);

        // Exact ID, no first-wins on duplicate conflicting definitions.
        var enumGroups = root.Descendants().Where(x => NameIs(x, "EnumType"))
            .GroupBy(x => Attr(x, "id"), StringComparer.Ordinal)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);
        var declarations = selectedIed.DesignModel.LogicalDevices
            .Where(d => acceptedDomains.Contains(d.MmsDomain))
            .SelectMany(d => d.LogicalNodes)
            .SelectMany(n => n.DataObjects)
            .SelectMany(o => o.Attributes)
            .Where(a => string.Equals(a.FunctionalConstraint, "SG",
                StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.SclBType, "Enum",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Source, "SCL.DataTypeTemplates",
                    StringComparison.Ordinal) &&
                a.MmsTypeSignature.StartsWith("Enum:", StringComparison.Ordinal))
            .GroupBy(a => Key(a.MmsReference, a.FunctionalConstraint),
                StringComparer.Ordinal)
            .Where(g => g.Count() == 1).Select(g => g.Single()).ToArray();

        // The selected IED's online type proof must be unique and enumerated.
        var live = onlineModel.LogicalDevices.SelectMany(d => d.LogicalNodes)
            .SelectMany(n => n.DataObjects).SelectMany(o => o.Attributes)
            .Where(a => string.Equals(a.FunctionalConstraint, "SG",
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => Key(a.MmsReference, a.FunctionalConstraint),
                StringComparer.Ordinal)
            .Where(g => g.Count() == 1 &&
                g.Single().TypeDiscoveryStatus.Equals("Exact",
                    StringComparison.OrdinalIgnoreCase) &&
                (g.Single().MmsType.Equals("enumerated", StringComparison.OrdinalIgnoreCase) ||
                 g.Single().MmsType.Equals("enum", StringComparison.OrdinalIgnoreCase) ||
                 g.Single().SclBType.Equals("Enum", StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.Ordinal);

        foreach (var attr in declarations)
        {
            var typeId = attr.MmsTypeSignature["Enum:".Length..];
            var key = Key(attr.MmsReference, attr.FunctionalConstraint);
            if (!live.ContainsKey(key) ||
                !enumGroups.TryGetValue(typeId, out var enumType) ||
                !TrySymbols(enumType, out var symbols))
                continue;
            bound.Add(key, new BoundEnum(typeId, symbols));
        }
        return new IedBrowserSclEnumEvidence(source.Sha256, bound);
    }

    public bool TryFormat(string reference, string fc, string raw,
        out string display, out string evidence)
    {
        display = raw;
        evidence = "No exact live SG EnumType declaration in the verified SCL source.";
        if (!string.Equals(fc, "SG", StringComparison.OrdinalIgnoreCase) ||
            !_bindings.TryGetValue(Key(reference, fc), out var bound))
            return false;
        var ordinalText = (raw ?? "").Trim();
        if (ordinalText.StartsWith("value", StringComparison.OrdinalIgnoreCase))
            ordinalText = ordinalText["value".Length..];
        if (!long.TryParse(ordinalText, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var ordinal))
        {
            evidence = "SG enum has an unrecognized raw MMS ordinal.";
            return false;
        }
        if (!bound.Symbols.TryGetValue(ordinal, out var symbol))
        {
            evidence = $"SCL EnumType {bound.TypeId} has no ordinal {ordinal}; raw preserved.";
            return false;
        }
        display = symbol;
        evidence = $"Verified SCL EnumType {bound.TypeId}, ord={ordinal}; source SHA-256 {SourceSha256}. Raw MMS ordinal preserved in SSOT.";
        return true;
    }

    private static bool TrySymbols(XElement definition,
        out IReadOnlyDictionary<long, string> symbols)
    {
        symbols = new Dictionary<long, string>();
        var values = definition.Elements().Where(x => NameIs(x, "EnumVal"))
            .Take(257).ToArray();
        if (values.Length is < 1 or > 256) return false;
        var decoded = new Dictionary<long, string>();
        foreach (var value in values)
        {
            var name = value.Value.Trim();
            if (name.Length is < 1 or > 128 ||
                !long.TryParse(Attr(value, "ord"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var ord) ||
                !decoded.TryAdd(ord, name))
                return false;
        }
        symbols = decoded;
        return true;
    }

    private static string Key(string reference, string fc)
        => IedBrowserLiveValues.CanonicalReference(reference) + "|" +
           (fc ?? "").Trim().ToUpperInvariant();
    private static bool NameIs(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);
    private static string Attr(XElement element, string name)
        => (string?)element.Attribute(name) ?? "";
}
