using AR.Iec61850.Scl.Workspace;

namespace ArIED61850Tester.Services;

/// <summary>
/// An SCL AccessPoint is a model identity even when Communication/ConnectedAP
/// supplies no MMS IP. The optional address is a separate, sourced fact.
/// </summary>
public sealed record SclAccessPointChoice(
    string IedName,
    string AccessPointName,
    SclMmsEndpoint? DeclaredEndpoint)
{
    public bool HasDeclaredAddress => DeclaredEndpoint?.HasUsableAddress == true;
}

/// <summary>
/// Offline-only topology projection. Never guesses IPs and never transfers one
/// AccessPoint's endpoint to a different AP or IED.
/// </summary>
internal static class SclEndpointTopology
{
    public static IReadOnlyList<SclAccessPointChoice> Choices(SclWorkspaceDocument document, string iedName)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Ieds
            .Where(ied => ied.CanBrowseOffline &&
                ied.IedName.Equals(iedName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(ied.AccessPointName))
            .GroupBy(ied => ied.AccessPointName, StringComparer.OrdinalIgnoreCase)
            // Duplicate AP workspaces or conflicting ConnectedAP addresses are not
            // safe operator choices; do not silently take the first.
            .Where(group => group.Count() == 1)
            .Select(group =>
            {
                var workspace = group.Single();
                var endpoints = document.MmsEndpoints.Where(endpoint =>
                    endpoint.HasUsableAddress &&
                    endpoint.IedName.Equals(workspace.IedName, StringComparison.OrdinalIgnoreCase) &&
                    endpoint.AccessPointName.Equals(workspace.AccessPointName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                return new { Workspace = workspace, Endpoints = endpoints };
            })
            .Where(item => item.Endpoints.Length <= 1)
            .Select(item => new SclAccessPointChoice(
                item.Workspace.IedName,
                item.Workspace.AccessPointName,
                item.Endpoints.SingleOrDefault()))
            .OrderBy(choice => choice.AccessPointName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<SclMmsEndpoint> Candidates(SclWorkspaceDocument document, string iedName)
        => Choices(document, iedName)
            .Where(choice => choice.HasDeclaredAddress)
            .Select(choice => choice.DeclaredEndpoint!)
            .ToArray();

    public static SclIedWorkspace? FindExactWorkspace(
        SclWorkspaceDocument document,
        SclAccessPointChoice target)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(target);
        var choices = Choices(document, target.IedName);
        var exact = choices.Where(choice =>
            choice.IedName.Equals(target.IedName, StringComparison.OrdinalIgnoreCase) &&
            choice.AccessPointName.Equals(target.AccessPointName, StringComparison.OrdinalIgnoreCase) &&
            SameEndpoint(choice.DeclaredEndpoint, target.DeclaredEndpoint)).ToArray();

        if (exact.Length != 1)
            return null;

        var matches = document.Ieds.Where(workspace =>
            workspace.CanBrowseOffline &&
            workspace.IedName.Equals(target.IedName, StringComparison.OrdinalIgnoreCase) &&
            workspace.AccessPointName.Equals(target.AccessPointName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    // Backward-compatible strict declared-endpoint lookup for P7.5 call sites.
    public static SclIedWorkspace? FindExactWorkspace(
        SclWorkspaceDocument document,
        SclMmsEndpoint target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!target.HasUsableAddress) return null;
        var matches = Choices(document, target.IedName).Where(choice =>
            choice.HasDeclaredAddress && SameEndpoint(choice.DeclaredEndpoint, target)).ToArray();
        return matches.Length == 1 ? FindExactWorkspace(document, matches[0]) : null;
    }

    private static bool SameEndpoint(SclMmsEndpoint? left, SclMmsEndpoint? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.IedName.Equals(right.IedName, StringComparison.OrdinalIgnoreCase) &&
               left.AccessPointName.Equals(right.AccessPointName, StringComparison.OrdinalIgnoreCase) &&
               left.IpAddress.Equals(right.IpAddress, StringComparison.OrdinalIgnoreCase) &&
               left.Port == right.Port &&
               left.SubNetworkName.Equals(right.SubNetworkName, StringComparison.OrdinalIgnoreCase);
    }
}
