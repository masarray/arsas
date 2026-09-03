// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Creates an immutable report projection from the shared Engineering/FAT workspace.
/// FAT disposition is a hard report boundary: a row removed by the operator must never
/// leak back into PDF/print-preview output merely because old evidence still exists.
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
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ied);
        if (!project.Ieds.Contains(ied))
            throw new ArgumentException("The selected IED does not belong to this IO FAT project.", nameof(ied));

        return CreateCore(project, new[] { ied });
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
        scoped.SetSources(project.Sources, project.SourceSetSha256);
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
            TestPoints = ied.TestPoints.Where(Includes).ToList(),
            LatestComtradeFiles = ied.LatestComtradeFiles,
            LatestComtradeRemotePath = ied.LatestComtradeRemotePath,
            LatestComtradeCompleteness = ied.LatestComtradeCompleteness,
            LatestComtradeAcquisitionSource = ied.LatestComtradeAcquisitionSource,
            LatestComtradeModifiedAtUtc = ied.LatestComtradeModifiedAtUtc,
            LatestComtradeCapturedAtUtc = ied.LatestComtradeCapturedAtUtc,
            LatestComtradeFileCount = ied.LatestComtradeFileCount,
            LatestComtradeKnownSizeBytes = ied.LatestComtradeKnownSizeBytes
        };
}
