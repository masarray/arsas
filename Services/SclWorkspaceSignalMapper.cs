using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Presentation adapter from the engine-owned SCL workspace model to ArIED signal rows.
/// This class deliberately contains no XML parsing, IEC 61850 type-template traversal,
/// FCD expansion, or CDC value-selection semantics.
/// </summary>
public static class SclWorkspaceSignalMapper
{
    private static readonly HashSet<string> RuntimeLeafNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "stVal", "general", "posVal", "actVal", "setVal", "f", "i"
    };

    public static IReadOnlyList<SignalDefinition> BuildSignals(SclIedWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        // The engine is authoritative for FCD/FCDA -> DataAttribute semantics.
        // ARSAS only projects the engine result into operator-facing rows.
        var semanticBindings = Iec61850DataSetSemanticBindingResolver.Resolve(workspace.DesignModel);
        var dataSetBindings = BuildDataSetBindings(semanticBindings);
        var staticPrimaryReferences = BuildStaticPrimaryReferences(semanticBindings);
        var reportBindings = BuildReportBindings(workspace.DesignModel);
        var signals = new List<SignalDefinition>();

        foreach (var logicalDevice in workspace.DesignModel.LogicalDevices)
        {
            foreach (var logicalNode in logicalDevice.LogicalNodes)
            {
                foreach (var dataObject in logicalNode.DataObjects)
                {
                    AddRuntimeSignals(signals, logicalNode, dataObject, dataSetBindings, reportBindings);
                    AddControlSignal(signals, logicalNode, dataObject, dataSetBindings, reportBindings);
                }
            }
        }

        return signals
            .Where(signal => SasOperationalSignalPolicy.IsVisible(signal) ||
                             staticPrimaryReferences.Contains(signal.ObjectReference))
            .GroupBy(signal => NormalizePresentationReference(signal.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(signal => signal.SortPriority)
            .ThenBy(signal => signal.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddRuntimeSignals(
        ICollection<SignalDefinition> signals,
        LiveIedLogicalNodeModel logicalNode,
        LiveIedDataObjectModel dataObject,
        IReadOnlyDictionary<string, string> dataSetBindings,
        IReadOnlyDictionary<string, string> reportBindings)
    {
        var quality = FindCompanion(dataObject, "q");
        var timestamp = FindCompanion(dataObject, "t");

        foreach (var attribute in dataObject.Attributes)
        {
            var fc = (attribute.FunctionalConstraint ?? string.Empty).Trim().ToUpperInvariant();
            if (fc is not ("ST" or "MX") || IsCompanion(attribute.AttributePath))
                continue;
            if (!IsRuntimeLeaf(attribute.AttributePath))
                continue;

            var reference = attribute.ObjectReference;
            dataSetBindings.TryGetValue(reference, out var dataSetReference);
            reportBindings.TryGetValue(dataSetReference ?? string.Empty, out var reportReference);
            var category = ResolveCategory(logicalNode.LnClass, dataObject.Name, dataObject.InferredCdc, fc);

            signals.Add(new SignalDefinition
            {
                Name = BuildDisplayName(logicalNode.Name, dataObject.Name, attribute.AttributePath),
                ObjectReference = reference,
                DisplayReference = reference,
                FunctionalConstraint = fc,
                DataType = ResolveDataType(attribute),
                Category = category,
                Confidence = attribute.TypeConfidence is LiveIedDiscoveryConfidenceLevel.Exact or LiveIedDiscoveryConfidenceLevel.High
                    ? "High"
                    : "Medium",
                DataSetReference = dataSetReference ?? string.Empty,
                ReportControlReference = reportReference ?? string.Empty,
                ReportCoverageReason = string.IsNullOrWhiteSpace(reportReference)
                    ? "SCL design model contains no static ReportControl coverage; polling remains the safe fallback."
                    : $"Static SCL report candidate {reportReference}; live RCB attributes are verified before enable.",
                QualityReference = quality?.ObjectReference ?? string.Empty,
                TimestampReference = timestamp?.ObjectReference ?? string.Empty,
                Source = "SCL design model",
                IsReportCapable = !string.IsNullOrWhiteSpace(reportReference),
                ReportCoverage = string.IsNullOrWhiteSpace(reportReference)
                    ? "MMS polling fallback"
                    : "Static SCL report candidate",
                IsSelected = false,
                Value = "-",
                Quality = "Unknown",
                DeviceTimestamp = "-",
                ProbeStatus = "Projected from SCL; live read verification pending"
            });
        }
    }

    private static void AddControlSignal(
        ICollection<SignalDefinition> signals,
        LiveIedLogicalNodeModel logicalNode,
        LiveIedDataObjectModel dataObject,
        IReadOnlyDictionary<string, string> dataSetBindings,
        IReadOnlyDictionary<string, string> reportBindings)
    {
        var controlAttributes = dataObject.Attributes
            .Where(attribute => string.Equals(attribute.FunctionalConstraint, "CO", StringComparison.OrdinalIgnoreCase) ||
                                Leaf(attribute.AttributePath).Equals("ctlModel", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (controlAttributes.Length == 0)
            return;

        var controlReference = dataObject.Reference;
        var ctlModel = dataObject.Attributes.FirstOrDefault(attribute =>
            Leaf(attribute.AttributePath).Equals("ctlModel", StringComparison.OrdinalIgnoreCase));
        var status = dataObject.Attributes.FirstOrDefault(attribute =>
            Leaf(attribute.AttributePath).Equals("stVal", StringComparison.OrdinalIgnoreCase));
        dataSetBindings.TryGetValue(status?.ObjectReference ?? string.Empty, out var dataSetReference);
        reportBindings.TryGetValue(dataSetReference ?? string.Empty, out var reportReference);

        signals.Add(new SignalDefinition
        {
            Name = $"{logicalNode.Name} {dataObject.Name}",
            ObjectReference = controlReference,
            DisplayReference = controlReference,
            FunctionalConstraint = "CO",
            DataType = string.IsNullOrWhiteSpace(dataObject.InferredCdc)
                ? "IEC 61850 control"
                : $"{dataObject.InferredCdc} control",
            Category = "Control",
            Confidence = "High",
            DataSetReference = dataSetReference ?? string.Empty,
            ReportControlReference = reportReference ?? string.Empty,
            ReportCoverageReason = "Control execution is disabled until the live ctlModel and exact MMS Oper/SBOw/Cancel structures are inspected.",
            Source = "SCL design model",
            IsControlSignal = true,
            ControlCdc = dataObject.InferredCdc,
            ControlModelReference = ctlModel?.ObjectReference ?? $"{controlReference}.ctlModel",
            ControlStatusReference = status?.ObjectReference ?? string.Empty,
            ControlModelText = "SCL design • live verification required",
            ControlValueType = ResolveControlValueType(dataObject.InferredCdc),
            IsSelected = false,
            Value = "-",
            Quality = "Unknown",
            DeviceTimestamp = "-",
            ProbeStatus = "SCL control candidate; live verification required"
        });
    }

    private static Dictionary<string, string> BuildDataSetBindings(LiveIedDataSetSemanticBindingDocument semanticBindings)
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in semanticBindings.Members)
        {
            if (!member.IsResolved)
                continue;

            foreach (var attribute in member.ResolvedAttributes)
            {
                if (!string.IsNullOrWhiteSpace(attribute.Reference))
                    bindings.TryAdd(attribute.Reference, member.DataSetReference);
            }
        }

        return bindings;
    }

    private static HashSet<string> BuildStaticPrimaryReferences(LiveIedDataSetSemanticBindingDocument semanticBindings)
        => semanticBindings.Members
            .Select(member => member.PrimaryValueReference)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> BuildReportBindings(LiveIedModelDiscoveryDocument model)
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var report in model.ReportControls)
        {
            if (!string.IsNullOrWhiteSpace(report.DataSetReference))
                bindings.TryAdd(report.DataSetReference, report.Reference);
        }
        return bindings;
    }

    private static LiveIedDataAttributeModel? FindCompanion(LiveIedDataObjectModel dataObject, string leaf)
        => dataObject.Attributes.FirstOrDefault(attribute => Leaf(attribute.AttributePath).Equals(leaf, StringComparison.OrdinalIgnoreCase));

    private static bool IsCompanion(string path)
    {
        var leaf = Leaf(path);
        return leaf.Equals("q", StringComparison.OrdinalIgnoreCase) ||
               leaf.Equals("t", StringComparison.OrdinalIgnoreCase) ||
               leaf.Equals("ctlModel", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRuntimeLeaf(string path)
    {
        var leaf = Leaf(path);
        if (RuntimeLeafNames.Contains(leaf))
            return true;

        var normalized = path.Replace('$', '.');
        return normalized.EndsWith("mag.f", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("ang.f", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("instMag.i", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDataType(LiveIedDataAttributeModel attribute)
    {
        if (!string.IsNullOrWhiteSpace(attribute.SclBType))
            return attribute.SclBType;
        if (!string.IsNullOrWhiteSpace(attribute.MmsType))
            return attribute.MmsType;
        return "IEC 61850 value";
    }

    private static string ResolveCategory(string lnClass, string doName, string cdc, string fc)
    {
        if (fc.Equals("MX", StringComparison.OrdinalIgnoreCase))
            return "Measurement";
        if (doName.Equals("Pos", StringComparison.OrdinalIgnoreCase) || cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase))
            return "Position";
        if (lnClass.StartsWith('P') || doName.Equals("Op", StringComparison.OrdinalIgnoreCase) || doName.Equals("Str", StringComparison.OrdinalIgnoreCase))
            return "Protection";
        return "Status";
    }

    private static string ResolveControlValueType(string cdc)
        => (cdc ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "DPC" => "Dbpos",
            "SPC" => "Boolean",
            "INC" or "ISC" or "BSC" => "Int32",
            "APC" or "BAC" => "Float32",
            _ => string.Empty
        };

    private static string BuildDisplayName(string logicalNode, string dataObject, string attributePath)
        => $"{logicalNode} {dataObject} {attributePath}";

    private static string Leaf(string? path)
    {
        var text = (path ?? string.Empty).Replace('$', '.').Trim('.');
        var index = text.LastIndexOf('.');
        return index >= 0 ? text[(index + 1)..] : text;
    }

    /// <summary>
    /// Presentation-only dedup normalization. It must not be used to infer
    /// DataSet membership or IEC 61850 semantic targets; the engine owns that.
    /// </summary>
    private static string NormalizePresentationReference(string? reference)
    {
        var text = (reference ?? string.Empty).Trim().Replace('$', '.').Replace("//", "/", StringComparison.Ordinal);
        return text.ToUpperInvariant();
    }
}
