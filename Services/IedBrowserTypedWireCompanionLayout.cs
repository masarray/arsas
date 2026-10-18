using System.Globalization;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Immutable *layout only* for one Report FCD. Maps typed LN-root GVA ordered
/// TypeSpecification positions to named q/t leaves in the exact active model.
/// Does not hold received values, guess from runtime bit strings, or use SCL
/// synthesized declaration order. A missing/ambiguous index fails closed.
/// </summary>
public static class IedBrowserTypedWireCompanionLayout
{
    public sealed record Route(
        string Reference, string Kind, IReadOnlyList<int> ChildIndices);

    // Cache only the model's immutable declaration routes, NEVER Report/Read
    // evidence. Weak ownership evicts layouts when the IED model is replaced.
    private static readonly ConditionalWeakTable<LiveIedModelDiscoveryDocument,
        ConcurrentDictionary<string, IReadOnlyList<Route>>> RouteCache = new();

    public static IReadOnlyList<Route> Resolve(
        LiveIedModelDiscoveryDocument? model, string carrier, string fc)
    {
        if (model is null || string.IsNullOrWhiteSpace(fc)) return [];
        var root = ArIED61850Tester.Models.IedBrowserLiveValues.CanonicalReference(carrier);
        var normalizedFc = fc.Trim().ToUpperInvariant();
        var cache = RouteCache.GetValue(model, _ =>
            new ConcurrentDictionary<string, IReadOnlyList<Route>>(StringComparer.Ordinal));
        return cache.GetOrAdd(root + "|" + normalizedFc,
            _ => BuildVerifiedRoutes(model, root, normalizedFc));
    }

    private static IReadOnlyList<Route> BuildVerifiedRoutes(
        LiveIedModelDiscoveryDocument model, string root, string fc)
    {
        var slash = root.IndexOf('/');
        if (slash <= 0 || root.Length <= slash + 1) return [];
        var carrierDepth = root[(slash + 1)..].Split('.').Length;
        if (carrierDepth < 2 || carrierDepth > 12) return [];
        var selectedFc = fc;
        var matches = model.LogicalDevices.SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects).SelectMany(obj => obj.Attributes)
            .Where(attr => string.Equals(attr.FunctionalConstraint,
                        selectedFc, StringComparison.OrdinalIgnoreCase))
            .Select(attr => (Attr: attr, Reference:
                ArIED61850Tester.Models.IedBrowserLiveValues.CanonicalReference(
                    attr.MmsReference)))
            .Where(candidate => candidate.Reference.StartsWith(root + ".",
                StringComparison.Ordinal) &&
                (candidate.Reference.EndsWith(".q", StringComparison.Ordinal) ||
                 candidate.Reference.EndsWith(".t", StringComparison.Ordinal)))
            .ToArray();
        if (matches.Length == 0) return [];

        var routes = new List<(Route Route, string CarrierOrder)>();
        // A duplicate exact leaf is ambiguous even when its order matches.
        foreach (var group in matches.GroupBy(x => x.Reference, StringComparer.Ordinal))
        {
            if (group.Count() != 1) continue;
            var (attr, reference) = group.Single();
            var isQuality = reference.EndsWith(".q", StringComparison.Ordinal);
            if (!string.Equals(attr.TypeDiscoveryStatus, "Exact",
                    StringComparison.OrdinalIgnoreCase) ||
                !attr.TypeSource.StartsWith("GetVariableAccessAttributes",
                    StringComparison.Ordinal) ||
                !(isQuality
                    ? string.Equals(attr.SclBType, "Quality", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(attr.MmsType, "bit-string", StringComparison.OrdinalIgnoreCase)
                    : string.Equals(attr.SclBType, "Timestamp", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(attr.SclBType, "UtcTime", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(attr.MmsType, "utc-time", StringComparison.OrdinalIgnoreCase)))
                continue;
            var declaration = attr.TypeDeclarationOrder.Split('.');
            // GVA LN root ordering: FC / DO / nested BDA ... / q or t.
            // Every path index must correspond to one canonical IEC component.
            var expectedDepth = reference[(reference.IndexOf('/') + 1)..]
                .Split('.').Length;
            if (declaration.Length != expectedDepth ||
                declaration.Length <= carrierDepth ||
                declaration.Length > 13 ||
                declaration.Any(segment => segment.Length != 6 ||
                    !int.TryParse(segment, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var n) || n is < 0 or > 63))
                continue;
            var indices = declaration.Skip(carrierDepth).Select(x =>
                int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            routes.Add((new Route(reference, isQuality ? "Quality" : "Timestamp",
                indices), string.Join(".", declaration.Take(carrierDepth))));
        }
        // Conflicting FC/DO/container order is not one trustworthy typed
        // layout, even when leaf-relative indices happen to be distinct.
        if (routes.Select(x => x.CarrierOrder).Distinct(StringComparer.Ordinal)
                .Take(2).Count() != 1)
            return [];
        // Same wire child index cannot prove two different IEC members.
        // Reject every collision, not first-wins.
        return routes.GroupBy(x => string.Join(".", x.Route.ChildIndices),
                StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single().Route)
            .OrderBy(route => route.Reference, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool TryLocate(MmsDataValue carrier, IReadOnlyList<int> indices,
        out MmsDataValue leaf)
    {
        leaf = null!;
        if (indices.Count is < 1 or > 12) return false;
        var current = carrier;
        foreach (var index in indices)
        {
            if (current.Kind != MmsDataKind.Structure ||
                current.Children.Count is < 1 or > 64 ||
                index < 0 || index >= current.Children.Count)
                return false;
            current = current.Children[index];
        }
        if (current.Kind is not (MmsDataKind.BitString or MmsDataKind.UtcTime))
            return false;
        leaf = current;
        return true;
    }
}
