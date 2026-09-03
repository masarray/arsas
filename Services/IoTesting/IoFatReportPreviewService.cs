// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Creates the immutable selected-IED report projection used by WPF print preview.
/// Shared workspace membership and FAT disposition are applied before any layout engine,
/// so a right-click Remove from FAT cannot remain visible through older evidence state.
/// </summary>
public static class IoFatReportPreviewService
{
    public static IoTestProject CreateIedScopedProject(IoTestProject project, IoTestIedPlan ied)
        => IoFatReportScope.CreateForIed(project, ied);
}
