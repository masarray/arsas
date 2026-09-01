using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// ARSAS FAT v2 consumer adapter over ARIEC-owned static DataSet authority.
///
/// This service does not parse SCL, infer FCDA semantics, or deduplicate by runtime object
/// reference. ARIEC remains the authority for mandatory DataSet descriptors; ARSAS creates
/// exactly one FAT row per static DataSet membership.
/// </summary>
public static class FatDataSetSignalProjectionService
{
    public static IReadOnlyList<FatVerificationSignal> Project(SclIedWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(workspace.DesignModel);
        return Project(workspace.IedName, workspace.AccessPointName, workspace.DesignModel);
    }

    public static IReadOnlyList<FatVerificationSignal> Project(
        string iedName,
        string accessPointName,
        LiveIedModelDiscoveryDocument model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iedName);
        ArgumentNullException.ThrowIfNull(model);

        var descriptors = Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(model);
        var rows = descriptors
            .SelectMany(descriptor => descriptor.DataSetMemberships.Select(membership =>
                CreateRow(iedName.Trim(), accessPointName?.Trim() ?? string.Empty, descriptor, membership)))
            .OrderBy(row => row.DataSetReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.DataSetMemberIndex)
            .ThenBy(row => row.StaticMemberReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var staticMemberCount = model.DataSets.Sum(dataSet => dataSet.Members.Count);
        if (rows.Length != staticMemberCount)
        {
            throw new InvalidDataException(
                $"ARIEC static DataSet projection returned {rows.Length} FAT membership row(s), " +
                $"but the SCL design model contains {staticMemberCount}. FAT import refuses to silently drop or invent DataSet members.");
        }

        var duplicateIdentity = rows
            .GroupBy(row => row.SignalId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIdentity is not null)
        {
            throw new InvalidDataException(
                $"Static DataSet membership identity is not unique for FAT signal '{duplicateIdentity.Key}'.");
        }

        return rows;
    }

    private static FatVerificationSignal CreateRow(
        string iedName,
        string accessPointName,
        Iec61850SignalDescriptor descriptor,
        Iec61850SignalDataSetMembership membership)
    {
        var staticReference = FirstNonEmpty(
            membership.CanonicalMemberReference,
            membership.OriginalMemberReference,
            descriptor.DesignReference,
            descriptor.ObservedReference,
            descriptor.PrimaryValueReference);
        var runtimeReference = FirstNonEmpty(
            descriptor.PrimaryValueReference,
            descriptor.DesignReference,
            descriptor.ObservedReference,
            staticReference);
        var dataType = FirstNonEmpty(descriptor.MmsType, descriptor.SclBType, "Unknown");
        var signalKind = Classify(descriptor.FunctionalConstraint, dataType);

        return new FatVerificationSignal
        {
            SignalId = BuildSignalId(
                iedName,
                accessPointName,
                membership.DataSetReference,
                membership.MemberIndex,
                staticReference),
            IedName = iedName,
            AccessPointName = accessPointName,
            DataSetReference = membership.DataSetReference,
            DataSetMemberIndex = membership.MemberIndex,
            StaticMemberReference = staticReference,
            RuntimeReference = runtimeReference,
            SignalName = FirstNonEmpty(descriptor.DataObject, descriptor.DataAttributePath, staticReference),
            FunctionalConstraint = descriptor.FunctionalConstraint,
            DataType = dataType,
            SignalKind = signalKind,
            CaptureMode = signalKind == FatSignalKind.Discrete
                ? FatCaptureMode.AutomaticTransition
                : FatCaptureMode.OperatorSnapshot
        };
    }

    internal static FatSignalKind Classify(string? functionalConstraint, string? dataType)
    {
        // Functional constraint describes service/semantic grouping, not the value domain.
        // In particular MX may legally carry enumerated or structured values. Treating every
        // MX member as numeric would silently opt such members into analog semantics. Capture
        // policy is therefore conservative and driven by the ARIEC-resolved basic/MMS type.
        _ = functionalConstraint;
        var type = (dataType ?? string.Empty).Trim().ToUpperInvariant();

        if (type.Contains("FLOAT", StringComparison.Ordinal) ||
            type.Contains("DOUBLE", StringComparison.Ordinal) ||
            type.Contains("REAL", StringComparison.Ordinal) ||
            type.Contains("ANALOG", StringComparison.Ordinal))
        {
            return FatSignalKind.Analog;
        }

        if (type.Contains("BOOLEAN", StringComparison.Ordinal) ||
            type.Contains("BOOL", StringComparison.Ordinal) ||
            type.Contains("DBPOS", StringComparison.Ordinal) ||
            type.Contains("DOUBLE-POINT", StringComparison.Ordinal) ||
            type.Contains("DOUBLE POINT", StringComparison.Ordinal) ||
            type.Contains("SINGLE-POINT", StringComparison.Ordinal) ||
            type.Contains("SINGLE POINT", StringComparison.Ordinal))
        {
            return FatSignalKind.Discrete;
        }

        // Integer/enumerated/bit-string/structured values remain required FAT rows, but use
        // operator snapshot capture until a stronger two-state/numeric semantic is proven.
        return FatSignalKind.Other;
    }

    private static string BuildSignalId(
        string iedName,
        string accessPointName,
        string dataSetReference,
        int memberIndex,
        string staticReference)
    {
        var identity = string.Join("|", new[]
        {
            iedName.Trim(),
            accessPointName.Trim(),
            dataSetReference.Trim(),
            memberIndex.ToString(CultureInfo.InvariantCulture),
            staticReference.Trim()
        });
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return "fat-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
