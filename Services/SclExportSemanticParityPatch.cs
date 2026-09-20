using System.Globalization;
using System.Xml.Linq;
using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Services;

public sealed record SclExportSemanticParityPatchResult(
    int PatchedDataObjects,
    int RemovedInvalidInstanceValues,
    IReadOnlyList<string> Messages)
{
    public bool Changed => PatchedDataObjects > 0 || RemovedInvalidInstanceValues > 0;
}

public static class SclExportSemanticParityPatch
{
    private const string TargetLogicalNodeName = "MPLS_GGIO1";
    private const string TargetPrefix = "MPLS_";
    private const string TargetLnClass = "GGIO";
    private const string TargetLnInst = "1";
    private const string TargetDataObject = "CBClsCounter";

    public static SclExportSemanticParityPatchResult ApplyForLiveModel(
        LiveIedModelDiscoveryDocument liveModel,
        string sclPath)
    {
        ArgumentNullException.ThrowIfNull(liveModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(sclPath);

        if (!File.Exists(sclPath))
            throw new FileNotFoundException("Generated SCL file does not exist.", sclPath);

        // This compatibility correction is deliberately keyed to the exact object
        // proven by the physical comparison. It is not a generic name/value guess.
        var targetExistsInLiveModel = liveModel.LogicalDevices
            .SelectMany(device => device.LogicalNodes)
            .Where(node => string.Equals(node.Name, TargetLogicalNodeName, StringComparison.Ordinal))
            .SelectMany(node => node.DataObjects)
            .Any(dataObject =>
                string.Equals(dataObject.Name, TargetDataObject, StringComparison.Ordinal) &&
                dataObject.Reference.EndsWith(
                    "/" + TargetLogicalNodeName + "." + TargetDataObject,
                    StringComparison.Ordinal));

        if (!targetExistsInLiveModel)
        {
            return new SclExportSemanticParityPatchResult(
                0,
                0,
                Array.Empty<string>());
        }

        var document = XDocument.Load(sclPath, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidDataException("Generated SCL has no root element.");
        var ns = root.Name.Namespace;

        var targetLogicalNodes = document
            .Descendants(ns + "LN")
            .Where(IsTargetLogicalNode)
            .ToArray();

        if (targetLogicalNodes.Length != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one {TargetLogicalNodeName} LN in generated SCL, found {targetLogicalNodes.Length}.");
        }

        var logicalNode = targetLogicalNodes[0];
        var lnTypeId = Attribute(logicalNode, "lnType");
        if (string.IsNullOrWhiteSpace(lnTypeId))
            throw new InvalidDataException($"{TargetLogicalNodeName} has no lnType in generated SCL.");

        var lNodeType = document
            .Descendants(ns + "LNodeType")
            .SingleOrDefault(element =>
                string.Equals(Attribute(element, "id"), lnTypeId, StringComparison.Ordinal))
            ?? throw new InvalidDataException(
                $"LNodeType '{lnTypeId}' for {TargetLogicalNodeName} was not found.");

        var dataObject = lNodeType
            .Elements(ns + "DO")
            .SingleOrDefault(element =>
                string.Equals(Attribute(element, "name"), TargetDataObject, StringComparison.Ordinal))
            ?? throw new InvalidDataException(
                $"{TargetLogicalNodeName}.{TargetDataObject} was not found in LNodeType '{lnTypeId}'.");

        var doTypeId = Attribute(dataObject, "type");
        if (string.IsNullOrWhiteSpace(doTypeId))
            throw new InvalidDataException(
                $"{TargetLogicalNodeName}.{TargetDataObject} has no DOType reference.");

        var originalDoType = document
            .Descendants(ns + "DOType")
            .SingleOrDefault(element =>
                string.Equals(Attribute(element, "id"), doTypeId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"DOType '{doTypeId}' was not found.");

        // If a future exporter interns templates, never let this compatibility repair
        // mutate another object that happens to share the same DOType.
        var referencesToDoType = document
            .Descendants(ns + "DO")
            .Count(element =>
                string.Equals(Attribute(element, "type"), doTypeId, StringComparison.Ordinal));

        XElement targetDoType;
        if (referencesToDoType > 1)
        {
            targetDoType = new XElement(originalDoType);
            var uniqueId = UniqueTypeId(document, ns, doTypeId + "_ARSAS_INS");
            targetDoType.SetAttributeValue("id", uniqueId);
            dataObject.SetAttributeValue("type", uniqueId);
            originalDoType.AddAfterSelf(targetDoType);
        }
        else
        {
            targetDoType = originalDoType;
        }

        var stVal = targetDoType
            .Elements(ns + "DA")
            .SingleOrDefault(element =>
                string.Equals(Attribute(element, "name"), "stVal", StringComparison.Ordinal))
            ?? throw new InvalidDataException(
                $"{TargetLogicalNodeName}.{TargetDataObject} DOType has no root stVal DA.");

        var oldCdc = Attribute(targetDoType, "cdc");
        var oldBType = Attribute(stVal, "bType");

        targetDoType.SetAttributeValue("cdc", "INS");
        stVal.SetAttributeValue("bType", "INT32");
        stVal.Attribute("type")?.Remove();

        var removedInvalidValues = 0;
        foreach (var doi in logicalNode
                     .Elements(ns + "DOI")
                     .Where(element =>
                         string.Equals(Attribute(element, "name"), TargetDataObject, StringComparison.Ordinal)))
        {
            foreach (var dai in doi
                         .Elements(ns + "DAI")
                         .Where(element =>
                             string.Equals(Attribute(element, "name"), "stVal", StringComparison.Ordinal)))
            {
                foreach (var value in dai.Elements(ns + "Val").ToArray())
                {
                    if (int.TryParse(
                            value.Value.Trim(),
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out _))
                    {
                        continue;
                    }

                    value.Remove();
                    removedInvalidValues++;
                }
            }
        }

        document.Save(sclPath, SaveOptions.DisableFormatting);

        return new SclExportSemanticParityPatchResult(
            1,
            removedInvalidValues,
            [
                $"{TargetLogicalNodeName}.{TargetDataObject}: CDC {oldCdc} -> INS, stVal {oldBType} -> INT32."
            ]);
    }

    private static bool IsTargetLogicalNode(XElement element)
        => string.Equals(Attribute(element, "prefix"), TargetPrefix, StringComparison.Ordinal) &&
           string.Equals(Attribute(element, "lnClass"), TargetLnClass, StringComparison.Ordinal) &&
           string.Equals(Attribute(element, "inst"), TargetLnInst, StringComparison.Ordinal);

    private static string UniqueTypeId(XDocument document, XNamespace ns, string candidate)
    {
        var used = document
            .Descendants(ns + "DOType")
            .Select(element => Attribute(element, "id"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);

        if (!used.Contains(candidate))
            return candidate;

        for (var index = 2; ; index++)
        {
            var next = candidate + "_" + index.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(next))
                return next;
        }
    }

    private static string Attribute(XElement element, string name)
        => element.Attributes()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))
            ?.Value
            ?? string.Empty;
}
