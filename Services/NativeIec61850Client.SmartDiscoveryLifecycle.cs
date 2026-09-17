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
            _smartDiscoveryTypeProbeCount = 0;
            _smartDiscoverySuccessfulTypeProbeCount = 0;
            _smartDiscoveryAuthorityHost = string.Empty;
            _smartDiscoveryAuthorityPort = 0;
        }
    }

    private long GetSmartDiscoveryAssociationGeneration()
    {
        lock (_smartDiscoveryFlightSync)
            return _smartDiscoveryAssociationGeneration;
    }

    private bool IsCurrentSmartDiscoveryAssociationGeneration(long generation)
    {
        lock (_smartDiscoveryFlightSync)
            return generation == _smartDiscoveryAssociationGeneration;
    }

    private bool TryGetSmartDiscoveryFlight(
        long generation,
        out Task<IReadOnlyList<SignalDefinition>> flight)
    {
        lock (_smartDiscoveryFlightSync)
        {
            if (_smartDiscoveryAssociationFlight is not null &&
                _smartDiscoveryFlightGeneration == generation)
            {
                flight = _smartDiscoveryAssociationFlight;
                return true;
            }
        }

        flight = null!;
        return false;
    }

    private void PublishSmartDiscoveryFlight(
        long generation,
        Task<IReadOnlyList<SignalDefinition>> flight)
    {
        lock (_smartDiscoveryFlightSync)
        {
            if (generation != _smartDiscoveryAssociationGeneration)
                return;

            _smartDiscoveryAssociationFlight = flight;
            _smartDiscoveryFlightGeneration = generation;
        }
    }

    private void ClearSmartDiscoveryFlight(
        long generation,
        Task<IReadOnlyList<SignalDefinition>> flight)
    {
        // Read the exception here as well so a detached owner whose only waiter was
        // cancelled cannot leave an unobserved fault behind.
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
