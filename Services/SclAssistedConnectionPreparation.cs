using System.Xml;
using ArMms = AR.Iec61850.Mms;
using ArScl = AR.Iec61850.Scl;

namespace ArIED61850Tester.Services;

public sealed class SclAssistedConnectionPreparation
{
    public ArScl.SclAssistedMmsAssociationPlan? AssociationPlan { get; init; }
    public ArScl.SclMmsDomainInventory DomainInventory { get; init; } = new();
    public ArScl.SclInitialFcReadDesign? InitialReadDesign { get; init; }
    public ArMms.InitialFcReadPlan? InitialReadPlan { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public bool IsSuccess =>
        Errors.Count == 0 &&
        AssociationPlan is not null &&
        DomainInventory.IsSuccess &&
        InitialReadDesign?.IsSuccess == true &&
        InitialReadPlan?.IsValid == true;
}

/// <summary>
/// Pure Step-5 orchestration preparation. It converts one trusted SCL IED/AccessPoint
/// plus the operator-bound TCP endpoint into the exact ARIEC61850 association/domain/
/// initial-read contracts. It performs no socket I/O and never falls back to discovery.
/// </summary>
public static class SclAssistedConnectionPreparationBuilder
{
    public static SclAssistedConnectionPreparation Build(
        string sclXml,
        string iedName,
        string accessPointName,
        string host,
        int port,
        int maximumVariableReferencesPerRead = ArMms.MmsReadBatchCodec.MaximumVariableReferencesPerRead)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var normalizedHost = (host ?? string.Empty).Trim();
        var normalizedIed = (iedName ?? string.Empty).Trim();
        var normalizedAccessPoint = (accessPointName ?? string.Empty).Trim();
        var normalizedPort = port <= 0 ? 102 : port;

        if (string.IsNullOrWhiteSpace(sclXml))
            errors.Add("SCL XML is empty.");
        if (string.IsNullOrWhiteSpace(normalizedIed))
            errors.Add("An exact SCL IED name is required.");
        if (string.IsNullOrWhiteSpace(normalizedAccessPoint))
            errors.Add("An exact SCL AccessPoint name is required.");
        if (string.IsNullOrWhiteSpace(normalizedHost))
            errors.Add("A TCP endpoint is required before SCL-assisted connect.");
        if (normalizedPort is < 1 or > 65535)
            errors.Add($"TCP port must be in 1..65535; received {normalizedPort}.");
        if (maximumVariableReferencesPerRead is < 1 or > ArMms.MmsReadBatchCodec.MaximumVariableReferencesPerRead)
        {
            errors.Add(
                $"Initial Read batch size must be in 1..{ArMms.MmsReadBatchCodec.MaximumVariableReferencesPerRead}; received {maximumVariableReferencesPerRead}.");
        }

        if (errors.Count > 0)
            return Fail(errors, warnings);

        ArScl.SclMmsAssociationProfileSet profileSet;
        try
        {
            profileSet = ArScl.SclMmsAssociationProfileReader.Read(sclXml);
        }
        catch (XmlException ex)
        {
            errors.Add($"SCL XML is malformed: {ex.Message}");
            return Fail(errors, warnings);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
        {
            errors.Add($"SCL MMS communication profile could not be read: {ex.GetType().Name}: {ex.Message}");
            return Fail(errors, warnings);
        }

        warnings.AddRange(profileSet.Warnings);
        var matches = profileSet.AccessPoints
            .Where(profile =>
                string.Equals(profile.IedName, normalizedIed, StringComparison.Ordinal) &&
                string.Equals(profile.AccessPointName, normalizedAccessPoint, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length != 1)
        {
            errors.Add(matches.Length == 0
                ? $"SCL Communication has no exact ConnectedAP for IED '{normalizedIed}' / AccessPoint '{normalizedAccessPoint}'."
                : $"SCL Communication has {matches.Length} exact ConnectedAP entries for IED '{normalizedIed}' / AccessPoint '{normalizedAccessPoint}'; association identity is ambiguous.");
            return Fail(errors, warnings);
        }

        var sclRemote = matches[0];
        var sclHost = (sclRemote.Endpoint.IpAddress ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sclHost))
        {
            warnings.Add($"SCL ConnectedAP has no IP address; using the explicit endpoint binding '{normalizedHost}'.");
        }
        else if (!string.Equals(sclHost, normalizedHost, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"SCL IP '{sclHost}' differs from the explicit endpoint binding '{normalizedHost}'; TCP uses the explicit binding while OSI association identity remains SCL-derived.");
        }

        // TCP endpoint binding is an application-level choice. Preserve every called-side
        // OSI identity value from SCL and change only the network endpoint presented to
        // the pure ARIEC association planner.
        var effectiveRemote = new ArScl.SclMmsAccessPoint
        {
            IedName = sclRemote.IedName,
            AccessPointName = sclRemote.AccessPointName,
            SubNetworkName = sclRemote.SubNetworkName,
            SubNetworkType = sclRemote.SubNetworkType,
            Endpoint = new ArScl.SclMmsEndpoint
            {
                IpAddress = normalizedHost,
                IpSubnet = sclRemote.Endpoint.IpSubnet,
                IpGateway = sclRemote.Endpoint.IpGateway
            },
            Association = sclRemote.Association,
            Parameters = sclRemote.Parameters
        };

        var association = ArScl.SclAssistedMmsAssociationPlanBuilder.BuildExact(
            effectiveRemote,
            ArScl.MmsLocalAssociationProfile.SclInteroperabilityDefault);
        warnings.AddRange(association.Warnings);
        if (!association.IsSuccess || association.Plan is null)
        {
            errors.AddRange(association.Errors);
            return Fail(errors, warnings);
        }

        var runtimePlan = new ArScl.SclAssistedMmsAssociationPlan
        {
            Host = normalizedHost,
            Port = normalizedPort,
            IedName = association.Plan.IedName,
            AccessPointName = association.Plan.AccessPointName,
            LocalProfileName = association.Plan.LocalProfileName,
            Cotp = association.Plan.Cotp,
            Association = association.Plan.Association,
            CotpConnectRequest = association.Plan.CotpConnectRequest,
            SessionPresentationAcseMmsRequest = association.Plan.SessionPresentationAcseMmsRequest
        };

        var domains = ArScl.SclMmsDomainInventoryReader.Read(sclXml, normalizedIed, normalizedAccessPoint);
        warnings.AddRange(domains.Warnings);
        if (!domains.IsSuccess)
            errors.AddRange(domains.Errors);

        var design = ArScl.SclInitialFcReadDesignBuilder.Read(sclXml, normalizedIed, normalizedAccessPoint);
        warnings.AddRange(design.Warnings);
        if (!design.IsSuccess)
            errors.AddRange(design.Errors);

        ArMms.InitialFcReadPlan? initialReadPlan = null;
        if (design.IsSuccess && domains.IsSuccess)
        {
            initialReadPlan = ArMms.InitialFcReadPlanner.FromSclModel(
                design.Model,
                domains.ExpectedDomains,
                maximumVariableReferencesPerRead);
            warnings.AddRange(initialReadPlan.Warnings);
            if (!initialReadPlan.IsValid)
                errors.AddRange(initialReadPlan.Errors);
        }

        return new SclAssistedConnectionPreparation
        {
            AssociationPlan = runtimePlan,
            DomainInventory = domains,
            InitialReadDesign = design,
            InitialReadPlan = initialReadPlan,
            Errors = errors.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static SclAssistedConnectionPreparation Fail(
        IReadOnlyCollection<string> errors,
        IReadOnlyCollection<string> warnings)
        => new()
        {
            Errors = errors.Where(message => !string.IsNullOrWhiteSpace(message)).Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = warnings.Where(message => !string.IsNullOrWhiteSpace(message)).Distinct(StringComparer.Ordinal).ToArray()
        };
}
