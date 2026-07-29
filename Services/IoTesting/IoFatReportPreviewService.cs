// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Creates an immutable report scope for the selected workbook IED. Native PDF and
/// FixedDocument preview then consume the same shared layout engine.
/// </summary>
public static class IoFatReportPreviewService
{
    public static IoTestProject CreateIedScopedProject(IoTestProject project, IoTestIedPlan ied)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ied);
        if (!project.Ieds.Contains(ied))
            throw new ArgumentException("The selected IED does not belong to this IO FAT project.", nameof(ied));

        return new IoTestProject
        {
            ProjectId = project.ProjectId,
            SchemaVersion = project.SchemaVersion,
            ProjectName = project.ProjectName,
            SourceWorkbookName = project.SourceWorkbookName,
            SourceWorkbookSha256 = project.SourceWorkbookSha256,
            ImportedAt = project.ImportedAt,
            Ieds = new List<IoTestIedPlan> { ied }
        };
    }
}
