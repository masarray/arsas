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

    private void ClearCanonicalModel()
    {
        _liveInitialFcRead = null;
        _liveCanonicalModel = null;
    }
}
