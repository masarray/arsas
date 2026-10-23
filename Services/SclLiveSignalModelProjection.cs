using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Converts the application-neutral signal rows produced by ARIEC61850 discovery into
/// a bounded live-model projection that can be evaluated by the engine SCL comparer.
/// It does not parse SCL or infer protocol services from XML.
/// </summary>
public static class SclLiveSignalModelProjection
{
    public static LiveIedModelDiscoveryDocument Build(
        string iedName,
        string accessPointName,
        IReadOnlyList<SignalDefinition> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var attributes = signals
            .SelectMany(ToAttributeRows)
            .GroupBy(row => row.Reference, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var logicalDevices = attributes
            .GroupBy(row => row.Domain, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(domainGroup => new LiveIedLogicalDeviceModel
            {
                MmsDomain = domainGroup.Key,
                Inst = ResolveLdInst(domainGroup.Key, iedName),
                LogicalNodes = domainGroup
                    .GroupBy(row => row.LogicalNode, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(logicalNodeGroup => BuildLogicalNode(logicalNodeGroup.Key, logicalNodeGroup))
                    .ToArray()
            })
            .ToArray();

        var dataSets = BuildDataSets(signals);
        var reports = BuildReportControls(signals);
        var logicalNodes = logicalDevices.SelectMany(device => device.LogicalNodes).ToArray();
        var dataObjects = logicalNodes.SelectMany(node => node.DataObjects).ToArray();
        var dataAttributes = dataObjects.SelectMany(dataObject => dataObject.Attributes).ToArray();

        return new LiveIedModelDiscoveryDocument
        {
            Source = "ArIEDSignalProjection",
            IedName = iedName ?? string.Empty,
            AccessPointName = accessPointName ?? string.Empty,
            LogicalDevices = logicalDevices,
            DataSets = dataSets,
            ReportControls = reports,
            Coverage = new LiveIedModelDiscoveryCoverage
            {
                LogicalDeviceCount = logicalDevices.Length,
                LogicalNodeCount = logicalNodes.Length,
                DataObjectCount = dataObjects.Length,
                DataAttributeCount = dataAttributes.Length,
                ExactFunctionalConstraintCount = dataAttributes.Count(attribute =>
                    attribute.FunctionalConstraintConfidence == LiveIedDiscoveryConfidenceLevel.Exact),
                ExactMmsTypeCount = dataAttributes.Count(attribute =>
                    attribute.TypeConfidence is LiveIedDiscoveryConfidenceLevel.Exact or LiveIedDiscoveryConfidenceLevel.High),
                DataSetCount = dataSets.Length,
                ReportControlCount = reports.Length,
                BufferedReportControlCount = reports.Count(report => report.Buffered),
                UnbufferedReportControlCount = reports.Count(report => !report.Buffered)
            },
            Summary = $"ArIED signal projection: LD={logicalDevices.Length}, LN={logicalNodes.Length}, DO={dataObjects.Length}, DA={dataAttributes.Length}, DataSet={dataSets.Length}, RCB={reports.Length}."
        };
    }

    private static LiveIedLogicalNodeModel BuildLogicalNode(
        string logicalNodeName,
        IEnumerable<AttributeRow> rows)
    {
        var materialized = rows.ToArray();
        var lnClass = SignalDefinition.DetectLogicalNodeClass(logicalNodeName);
        var dataObjects = materialized
            .GroupBy(row => row.DataObject, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LiveIedDataObjectModel
            {
                Reference = $"{group.First().Domain}/{logicalNodeName}.{group.Key}",
                Name = group.Key,
                InferredCdc = group.Select(row => row.Cdc).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                CdcConfidence = 0.90,
                ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
                Evidence = new[] { "Projected from ARIEC61850 signal discovery output." },
                Attributes = group
                    .OrderBy(row => row.AttributePath, StringComparer.OrdinalIgnoreCase)
                    .Select(row => new LiveIedDataAttributeModel
                    {
                        ObjectReference = row.Reference,
                        AttributePath = row.AttributePath,
                        FunctionalConstraint = row.FunctionalConstraint,
                        MmsReference = row.Reference,
                        MmsItemName = row.Reference.Contains('/')
                            ? row.Reference[(row.Reference.IndexOf('/') + 1)..]
                            : row.Reference,
                        Source = "ArIED.SignalDefinition",
                        SclBType = row.DataType,
                        MmsType = row.DataType,
                        MmsTypeSignature = row.DataType,
                        TypeDiscoveryStatus = "Projected",
                        TypeDiscoveryMessage = "Projected from ARIEC61850 live signal discovery.",
                        TypeSource = "ARIEC61850 live discovery",
                        TypeConfidence = LiveIedDiscoveryConfidenceLevel.High,
                        FunctionalConstraintConfidence = string.IsNullOrWhiteSpace(row.FunctionalConstraint)
                            ? LiveIedDiscoveryConfidenceLevel.Unknown
                            : LiveIedDiscoveryConfidenceLevel.Exact
                    })
                    .ToArray()
            })
            .ToArray();

        return new LiveIedLogicalNodeModel
        {
            Name = logicalNodeName,
            LnClass = lnClass,
            ProposedLnTypeId = $"LN_{lnClass}_{logicalNodeName}",
            FunctionalConstraintCounts = dataObjects
                .SelectMany(dataObject => dataObject.Attributes)
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute.FunctionalConstraint))
                .GroupBy(attribute => attribute.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase),
            DataObjects = dataObjects
        };
    }

    private static IEnumerable<AttributeRow> ToAttributeRows(SignalDefinition signal)
    {
        if (!TryParseReference(signal.ObjectReference, out var parsed))
            yield break;

        if (!signal.IsControlSignal)
        {
            yield return new AttributeRow(
                parsed.Domain,
                parsed.LogicalNode,
                parsed.DataObject,
                parsed.AttributePath,
                signal.ObjectReference,
                signal.FunctionalConstraint,
                NormalizeDataType(signal.DataType),
                string.Empty);
            yield break;
        }

        if (TryParseReference(signal.ControlModelReference, out var ctlModel))
        {
            yield return new AttributeRow(
                ctlModel.Domain,
                ctlModel.LogicalNode,
                ctlModel.DataObject,
                ctlModel.AttributePath,
                signal.ControlModelReference,
                "CF",
                "Enum",
                signal.ControlCdc);
        }
    }

    private static LiveIedDataSetModel[] BuildDataSets(IReadOnlyList<SignalDefinition> signals)
        => signals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.DataSetReference))
            .GroupBy(signal => signal.DataSetReference, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                ParseContainerReference(group.Key, out var domain, out var logicalNode, out var name);
                var members = group
                    .Where(signal => !signal.IsControlSignal)
                    .GroupBy(signal => signal.ObjectReference, StringComparer.OrdinalIgnoreCase)
                    .Select((member, index) => new LiveIedDataSetMemberModel
                    {
                        Index = index + 1,
                        Reference = member.First().ObjectReference,
                        FunctionalConstraint = member.First().FunctionalConstraint,
                        MmsReference = member.First().ObjectReference,
                        Confidence = LiveIedDiscoveryConfidenceLevel.High
                    })
                    .ToArray();
                return new LiveIedDataSetModel
                {
                    Reference = group.Key,
                    Domain = domain,
                    LogicalNode = logicalNode,
                    Name = name,
                    MemberCount = members.Length,
                    Members = members
                };
            })
            .ToArray();

    private static LiveIedReportControlModel[] BuildReportControls(IReadOnlyList<SignalDefinition> signals)
        => signals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.ReportControlReference))
            .GroupBy(signal => signal.ReportControlReference, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                ParseContainerReference(group.Key, out var domain, out var logicalNode, out var name);
                var reference = group.Key.Replace('$', '.');
                return new LiveIedReportControlModel
                {
                    Reference = group.Key,
                    Domain = domain,
                    LogicalNode = logicalNode,
                    Name = name,
                    Buffered = reference.Contains(".BR.", StringComparison.OrdinalIgnoreCase),
                    DataSetReference = group.Select(signal => signal.DataSetReference)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                    Status = "Projected from ARIEC61850 live signal bindings"
                };
            })
            .ToArray();

    private static bool TryParseReference(string? reference, out ParsedReference parsed)
    {
        parsed = default;
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        var slash = text.IndexOf('/');
        if (slash <= 0 || slash >= text.Length - 1)
            return false;

        var domain = text[..slash];
        var parts = text[(slash + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        parsed = new ParsedReference(
            domain,
            parts[0],
            parts[1],
            parts.Length > 2 ? string.Join(".", parts.Skip(2)) : "ctlModel");
        return true;
    }

    private static void ParseContainerReference(
        string reference,
        out string domain,
        out string logicalNode,
        out string name)
    {
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        var slash = text.IndexOf('/');
        domain = slash > 0 ? text[..slash] : string.Empty;
        var remainder = slash >= 0 && slash < text.Length - 1 ? text[(slash + 1)..] : text;
        var parts = remainder.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        logicalNode = parts.Length > 0 ? parts[0] : string.Empty;
        name = parts.Length > 0 ? parts[^1] : string.Empty;
    }

    private static string ResolveLdInst(string domain, string iedName)
        => !string.IsNullOrWhiteSpace(iedName) && domain.StartsWith(iedName, StringComparison.OrdinalIgnoreCase)
            ? domain[iedName.Length..]
            : domain;

    private static string NormalizeDataType(string? dataType)
    {
        var value = (dataType ?? string.Empty).Trim();
        var separator = value.IndexOf(' ');
        return separator > 0 ? value[..separator] : value;
    }

    private readonly record struct ParsedReference(
        string Domain,
        string LogicalNode,
        string DataObject,
        string AttributePath);

    private readonly record struct AttributeRow(
        string Domain,
        string LogicalNode,
        string DataObject,
        string AttributePath,
        string Reference,
        string FunctionalConstraint,
        string DataType,
        string Cdc);
}
