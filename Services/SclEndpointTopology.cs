using AR.Iec61850.Scl.Workspace;

namespace ArIED61850Tester.Services;

/// <summary>
/// Pure, offline SCD endpoint selection. AP identities and IPs must originate
/// from the engine's ConnectedAP inventory; never synthesize, poll or probe.
/// </summary>
internal static class SclEndpointTopology
{
    public static IReadOnlyList<SclMmsEndpoint> Candidates(SclWorkspaceDocument document, string iedName)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.MmsEndpoints
            .Where(endpoint =>
                endpoint.HasUsableAddress &&
                endpoint.IedName.Equals(iedName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(endpoint => endpoint.AccessPointName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(endpoint => endpoint.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ThenBy(endpoint => endpoint.Port)
            .ToArray();
    }

    public static SclIedWorkspace? FindExactWorkspace(
        SclWorkspaceDocument document,
        SclMmsEndpoint target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);

        var matches = document.Ieds.Where(workspace =>
            workspace.IedName.Equals(target.IedName, StringComparison.OrdinalIgnoreCase) &&
            workspace.AccessPointName.Equals(target.AccessPointName, StringComparison.OrdinalIgnoreCase) &&
            workspace.PreferredEndpoint?.HasUsableAddress == true &&
            workspace.PreferredEndpoint.IpAddress.Equals(target.IpAddress, StringComparison.OrdinalIgnoreCase) &&
            workspace.PreferredEndpoint.Port == target.Port).ToArray();

        // Conflicting source bindings must not silently select a random AP.
        return matches.Length == 1 && matches[0].CanBrowseOffline ? matches[0] : null;
    }
}
