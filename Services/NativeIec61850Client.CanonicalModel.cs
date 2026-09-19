using AR.Iec61850.Discovery;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    private LiveIedCanonicalModel? _liveCanonicalModel;
    private ArMms.InitialFcReadExecutionResult? _liveInitialFcRead;

    /// <summary>
    /// Canonical live snapshot bound to the accepted MMS association that produced
    /// the discovery model. Safe SCL export consumes this snapshot so communication
    /// parameters and instance values are evidence-backed rather than reconstructed
    /// from UI defaults.
    /// </summary>
    public LiveIedCanonicalModel? LastCanonicalModel => _liveCanonicalModel;
    public ArMms.InitialFcReadExecutionResult? LastInitialFcRead => _liveInitialFcRead;

    private LiveIedCanonicalModel BuildCanonicalModel(
        LiveIedModelDiscoveryDocument model,
        ArMms.InitialFcReadExecutionResult? initialRead = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var accessPointName = string.IsNullOrWhiteSpace(model.AccessPointName)
            ? "AP1"
            : model.AccessPointName.Trim();
        var communication = _session.GetAcceptedCommunicationEvidence(accessPointName);
        return LiveIedCanonicalModelBuilder.Build(model, communication, initialRead);
    }

    private void PublishCanonicalModel(
        LiveIedModelDiscoveryDocument model,
        ArMms.InitialFcReadExecutionResult? initialRead = null)
    {
        _liveInitialFcRead = initialRead;
        _liveCanonicalModel = BuildCanonicalModel(model, initialRead);
    }

    private static readonly HashSet<string> SaveSclEnrichmentFunctionalConstraints = new(
        ["ST", "MX", "SV", "CF", "DC", "SG", "SE", "SR", "OR", "BL", "EX", "SP"],
        StringComparer.Ordinal);

    /// <summary>
    /// Enriches the canonical snapshot only when the user explicitly saves SCL.
    /// Smart Discovery stays structure/type-only; this bounded phase reads safe FC
    /// roots on the already accepted association and projects exact scalar leaves.
    /// Control/report service FCs (CO/RP/BR/LG/GO/GS/MS/US) are never read here.
    /// </summary>
    public async Task<LiveIedCanonicalModel> EnrichCanonicalForSclSaveAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_session.IsMmsInitiated || !_session.IsTransportConnected)
            throw new InvalidOperationException("Save-SCL enrichment requires the existing initiated MMS association.");
        if (_liveModel is null)
            throw new InvalidOperationException("Save-SCL enrichment requires a successful live discovery model.");

        var sourcePlan = ArMms.InitialFcReadPlanner.FromSclModel(_liveModel);
        var safeTargets = sourcePlan.Targets
            .Where(target => SaveSclEnrichmentFunctionalConstraints.Contains(
                (target.FunctionalConstraint ?? string.Empty).Trim().ToUpperInvariant()))
            .ToArray();
        var plan = ArMms.InitialFcReadPlanner.Build(
            safeTargets,
            sourcePlan.MaximumVariableReferencesPerRead);
        if (!plan.IsValid)
        {
            throw new InvalidOperationException(
                "Save-SCL enrichment could not build a safe FC-root Read plan: " +
                string.Join(" | ", plan.Errors));
        }

        var initialRead = await _session.ExecuteInitialFcReadPlanSmartAsync(
                plan,
                new ArMms.MmsSmartInitialFcReadOptions
                {
                    MaxOutstandingBatches = 8,
                    UnknownPeerMaxOutstandingBatches = 4,
                    PerBatchTimeout = TimeSpan.FromSeconds(5)
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (initialRead.Status is ArMms.InitialFcReadExecutionStatus.InvalidPlan
            or ArMms.InitialFcReadExecutionStatus.SessionNotReady
            or ArMms.InitialFcReadExecutionStatus.TimedOut
            or ArMms.InitialFcReadExecutionStatus.TransportFailure)
        {
            throw new InvalidOperationException(
                $"Save-SCL enrichment failed ({initialRead.Status}): {initialRead.Message}");
        }

        PublishCanonicalModel(_liveModel, initialRead);
        return _liveCanonicalModel
            ?? throw new InvalidOperationException("Save-SCL enrichment did not publish a canonical model.");
    }

    private void ClearCanonicalModel()
    {
        _liveInitialFcRead = null;
        _liveCanonicalModel = null;
    }
}
