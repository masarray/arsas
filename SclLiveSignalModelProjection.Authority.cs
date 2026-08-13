using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// MainWindow compatibility shim. Engine-owned design/live models are authoritative;
/// SignalDefinition projection is used only for legacy/cached rows without engine model provenance.
/// </summary>
internal static class SclLiveSignalModelProjection
{
    public static LiveIedModelDiscoveryDocument Build(
        string iedName,
        string accessPointName,
        IReadOnlyList<SignalDefinition> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var isDesignRows = signals.Any(signal =>
            signal.Source.Equals("SCL design model", StringComparison.OrdinalIgnoreCase));

        if (isDesignRows &&
            SclLiveModelAuthorityRegistry.TryGetDesign(iedName, accessPointName, out var designModel))
        {
            return designModel;
        }

        if (!isDesignRows &&
            SclLiveModelAuthorityRegistry.TryGetLive(iedName, accessPointName, out var liveModel))
        {
            return liveModel;
        }

        return Services.SclLiveSignalModelProjection.Build(iedName, accessPointName, signals);
    }
}
