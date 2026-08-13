using System.Collections.Concurrent;
using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Services;

/// <summary>
/// Application provenance registry for engine-owned design/live models.
/// It performs no IEC 61850 reference, CDC, FC, DataSet, or vendor interpretation.
/// </summary>
public static class SclLiveModelAuthorityRegistry
{
    private static readonly ConcurrentDictionary<string, WeakReference<LiveIedModelDiscoveryDocument>> DesignModels
        = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, WeakReference<LiveIedModelDiscoveryDocument>> LiveModels
        = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterDesign(string? iedName, string? accessPointName, LiveIedModelDiscoveryDocument model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DesignModels[Key(iedName, accessPointName)] = new(model);
    }

    public static void RegisterLive(string? iedName, string? accessPointName, LiveIedModelDiscoveryDocument model)
    {
        ArgumentNullException.ThrowIfNull(model);
        LiveModels[Key(iedName, accessPointName)] = new(model);
    }

    public static bool TryGetDesign(string? iedName, string? accessPointName, out LiveIedModelDiscoveryDocument model)
        => TryGet(DesignModels, Key(iedName, accessPointName), out model);

    public static bool TryGetLive(string? iedName, string? accessPointName, out LiveIedModelDiscoveryDocument model)
        => TryGet(LiveModels, Key(iedName, accessPointName), out model);

    private static bool TryGet(
        ConcurrentDictionary<string, WeakReference<LiveIedModelDiscoveryDocument>> registry,
        string key,
        out LiveIedModelDiscoveryDocument model)
    {
        model = null!;
        if (!registry.TryGetValue(key, out var weak))
            return false;
        if (weak.TryGetTarget(out model!))
            return true;

        registry.TryRemove(key, out _);
        model = null!;
        return false;
    }

    private static string Key(string? iedName, string? accessPointName)
        => $"{(iedName ?? string.Empty).Trim()}|{(accessPointName ?? string.Empty).Trim()}";
}
