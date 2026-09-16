namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    private string _smartDiscoveryAuthorityHost = string.Empty;
    private int _smartDiscoveryAuthorityPort;

    /// <summary>
    /// Explicitly invalidates every association-scoped smart-discovery authority.
    /// This is called before a new ConnectAsync lifecycle begins so stale model/type
    /// evidence can never be reused across reconnects, even if later refactors change
    /// how _lastDiscovery/_liveModel are reset.
    /// </summary>
    private void ResetSmartDiscoveryAuthority()
    {
        _smartDiscoveryAuthority = null;
        _smartDiscoveryModelAuthority = null;
        _smartDiscoveryTypeProbeCount = 0;
        _smartDiscoverySuccessfulTypeProbeCount = 0;
        _smartDiscoveryAuthorityHost = string.Empty;
        _smartDiscoveryAuthorityPort = 0;
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
