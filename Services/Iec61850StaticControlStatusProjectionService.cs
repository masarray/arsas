using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed record Iec61850StaticControlStatusProjectionResult(
    IReadOnlyList<SignalDefinition> AddedSignals,
    int LinkedControlCount)
{
    public int AddedCount => AddedSignals.Count;
}

/// <summary>
/// Materializes the reportable status facet of an IEC 61850 position control object.
///
/// A static FCDA such as CSWI1.Pos or XCBR1.Pos is dual-role: the DO itself is the
/// control object, while its ST primary value (Pos.stVal) is process feedback that belongs
/// in Live Signal Values. ARSAS must not force one SignalDefinition to serve both roles,
/// because the normal runtime boundary intentionally rejects control objects.
///
/// This bridge is deliberately narrow and engine-authoritative. It only projects a status
/// row when ARIEC proves an exact static DataSet membership with FC=ST and an exact resolved
/// primary .stVal leaf, and an existing exact control object matches that same membership.
/// No prefix/fuzzy matching and no Oper/SBO/CtlVal reconstruction is permitted here.
/// </summary>
public static class Iec61850StaticControlStatusProjectionService
{
    private static readonly HashSet<string> PositionLogicalNodeClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CSWI", "XCBR", "XSWI"
    };

    public static Iec61850StaticControlStatusProjectionResult EnsureProjections(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var authorityModel = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel;
        if (authorityModel is null)
            return EmptyResult();

        var mandatory = Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(authorityModel);
        if (mandatory.Count == 0)
            return EmptyResult();

        var added = new List<SignalDefinition>();
        var linkedControls = new HashSet<SignalDefinition>(ReferenceEqualityComparer.Instance);

        foreach (var descriptor in mandatory)
        {
            if (!IsExactStaticPositionStatusDescriptor(descriptor))
                continue;

            foreach (var membership in descriptor.DataSetMemberships
                         .OrderBy(item => item.DataSetReference, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.MemberIndex))
            {
                var memberReference = FirstNonEmpty(
                    membership.CanonicalMemberReference,
                    membership.OriginalMemberReference,
                    descriptor.DesignReference,
                    descriptor.ObservedReference);
                if (string.IsNullOrWhiteSpace(memberReference) ||
                    string.IsNullOrWhiteSpace(membership.DataSetReference) ||
                    !IsExactPositionMemberReference(memberReference))
                {
                    continue;
                }

                var control = FindExactControlCompanion(
                    device.Signals,
                    membership.DataSetReference,
                    memberReference);
                if (control is null)
                    continue;

                LinkControlToMembership(control, descriptor, membership, memberReference);
                linkedControls.Add(control);

                var primaryValueReference = descriptor.PrimaryValueReference.Trim();
                var existingStatus = device.Signals.FirstOrDefault(signal =>
                    !signal.IsControlSignal &&
                    LiteralEquals(signal.DataSetReference, membership.DataSetReference) &&
                    LiteralEquals(signal.DisplayReference, memberReference) &&
                    LiteralEquals(signal.ObjectReference, primaryValueReference));
                if (existingStatus is not null)
                    continue;

                var report = descriptor.ReportMemberships.FirstOrDefault();
                var status = new SignalDefinition
                {
                    Name = FirstNonEmpty(descriptor.DataObject, descriptor.DataAttributePath, memberReference),
                    ObjectReference = primaryValueReference,
                    DisplayReference = memberReference,
                    FunctionalConstraint = "ST",
                    DataType = FirstNonEmpty(descriptor.MmsType, descriptor.SclBType, "Unknown"),
                    Category = "Position",
                    Confidence = "High",
                    DataSetReference = membership.DataSetReference,
                    ReportControlReference = report?.ReportControlReference ?? string.Empty,
                    QualityReference = descriptor.QualityReference,
                    TimestampReference = descriptor.TimestampReference,
                    Source = "ARIEC61850 static DataSet • dual-role control status projection",
                    IsSelected = false,
                    IsReportCapable = true,
                    ReportCoverage = report is null
                        ? "Static DataSet position status"
                        : "Static report/DataSet position status",
                    ReportCoverageReason =
                        $"Exact ARIEC static FCDA {memberReference} ({membership.DataSetReference}[{membership.MemberIndex}]) " +
                        $"resolves to ST primary value {primaryValueReference}; the control DO remains a separate command companion.",
                    ProbeStatus = "Not probed",
                    Value = "-",
                    Quality = "Unknown",
                    DeviceTimestamp = "-"
                };

                device.Signals.Add(status);
                added.Add(status);
            }
        }

        return new Iec61850StaticControlStatusProjectionResult(added, linkedControls.Count);
    }

    private static bool IsExactStaticPositionStatusDescriptor(Iec61850SignalDescriptor descriptor)
    {
        if (!descriptor.FunctionalConstraint.Equals("ST", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(descriptor.PrimaryValueReference))
        {
            return false;
        }

        var primary = NormalizeReference(descriptor.PrimaryValueReference);
        return primary.EndsWith(".stval", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExactPositionMemberReference(string memberReference)
    {
        if (!SignalDefinition.IsControlObjectReference(memberReference))
            return false;

        var normalized = NormalizeReference(memberReference);
        if (!normalized.EndsWith(".pos", StringComparison.OrdinalIgnoreCase))
            return false;

        var slash = normalized.IndexOf('/');
        if (slash < 0 || slash == normalized.Length - 1)
            return false;

        var afterSlash = normalized[(slash + 1)..];
        var logicalNode = afterSlash.Split('.', 2)[0];
        var logicalNodeClass = SignalDefinition.DetectLogicalNodeClass(logicalNode);
        return PositionLogicalNodeClasses.Contains(logicalNodeClass);
    }

    private static SignalDefinition? FindExactControlCompanion(
        IEnumerable<SignalDefinition> signals,
        string dataSetReference,
        string memberReference)
    {
        return signals
            .Where(signal => signal.IsControlSignal && signal.IsValidControlObject)
            .Where(signal =>
                string.IsNullOrWhiteSpace(signal.DataSetReference) ||
                LiteralEquals(signal.DataSetReference, dataSetReference))
            .Where(signal =>
                LiteralEquals(signal.DisplayReference, memberReference) ||
                LiteralEquals(signal.ObjectReference, memberReference))
            .OrderByDescending(signal => LiteralEquals(signal.DisplayReference, memberReference))
            .FirstOrDefault();
    }

    private static void LinkControlToMembership(
        SignalDefinition control,
        Iec61850SignalDescriptor descriptor,
        Iec61850SignalDataSetMembership membership,
        string memberReference)
    {
        // Exact FCDA authority only. These fields let StaticDataSetAuthoritySelection select
        // the command companion without making that control object publishable as a process row.
        control.DisplayReference = memberReference;
        control.DataSetReference = membership.DataSetReference;
        control.IsReportCapable = true;

        var report = descriptor.ReportMemberships.FirstOrDefault();
        if (report is not null && string.IsNullOrWhiteSpace(control.ReportControlReference))
            control.ReportControlReference = report.ReportControlReference;

        if (string.IsNullOrWhiteSpace(control.QualityReference))
            control.QualityReference = descriptor.QualityReference;
        if (string.IsNullOrWhiteSpace(control.TimestampReference))
            control.TimestampReference = descriptor.TimestampReference;
    }

    private static Iec61850StaticControlStatusProjectionResult EmptyResult()
        => new(Array.Empty<SignalDefinition>(), 0);

    private static bool LiteralEquals(string? left, string? right)
        => string.Equals(
            (left ?? string.Empty).Trim(),
            (right ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
