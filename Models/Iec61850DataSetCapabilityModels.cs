namespace ArIED61850Tester.Models;

public sealed class Iec61850DataSetMemberCapability
{
    public int Index { get; init; }
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
}

public sealed class Iec61850ReportControlCapability
{
    public string Reference { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public bool Indexed { get; init; }
    public string ReportId { get; init; } = string.Empty;
    public string ConfRev { get; init; } = string.Empty;
    public string TriggerOptions { get; init; } = string.Empty;
    public string OptionalFields { get; init; } = string.Empty;
    public string BufferTimeMs { get; init; } = string.Empty;
    public string IntegrityPeriodMs { get; init; } = string.Empty;
}

public sealed class Iec61850DataSetCapability
{
    public string Reference { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool? IsDeletable { get; init; }
    public string Fingerprint { get; init; } = string.Empty;
    public IReadOnlyList<Iec61850DataSetMemberCapability> Members { get; init; } =
        Array.Empty<Iec61850DataSetMemberCapability>();
    public IReadOnlyList<Iec61850ReportControlCapability> ReportControls { get; init; } =
        Array.Empty<Iec61850ReportControlCapability>();

    public int MemberCount => Members.Count;
    public int StructurallyResolvedMemberCount => Members.Count(member =>
        !string.IsNullOrWhiteSpace(member.Reference) &&
        !string.IsNullOrWhiteSpace(member.FunctionalConstraint));
    public bool HasConfiguredReportControl => ReportControls.Count > 0;
}

public sealed class Iec61850DataSetCapabilityIndex
{
    public long Generation { get; init; }
    public string Source { get; init; } = string.Empty;
    public string IedName { get; init; } = string.Empty;
    public string AccessPointName { get; init; } = string.Empty;
    public string ModelFingerprint { get; init; } = string.Empty;
    public string DataSetFingerprint { get; init; } = string.Empty;
    public string ReportBindingFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<Iec61850DataSetCapability> DataSets { get; init; } =
        Array.Empty<Iec61850DataSetCapability>();

    public int DataSetCount => DataSets.Count;
    public int MemberCount => DataSets.Sum(dataSet => dataSet.MemberCount);
    public int ReportReadyDataSetCount => DataSets.Count(dataSet => dataSet.HasConfiguredReportControl);
    public bool HasDataSets => DataSetCount > 0;
}

public sealed class Iec61850DataSetCapabilityParity
{
    public bool DataSetsMatch { get; init; }
    public bool ReportBindingsMatch { get; init; }
    public bool IsEquivalent => DataSetsMatch && ReportBindingsMatch;
    public string Summary { get; init; } = string.Empty;
}
