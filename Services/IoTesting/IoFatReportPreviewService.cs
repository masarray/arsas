// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Creates immutable report scopes from the shared FAT project. A single IED is the
/// canonical evidence artifact, while any operator-selected set of IEDs can be composed
/// into one combined report without changing the underlying per-IED evidence journals.
/// </summary>
public static class IoFatReportPreviewService
{
    public static IoTestProject CreateIedScopedProject(IoTestProject project, IoTestIedPlan ied)
        => CreateScopedProject(project, new[] { ied });

    public static IoTestProject CreateScopedProject(
        IoTestProject project,
        IEnumerable<IoTestIedPlan> ieds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ieds);

        var selected = ieds
            .Where(ied => ied != null)
            .Distinct(ReferenceEqualityComparer.Instance)
            .ToArray();
        if (selected.Length == 0)
            throw new ArgumentException("Select at least one IED for the FAT report scope.", nameof(ieds));
        if (selected.Any(ied => !project.Ieds.Contains(ied)))
            throw new ArgumentException("Every selected IED must belong to this IO FAT project.", nameof(ieds));

        return new IoTestProject
        {
            ProjectId = project.ProjectId,
            SchemaVersion = project.SchemaVersion,
            ProjectName = project.ProjectName,
            SourceWorkbookName = project.SourceWorkbookName,
            SourceWorkbookSha256 = project.SourceWorkbookSha256,
            ImportedAt = project.ImportedAt,
            DocumentControl = project.DocumentControl,
            Ieds = selected.ToList()
        };
    }
}
