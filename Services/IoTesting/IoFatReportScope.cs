// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Creates immutable report projections from the shared Engineering/FAT workspace.
/// FAT disposition is a hard report boundary: a row removed by the operator must never
/// leak back into PDF/print-preview output merely because old evidence still exists.
///
/// Phase C also makes the projection a true point-in-time artifact. No report point or
/// runtime object is shared with the live project, so a sibling IED that keeps running,
/// a later recapture, or an operator scope change cannot mutate an already-created report.
/// </summary>
internal static class IoFatReportScope
{
    public static IoTestProject Create(IoTestProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return CreateCore(project, project.Ieds);
    }

    public static IoTestProject CreateForIed(IoTestProject project, IoTestIedPlan ied)
    {
        ArgumentNullException.ThrowIfNull(ied);
        return CreateForIeds(project, new[] { ied });
    }

    public static IoTestProject CreateForIeds(
        IoTestProject project,
        IEnumerable<IoTestIedPlan> ieds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ieds);

        var selected = ieds
            .Where(static ied => ied is not null)
            .Distinct()
            .ToArray();
        if (selected.Length == 0)
            throw new ArgumentException("Select at least one IED for the FAT report scope.", nameof(ieds));
        if (selected.Any(ied => !project.Ieds.Contains(ied)))
            throw new ArgumentException("Every selected IED must belong to this IO FAT project.", nameof(ieds));

        // The Phase-B identity contract is Technical Key + IP. Report ownership is
        // ambiguous if the project itself contains more than one IED with a selected
        // identity, even when the caller happens to pass only one of those instances.
        var projectIdentityCounts = project.Ieds
            .GroupBy(IoTestPerIedProgressIdentity.IedKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var duplicateIdentity = selected
            .Select(IoTestPerIedProgressIdentity.IedKey)
            .FirstOrDefault(key => projectIdentityCounts.TryGetValue(key, out var count) && count != 1);
        if (!string.IsNullOrWhiteSpace(duplicateIdentity))
        {
            throw new InvalidDataException(
                $"The FAT project contains duplicate IED identity '{duplicateIdentity}'. Resolve the Technical Key/IP conflict before reporting.");
        }

        return CreateCore(project, selected);
    }

    public static bool Includes(IoTestPointPlan point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return point.WorkspaceSelected && point.IsIncludedInFat;
    }

    private static IoTestProject CreateCore(
        IoTestProject project,
        IEnumerable<IoTestIedPlan> ieds)
    {
        var scoped = new IoTestProject
        {
            ProjectId = project.ProjectId,
            SchemaVersion = project.SchemaVersion,
            ProjectName = project.ProjectName,
            SourceWorkbookName = project.SourceWorkbookName,
            SourceWorkbookSha256 = project.SourceWorkbookSha256,
            ImportedAt = project.ImportedAt,
            DocumentControl = project.DocumentControl,
            Ieds = ieds.Select(CloneIed).ToList()
        };
        scoped.SetSources(project.Sources.ToArray(), project.SourceSetSha256);
        scoped.InitializeRuntimeNotifications();
        return scoped;
    }

    private static IoTestIedPlan CloneIed(IoTestIedPlan ied)
        => new()
        {
            IedName = ied.IedName,
            IpAddress = ied.IpAddress,
            IedRole = ied.IedRole,
            Location = ied.Location,
            VoltageLevel = ied.VoltageLevel,
            Switchgear = ied.Switchgear,
            TestPoints = ied.TestPoints.Where(Includes).Select(ClonePoint).ToList(),
            LatestComtradeFiles = ied.LatestComtradeFiles,
            LatestComtradeRemotePath = ied.LatestComtradeRemotePath,
            LatestComtradeCompleteness = ied.LatestComtradeCompleteness,
            LatestComtradeAcquisitionSource = ied.LatestComtradeAcquisitionSource,
            LatestComtradeModifiedAtUtc = ied.LatestComtradeModifiedAtUtc,
            LatestComtradeCapturedAtUtc = ied.LatestComtradeCapturedAtUtc,
            LatestComtradeFileCount = ied.LatestComtradeFileCount,
            LatestComtradeKnownSizeBytes = ied.LatestComtradeKnownSizeBytes
        };

    private static IoTestPointPlan ClonePoint(IoTestPointPlan source)
    {
        var clone = new IoTestPointPlan
        {
            TestPointId = source.TestPointId,
            IedName = source.IedName,
            IpAddress = source.IpAddress,
            SignalName = source.SignalName,
            ObjectReference = source.ObjectReference,
            FunctionalConstraint = source.FunctionalConstraint,
            ExpectedOnText = source.ExpectedOnText,
            ExpectedOffText = source.ExpectedOffText,
            ExpectedOnRaw = source.ExpectedOnRaw,
            ExpectedOffRaw = source.ExpectedOffRaw,
            DataType = source.DataType,
            SignalAddress = source.SignalAddress,
            DataSetName = source.DataSetName,
            LogicalDevice = source.LogicalDevice,
            LogicalNode = source.LogicalNode,
            DataObject = source.DataObject,
            DataAttribute = source.DataAttribute,
            Cdc = source.Cdc,
            SourceIecReference = source.SourceIecReference,
            ReportDisplayReference = source.ReportDisplayReference,
            EventLogSearchReference = source.EventLogSearchReference,
            EvidenceExpected = source.EvidenceExpected,
            MappingQuality = source.MappingQuality,
            ReviewStatus = source.ReviewStatus,
            ReviewReason = source.ReviewReason,
            EventLogMatch = source.EventLogMatch,
            EvidenceReference = source.EvidenceReference,
            ReviewerComment = source.ReviewerComment,
            SourceSheet = source.SourceSheet,
            SourceRow = source.SourceRow,
            SignalKind = source.SignalKind,
            CaptureMode = source.CaptureMode,
            WorkspaceSelected = source.WorkspaceSelected,
            TestEnabled = source.TestEnabled,
            ImportReady = source.ImportReady,
            BindingStatus = source.BindingStatus,
            BindingEvidence = source.BindingEvidence
        };
        clone.RestoreFatDisposition(source.FatDisposition);
        CloneRuntime(source.Runtime, clone.Runtime);
        return clone;
    }

    private static void CloneRuntime(IoTestPointRuntime source, IoTestPointRuntime target)
    {
        target.State = source.State;
        target.LastObservedState = source.LastObservedState;
        target.LastSequence = source.LastSequence;
        target.ConnectionGeneration = source.ConnectionGeneration;
        target.OnEvidence = source.OnEvidence;
        target.OffEvidence = source.OffEvidence;
        target.Value1Evidence = source.Value1Evidence;
        target.Value2Evidence = source.Value2Evidence;
        target.AutoCaptureStage = source.AutoCaptureStage;
        target.StatusReason = source.StatusReason;
        target.Attempt = source.Attempt;
        target.CurrentValue = source.CurrentValue;
        target.CurrentQuality = source.CurrentQuality;
        target.CurrentSource = source.CurrentSource;
        target.CurrentIedTimestamp = source.CurrentIedTimestamp;
    }
}
