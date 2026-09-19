using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    private readonly object _smartDiscoveryFlightSync = new();
    private Task<IReadOnlyList<SignalDefinition>>? _smartDiscoveryAssociationFlight;
    private long _smartDiscoveryAssociationGeneration;
    private long _smartDiscoveryFlightGeneration = -1;
    private string _smartDiscoveryAuthorityHost = string.Empty;
    private int _smartDiscoveryAuthorityPort;

    /// <summary>
    /// Explicitly invalidates every association-scoped smart-discovery authority and
    /// advances the generation token. An already-running owner is intentionally not
    /// force-cancelled mid-PDU; it observes the generation change at the next safe
    /// boundary and is forbidden from publishing into the replacement association.
    /// </summary>
    private void ResetSmartDiscoveryAuthority()
    {
        lock (_smartDiscoveryFlightSync)
        {
            unchecked
            {
                _smartDiscoveryAssociationGeneration++;
            }

            _smartDiscoveryAssociationFlight = null;
            _smartDiscoveryFlightGeneration = -1;
            _smartDiscoveryAuthority = null;
            _smartDiscoveryModelAuthority = null;
            ClearCanonicalModel();
            _smartDiscoveryTypeProbeCount = 0;
            _smartDiscoverySuccessfulTypeProbeCount = 0;
            _smartDiscoveryAuthorityHost = string.Empty;
            _smartDiscoveryAuthorityPort = 0;
        }
    }

    private bool IsCurrentSmartDiscoveryAssociationGeneration(long generation)
    {
        lock (_smartDiscoveryFlightSync)
            return generation == _smartDiscoveryAssociationGeneration;
    }

    private Task<IReadOnlyList<SignalDefinition>> GetOrCreateSmartDiscoveryAssociationFlight(
        Func<long, Task<IReadOnlyList<SignalDefinition>>> ownerFactory)
    {
        ArgumentNullException.ThrowIfNull(ownerFactory);

        lock (_smartDiscoveryFlightSync)
        {
            var generation = _smartDiscoveryAssociationGeneration;
            if (_smartDiscoveryAssociationFlight is not null &&
                _smartDiscoveryFlightGeneration == generation)
            {
                return _smartDiscoveryAssociationFlight;
            }

            var flight = ownerFactory(generation);
            _smartDiscoveryAssociationFlight = flight;
            _smartDiscoveryFlightGeneration = generation;
            _ = flight.ContinueWith(
                completed => ClearSmartDiscoveryFlight(generation, completed),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return flight;
        }
    }

    private void ClearSmartDiscoveryFlight(
        long generation,
        Task<IReadOnlyList<SignalDefinition>> flight)
    {
        // Observe a detached owner's fault if every waiter cancelled independently.
        _ = flight.Exception;

        lock (_smartDiscoveryFlightSync)
        {
            if (generation == _smartDiscoveryAssociationGeneration &&
                _smartDiscoveryFlightGeneration == generation &&
                ReferenceEquals(_smartDiscoveryAssociationFlight, flight))
            {
                _smartDiscoveryAssociationFlight = null;
                _smartDiscoveryFlightGeneration = -1;
            }
        }
    }

    private bool TryPublishSmartDiscoveryAuthority(
        long generation,
        ArMms.MmsDiscoveryResult discovery,
        LiveIedModelDiscoveryDocument model,
        ArMms.InitialFcReadExecutionResult? initialRead,
        NativeReportInventory reportInventory,
        Iec61850DeviceIdentity identity,
        int typeProbeCount,
        int successfulTypeProbeCount,
        string summary)
    {
        lock (_smartDiscoveryFlightSync)
        {
            if (generation != _smartDiscoveryAssociationGeneration || !_session.IsMmsInitiated)
                return false;

            _lastDiscovery = discovery;
            _liveModel = model;
            PublishCanonicalModel(model, initialRead);
            LastReportInventory = reportInventory;
            DetectedIdentity = identity;
            PublishSmartDiscoveryAuthority(
                discovery,
                model,
                typeProbeCount,
                successfulTypeProbeCount);
            LastDiscoverySummary = summary;
            LastErrorMessage = summary;
            return true;
        }
    }

    private bool TryPublishSmartDiscoveryPresentation(
        long generation,
        NativeReportInventory reportInventory,
        Iec61850DeviceIdentity identity,
        string summary)
    {
        lock (_smartDiscoveryFlightSync)
        {
            if (generation != _smartDiscoveryAssociationGeneration ||
                !IsSmartDiscoveryAuthorityBoundToCurrentAssociation())
            {
                return false;
            }

            LastReportInventory = reportInventory;
            DetectedIdentity = identity;
            LastDiscoverySummary = summary;
            LastErrorMessage = summary;
            return true;
        }
    }

    private bool IsSmartDiscoveryAuthorityBoundToCurrentAssociation()
        => _session.IsMmsInitiated &&
           string.Equals(_smartDiscoveryAuthorityHost, _host, StringComparison.OrdinalIgnoreCase) &&
           _smartDiscoveryAuthorityPort == _port;

    private void BindSmartDiscoveryAuthorityToCurrentAssociation()
    {
        _smartDiscoveryAuthorityHost = _host;
        _smartDiscoveryAuthorityPort = _port;
    }
}
