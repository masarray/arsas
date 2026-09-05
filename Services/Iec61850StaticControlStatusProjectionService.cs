using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed record Iec61850StaticControlStatusProjectionResult(
    IReadOnlyList<SignalDefinition> AddedSignals,
    int LinkedControlCount)
{
    public int AddedCount => AddedSignals.Count;
    public int AddedControlCount => AddedSignals.Count(signal => signal.IsControlSignal);
    public int AddedRuntimeFeedbackCount => AddedSignals.Count(signal => !signal.IsControlSignal);
}

/// <summary>
/// Preserves the two distinct facets of an IEC 61850 control DataObject that is carried by
/// an authoritative static DataSet.
///
/// The exact SCL/ARIEC CDC + DataObjectReference identify the command target. That control
/// companion is retained even when no scalar feedback attribute can be resolved yet. When
/// ARIEC also proves one exact ST/MX PrimaryValueReference, a separate non-control runtime
/// feedback row may be materialized. This keeps command discovery independent from process
/// acquisition without weakening SignalDefinition.CanPublishToRuntime.
///
/// No control is inferred from object names. No Oper/SBO/SBOw/Cancel/ctlVal/ctlModel path is
/// reconstructed. Every companion is rooted in an exact DataSet membership plus a standard
/// controllable CDC, and every runtime feedback reference comes directly from ARIEC semantic
/// authority. Unsupported/ambiguous feedback therefore fails closed while the proven control
/// object remains available for live ctlModel inspection.
/// </summary>
public static class Iec61850StaticControlStatusProjectionService
{
    // IEC 61850 controllable CDC families handled by ARSAS' command surface. Keep status-only
    // CDCs (SPS/DPS/INS/ENS, etc.) out: their presence in a DataSet is not command authority.
    private static readonly HashSet<string> ControllableCdcs = new(StringComparer.OrdinalIgnoreCase)
    {
        "SPC", // single point control
        "DPC", // double point / switch position control
        "INC", // integer step control
        "ISC", // integer status/control variant used by regulating devices
        "APC", // analog process control
        "BAC", // binary controlled analog
        "BSC", // binary controlled step position
        "ENC"  // enumerated control
    };

    private static readonly HashSet<string> ControlServicePathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "ctlModel", "ctlVal", "ctlNum", "stSeld", "SBO", "SBOw", "Oper", "Cancel",
        "origin", "T", "Test", "Check", "operTm", "sboClass", "sboTimeout", "operTimeout"
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
            foreach (var membership in descriptor.DataSetMemberships
                         .OrderBy(item => item.DataSetReference, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.MemberIndex))
            {
                var cdc = FirstNonEmpty(descriptor.Cdc, membership.Cdc).ToUpperInvariant();
                if (!ControllableCdcs.Contains(cdc))
                    continue;

                var memberReference = FirstNonEmpty(
                    membership.CanonicalMemberReference,
                    membership.OriginalMemberReference,
                    descriptor.DesignReference,
                    descriptor.ObservedReference);
                var dataObjectReference = (descriptor.DataObjectReference ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(memberReference) ||
                    string.IsNullOrWhiteSpace(membership.DataSetReference) ||
                    string.IsNullOrWhiteSpace(dataObjectReference) ||
                    !SignalDefinition.IsControlObjectReference(dataObjectReference))
                {
                    continue;
                }

                var exactFeedbackReference = ResolveExactFeedbackReference(descriptor, membership);

                // Do not attach a command target to q/t or other companion-only FCDA rows.
                // The member must either be the control DO/FCD itself or the exact primary
                // feedback FCDA that ARIEC resolved for that same DataObject.
                if (!IsControlBearingMembership(memberReference, dataObjectReference, exactFeedbackReference))
                    continue;

                var control = FindExactControlCompanion(
                    device.Signals,
                    membership.DataSetReference,
                    memberReference,
                    dataObjectReference);
                if (control is null)
                {
                    control = CreateControlCompanion(
                        descriptor,
                        membership,
                        cdc,
                        memberReference,
                        dataObjectReference,
                        exactFeedbackReference);
                    device.Signals.Add(control);
                    added.Add(control);
                }
                else
                {
                    LinkControlToMembership(
                        control,
                        descriptor,
                        membership,
                        cdc,
                        memberReference,
                        exactFeedbackReference);
                }
                linkedControls.Add(control);

                if (string.IsNullOrWhiteSpace(exactFeedbackReference))
                    continue;

                var existingFeedback = device.Signals.FirstOrDefault(signal =>
                    !signal.IsControlSignal &&
                    LiteralEquals(signal.DataSetReference, membership.DataSetReference) &&
                    LiteralEquals(signal.DisplayReference, memberReference) &&
                    LiteralEquals(signal.ObjectReference, exactFeedbackReference));
                if (existingFeedback is not null)
                    continue;

                var feedback = CreateRuntimeFeedback(
                    descriptor,
                    membership,
                    cdc,
                    memberReference,
                    exactFeedbackReference);

                // PrimaryValueReference is semantic authority, but Live Signal admission has
                // its own process-value contract. If the exact leaf is not a supported runtime
                // value shape, retain the control companion and fail closed on feedback.
                if (!feedback.CanPublishAsSignal)
                    continue;

                device.Signals.Add(feedback);
                added.Add(feedback);
            }
        }

        return new Iec61850StaticControlStatusProjectionResult(added, linkedControls.Count);
    }

    internal static bool IsControllableCdc(string? cdc)
        => ControllableCdcs.Contains((cdc ?? string.Empty).Trim());

    private static string ResolveExactFeedbackReference(
        Iec61850SignalDescriptor descriptor,
        Iec61850SignalDataSetMembership membership)
    {
        var reference = (descriptor.PrimaryValueReference ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reference))
            return string.Empty;

        var fc = FirstNonEmpty(descriptor.FunctionalConstraint, membership.FunctionalConstraint).ToUpperInvariant();
        if (fc is not ("ST" or "MX"))
            return string.Empty;

        var segments = NormalizeReference(reference)
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Any(segment => ControlServicePathSegments.Contains(segment)))
            return string.Empty;

        return reference;
    }

    private static bool IsControlBearingMembership(
        string memberReference,
        string dataObjectReference,
        string exactFeedbackReference)
    {
        if (LiteralEquals(memberReference, dataObjectReference))
            return true;

        return !string.IsNullOrWhiteSpace(exactFeedbackReference) &&
               LiteralEquals(memberReference, exactFeedbackReference);
    }

    private static SignalDefinition? FindExactControlCompanion(
        IEnumerable<SignalDefinition> signals,
        string dataSetReference,
        string memberReference,
        string dataObjectReference)
    {
        return signals
            .Where(signal => signal.IsControlSignal && signal.IsValidControlObject)
            .Where(signal => LiteralEquals(signal.ObjectReference, dataObjectReference))
            .Where(signal =>
                string.IsNullOrWhiteSpace(signal.DataSetReference) ||
                LiteralEquals(signal.DataSetReference, dataSetReference))
            .Where(signal =>
                string.IsNullOrWhiteSpace(signal.DisplayReference) ||
                LiteralEquals(signal.DisplayReference, memberReference) ||
                LiteralEquals(signal.DisplayReference, dataObjectReference))
            .OrderByDescending(signal => LiteralEquals(signal.DataSetReference, dataSetReference))
            .ThenByDescending(signal => LiteralEquals(signal.DisplayReference, memberReference))
            .FirstOrDefault();
    }

    private static SignalDefinition CreateControlCompanion(
        Iec61850SignalDescriptor descriptor,
        Iec61850SignalDataSetMembership membership,
        string cdc,
        string memberReference,
        string dataObjectReference,
        string exactFeedbackReference)
    {
        var report = descriptor.ReportMemberships.FirstOrDefault();
        return new SignalDefinition
        {
            Name = FirstNonEmpty(descriptor.DataObject, dataObjectReference),
            ObjectReference = dataObjectReference,
            DisplayReference = memberReference,
            FunctionalConstraint = "CO",
            DataType = cdc,
            Category = "Control",
            Confidence = "High",
            DataSetReference = membership.DataSetReference,
            ReportControlReference = report?.ReportControlReference ?? string.Empty,
            QualityReference = descriptor.QualityReference,
            TimestampReference = descriptor.TimestampReference,
            Source = "ARIEC61850 static DataSet • exact CDC/DataObject control companion",
            IsControlSignal = true,
            ControlCdc = cdc,
            ControlStatusReference = exactFeedbackReference,
            IsSelected = false,
            IsReportCapable = true,
            ReportCoverage = "Static DataSet control companion",
            ReportCoverageReason =
                $"Exact SCL/ARIEC DataSet member {memberReference} ({membership.DataSetReference}[{membership.MemberIndex}]) " +
                $"has controllable CDC={cdc} and exact command DataObject {dataObjectReference}. Actual command actions remain disabled until live ctlModel inspection proves Direct/SBO operation.",
            ProbeStatus = "Control model pending",
            Value = "-",
            Quality = "Unknown",
            DeviceTimestamp = "-"
        };
    }

    private static void LinkControlToMembership(
        SignalDefinition control,
        Iec61850SignalDescriptor descriptor,
        Iec61850SignalDataSetMembership membership,
        string cdc,
        string memberReference,
        string exactFeedbackReference)
    {
        control.DisplayReference = memberReference;
        control.DataSetReference = membership.DataSetReference;
        control.IsReportCapable = true;
        control.ControlCdc = cdc;
        if (!string.IsNullOrWhiteSpace(exactFeedbackReference))
            control.ControlStatusReference = exactFeedbackReference;

        var report = descriptor.ReportMemberships.FirstOrDefault();
        if (report is not null && string.IsNullOrWhiteSpace(control.ReportControlReference))
            control.ReportControlReference = report.ReportControlReference;

        if (string.IsNullOrWhiteSpace(control.QualityReference))
            control.QualityReference = descriptor.QualityReference;
        if (string.IsNullOrWhiteSpace(control.TimestampReference))
            control.TimestampReference = descriptor.TimestampReference;
    }

    private static SignalDefinition CreateRuntimeFeedback(
        Iec61850SignalDescriptor descriptor,
        Iec61850SignalDataSetMembership membership,
        string cdc,
        string memberReference,
        string exactFeedbackReference)
    {
        var report = descriptor.ReportMemberships.FirstOrDefault();
        var fc = FirstNonEmpty(descriptor.FunctionalConstraint, membership.FunctionalConstraint).ToUpperInvariant();
        var category = cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase)
            ? "Position"
            : fc.Equals("MX", StringComparison.OrdinalIgnoreCase)
                ? "Measurement"
                : "Status";

        return new SignalDefinition
        {
            Name = FirstNonEmpty(descriptor.DataObject, descriptor.DataAttributePath, memberReference),
            ObjectReference = exactFeedbackReference,
            DisplayReference = memberReference,
            FunctionalConstraint = fc,
            DataType = FirstNonEmpty(descriptor.MmsType, descriptor.SclBType, "Unknown"),
            Category = category,
            Confidence = "High",
            DataSetReference = membership.DataSetReference,
            ReportControlReference = report?.ReportControlReference ?? string.Empty,
            QualityReference = descriptor.QualityReference,
            TimestampReference = descriptor.TimestampReference,
            Source = "ARIEC61850 static DataSet • exact control feedback projection",
            IsSelected = false,
            IsReportCapable = true,
            ReportCoverage = report is null
                ? "Static DataSet control feedback"
                : "Static report/DataSet control feedback",
            ReportCoverageReason =
                $"Exact SCL/ARIEC control member {memberReference} ({membership.DataSetReference}[{membership.MemberIndex}]) " +
                $"resolves to {fc} primary feedback {exactFeedbackReference}; command and process facets remain separate.",
            ProbeStatus = "Not probed",
            Value = "-",
            Quality = "Unknown",
            DeviceTimestamp = "-"
        };
    }

    private static Iec61850StaticControlStatusProjectionResult EmptyResult()
        => new(Array.Empty<SignalDefinition>(), 0);

    private static bool LiteralEquals(string? left, string? right)
        => string.Equals(
            NormalizeReference(left),
            NormalizeReference(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('\\', '/').Replace('$', '.');

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
