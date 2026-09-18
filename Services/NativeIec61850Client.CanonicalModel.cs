using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    private LiveIedCanonicalModel? _liveCanonicalModel;

    /// <summary>
    /// Canonical live snapshot bound to the accepted MMS association that produced
    /// the discovery model. Safe SCL export consumes this snapshot so communication
    /// parameters are evidence-backed rather than reconstructed from UI defaults.
    /// </summary>
    public LiveIedCanonicalModel? LastCanonicalModel => _liveCanonicalModel;

    private LiveIedCanonicalModel BuildCanonicalModel(
        LiveIedModelDiscoveryDocument model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var accessPointName = string.IsNullOrWhiteSpace(model.AccessPointName)
            ? "AP1"
            : model.AccessPointName.Trim();
        var communication = _session.GetAcceptedCommunicationEvidence(accessPointName);
        return LiveIedCanonicalModelBuilder.Build(model, communication);
    }

    private void PublishCanonicalModel(LiveIedModelDiscoveryDocument model)
        => _liveCanonicalModel = BuildCanonicalModel(model);

    private void ClearCanonicalModel()
        => _liveCanonicalModel = null;
}
