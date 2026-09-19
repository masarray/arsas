using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Export;
using AR.Iec61850.Scl.Workspace;

namespace ArIED61850Tester.Services;

public static class CanonicalSclReloadValidator
{
    public static SclIedWorkspace Validate(
        SclWorkspaceService workspaceService,
        LiveIedCanonicalModel canonical,
        LiveIedSclExportResult result)
    {
        ArgumentNullException.ThrowIfNull(workspaceService);
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(result);

        if (!string.Equals(result.Profile, "full-model", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Canonical live SCL must preserve the full discovered model; exporter profile was '{result.Profile}'.");
        }

        if (result.ExcludedAttributes.Count != 0)
        {
            throw new InvalidOperationException(
                $"Canonical full-model SCL unexpectedly excluded {result.ExcludedAttributes.Count} discovered attribute(s).");
        }

        var reloaded = workspaceService.Open(
            result.SclPath,
            new SclWorkspaceOpenOptions
            {
                IedName = canonical.IedName,
                AccessPointName = canonical.AccessPointName
            });

        var workspace = reloaded.Ieds.SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"Generated SCL could not be reopened as exactly one ARSAS workspace for '{canonical.IedName}/{canonical.AccessPointName}'.");

        if (!string.Equals(workspace.IedName, canonical.IedName, StringComparison.Ordinal) ||
            !string.Equals(workspace.AccessPointName, canonical.AccessPointName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Generated SCL reload identity drifted. Expected '{canonical.IedName}/{canonical.AccessPointName}', " +
                $"reloaded '{workspace.IedName}/{workspace.AccessPointName}'.");
        }

        var endpoint = workspace.PreferredEndpoint
            ?? throw new InvalidOperationException(
                "Generated SCL reload did not resolve a usable MMS endpoint.");

        if (!endpoint.HasUsableAddress ||
            !string.Equals(endpoint.IpAddress, canonical.Communication.Host, StringComparison.OrdinalIgnoreCase) ||
            endpoint.Port != 102)
        {
            throw new InvalidOperationException(
                $"Generated SCL reload endpoint drifted. Expected '{canonical.Communication.Host}:102', " +
                $"reloaded '{endpoint.EndpointText}'.");
        }

        var reloadCoverage = workspace.DesignModel.Coverage;
        var mismatches = new List<string>();
        if (reloadCoverage.LogicalDeviceCount != result.LogicalDeviceCount)
            mismatches.Add($"LD {reloadCoverage.LogicalDeviceCount}!={result.LogicalDeviceCount}");
        if (reloadCoverage.LogicalNodeCount != result.LogicalNodeCount)
            mismatches.Add($"LN {reloadCoverage.LogicalNodeCount}!={result.LogicalNodeCount}");
        if (workspace.DataSets.Count != result.DataSetCount)
            mismatches.Add($"DataSet {workspace.DataSets.Count}!={result.DataSetCount}");
        if (workspace.ReportControls.Count != result.ReportControlCount)
            mismatches.Add($"RCB {workspace.ReportControls.Count}!={result.ReportControlCount}");

        if (mismatches.Count > 0)
        {
            throw new InvalidOperationException(
                "Generated SCL changed structural counts when reloaded by ARSAS: " +
                string.Join(", ", mismatches) + ".");
        }

        var preparation = SclAssistedConnectionPreparationBuilder.Build(
            File.ReadAllText(result.SclPath),
            canonical.IedName,
            canonical.AccessPointName,
            canonical.Communication.Host,
            canonical.Communication.Port);
        if (!preparation.IsSuccess || preparation.AssociationPlan is null)
        {
            var detail = preparation.Errors.Count == 0
                ? "unknown SCL-assisted preparation failure"
                : string.Join(" | ", preparation.Errors);
            throw new InvalidOperationException(
                "Generated SCL cannot rebuild the ARSAS SCL-assisted reconnect plan: " + detail);
        }

        var plan = preparation.AssociationPlan;
        if (!string.Equals(plan.IedName, canonical.IedName, StringComparison.Ordinal) ||
            !string.Equals(plan.AccessPointName, canonical.AccessPointName, StringComparison.Ordinal) ||
            !string.Equals(plan.Host, canonical.Communication.Host, StringComparison.OrdinalIgnoreCase) ||
            plan.Port != canonical.Communication.Port)
        {
            throw new InvalidOperationException(
                $"Generated SCL reconnect plan identity drifted. Expected '{canonical.IedName}/{canonical.AccessPointName}' " +
                $"at {canonical.Communication.Host}:{canonical.Communication.Port}, rebuilt " +
                $"'{plan.IedName}/{plan.AccessPointName}' at {plan.Host}:{plan.Port}.");
        }

        var canonicalAssociation = canonical.Communication.Association;
        var rebuiltApTitle = string.Join(",", plan.Association.Called.ApTitle);
        var rebuiltPsel = Convert.ToHexString(plan.Association.Called.PresentationSelector);
        var rebuiltSsel = Convert.ToHexString(plan.Association.Called.SessionSelector);
        var rebuiltTsel = Convert.ToHexString(plan.Cotp.DestinationTsap);
        if (!string.Equals(rebuiltApTitle, canonicalAssociation.ApTitle, StringComparison.Ordinal) ||
            plan.Association.Called.AeQualifier != canonicalAssociation.AeQualifier ||
            !string.Equals(rebuiltPsel, canonicalAssociation.PresentationSelector, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(rebuiltSsel, canonicalAssociation.SessionSelector, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(rebuiltTsel, canonicalAssociation.TransportSelector, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Generated SCL reconnect association drifted from accepted canonical wire evidence. " +
                $"Expected AP={canonicalAssociation.ApTitle}, AE={canonicalAssociation.AeQualifier}, " +
                $"PSEL={canonicalAssociation.PresentationSelector}, SSEL={canonicalAssociation.SessionSelector}, " +
                $"TSEL={canonicalAssociation.TransportSelector}; rebuilt AP={rebuiltApTitle}, " +
                $"AE={plan.Association.Called.AeQualifier}, PSEL={rebuiltPsel}, SSEL={rebuiltSsel}, TSEL={rebuiltTsel}.");
        }

        var local = AR.Iec61850.Scl.MmsLocalAssociationProfile.ExistingRuntimeDefault;
        var callingApTitle = plan.Association.Calling.ApTitle;
        if (!plan.Cotp.SourceTsap.SequenceEqual(local.TransportSelector) ||
            !plan.Association.Calling.SessionSelector.SequenceEqual(local.SessionSelector) ||
            !plan.Association.Calling.PresentationSelector.SequenceEqual(local.PresentationSelector) ||
            !callingApTitle.SequenceEqual(local.ApTitle) ||
            plan.Association.Calling.AeQualifier != local.AeQualifier)
        {
            throw new InvalidOperationException(
                "Generated SCL reconnect plan changed the proven calling-side runtime identity.");
        }

        var acceptedProfileName = canonical.Communication.AssociationProfileName?.Trim() ?? string.Empty;
        var acceptedProfile = AR.Iec61850.Acse.AcseMmsInitiateRequest
            .BuildAssociationProfiles()
            .SingleOrDefault(profile =>
                string.Equals(profile.Name, acceptedProfileName, StringComparison.Ordinal));
        if (acceptedProfile is not null &&
            !plan.SessionPresentationAcseMmsRequest.SequenceEqual(acceptedProfile.Payload))
        {
            throw new InvalidOperationException(
                $"Generated SCL reconnect AARQ does not reproduce accepted association profile '{acceptedProfileName}' byte-for-byte.");
        }

        return workspace;
    }
}
