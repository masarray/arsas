using System.Xml.Linq;
using ArCanonical = AR.Iec61850.Engineering.Canonical;
using ArSclEngineering = AR.Iec61850.Scl.Engineering;
using ArMms = AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Thin application boundary for the source-neutral ARIEC canonical model.
///
/// ARSAS supplies user intent and presentation state. IEC 61850 DataSet/RCB semantics,
/// source provenance, live reconciliation and availability decisions remain engine-owned.
/// </summary>
public sealed partial class NativeIec61850Client
{
    private ArCanonical.CanonicalIedModel? _canonicalRuntimeModel;

    internal ArCanonical.CanonicalIedModel? CanonicalRuntimeModel => _canonicalRuntimeModel;

    private void ResetCanonicalRuntimeModel()
        => _canonicalRuntimeModel = null;

    private bool TryInstallCanonicalSclRuntimeModel(
        string sclXml,
        string iedName,
        string accessPointName,
        out string error,
        out IReadOnlyList<string> warnings)
    {
        error = string.Empty;
        warnings = Array.Empty<string>();

        try
        {
            var document = XDocument.Parse(
                sclXml,
                LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            var imported = ArSclEngineering.SclCanonicalImporter.Import(
                document,
                new ArSclEngineering.SclCanonicalImportOptions
                {
                    IedName = iedName,
                    AccessPointName = accessPointName,
                    SourceName = "ARSAS Open SCL"
                });

            warnings = imported.Warnings;
            if (!imported.IsSuccess || imported.Model is null)
            {
                error = imported.Errors.Length > 0
                    ? string.Join(" | ", imported.Errors)
                    : $"Canonical SCL import failed with status {imported.Status}.";
                return false;
            }

            _canonicalRuntimeModel = imported.Model;
            return true;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            error = $"Canonical SCL import failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private ArCanonical.CanonicalIedModel? ResolveCanonicalRuntimeModel(
        Iec61850MonitorDevice device,
        out string error)
    {
        error = string.Empty;

        if (device.SclWorkspace?.DesignModel is not null)
        {
            if (_canonicalRuntimeModel is not null &&
                _canonicalRuntimeModel.Source.Ingress == ArCanonical.CanonicalIngressKind.SclFile)
            {
                return _canonicalRuntimeModel;
            }

            error =
                "Open SCL is active, but the source-neutral canonical SCL model is unavailable. " +
                "Static reporting refused to rebuild protocol semantics from UI/live-model state.";
            return null;
        }

        if (device.LiveDiscoveryModel is not null)
        {
            _canonicalRuntimeModel = ArCanonical.CanonicalLiveModelAdapter.FromLiveDiscovery(
                device.LiveDiscoveryModel);
            return _canonicalRuntimeModel;
        }

        error = "No canonical SCL or live-discovery model is available for static acquisition.";
        return null;
    }

    private static ArCanonical.CanonicalStaticReportSelection[] BuildCanonicalStaticSelections(
        IEnumerable<Iec61850MonitorPoint> points)
        => points
            .Where(point => !string.IsNullOrWhiteSpace(point.IecReference))
            .GroupBy(
                point => $"{point.IecReference.Trim()}|{point.FunctionalConstraint.Trim()}",
                StringComparer.Ordinal)
            .Select(group =>
            {
                var point = group.First();
                return new ArCanonical.CanonicalStaticReportSelection
                {
                    Reference = point.IecReference.Trim(),
                    FunctionalConstraint = point.FunctionalConstraint.Trim()
                };
            })
            .ToArray();

    private static ArMms.MmsReportControlCandidate CandidateFromAvailability(
        ArMms.MmsRcbAvailabilitySnapshot snapshot)
        => new()
        {
            Domain = snapshot.Domain,
            LogicalNode = snapshot.LogicalNode,
            FunctionalConstraint = snapshot.Buffered ? "BR" : "RP",
            Name = snapshot.Name,
            Reference = snapshot.Reference,
            Buffered = snapshot.Buffered,
            DataSetReference = snapshot.DataSetReference,
            DataSetProbeState = snapshot.DataSetProbeState,
            DataSetProbeMessage = snapshot.DataSetProbeMessage,
            ReportId = snapshot.ReportId,
            ConfRev = snapshot.ConfRev,
            IntegrityPeriodMs = snapshot.IntegrityPeriodMs,
            EnabledState = snapshot.EnabledState,
            ReservationState = snapshot.ReservationState,
            ReservationTimeSeconds = snapshot.ReservationTimeSeconds,
            Owner = snapshot.Owner,
            BufferTimeMs = snapshot.BufferTimeMs,
            TriggerOptions = snapshot.TriggerOptions,
            OptionalFields = snapshot.OptionalFields,
            Status = $"EngineAvailability:{snapshot.Availability}",
            Attributes = snapshot.Attributes.ToList()
        };

    private static ArMms.MmsDataSetDirectoryResult DirectoryFromAvailability(
        ArMms.MmsRcbAvailabilitySnapshot snapshot)
        => new()
        {
            IsSuccess = snapshot.DataSetDirectorySuccess && snapshot.DataSetMembers.Count > 0,
            DataSetReference = snapshot.DataSetReference,
            Domain = snapshot.Domain,
            DataSetMmsName = string.Empty,
            IsDeletable = snapshot.DataSetIsDeletable ?? false,
            Members = snapshot.DataSetMembers,
            Message =
                $"Engine targeted availability evidence: {snapshot.Reference} -> {snapshot.DataSetReference}, " +
                $"members={snapshot.DataSetMembers.Count}, confidence={snapshot.Confidence}."
        };
}
