using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    /// <summary>
    /// Returns the same per-IED model authority used by semantic report projection.
    /// Direct-SCL FAT therefore uses the opened SCL design model; fully discovered
    /// Engineering sessions can use the live model. No ambient/global model is consulted.
    /// </summary>
    internal LiveIedModelDiscoveryDocument? AggregateProjectionAuthorityModel
        => _semanticReportProjectionAuthorityModel ?? _liveModel;

    internal bool TryBuildSchemaSafeAggregateReadPlan(
        string requestedReference,
        out SchemaSafeAggregateProjectionService.ReadPlan plan,
        out string status)
        => SchemaSafeAggregateProjectionService.TryBuildReadPlan(
            AggregateProjectionAuthorityModel,
            requestedReference,
            out plan,
            out status);
}
