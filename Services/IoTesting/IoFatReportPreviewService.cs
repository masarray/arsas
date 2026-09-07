// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Creates point-in-time report scopes from the shared FAT project. A single IED is the
/// canonical evidence artifact, while any operator-selected set of IEDs can be composed
/// into one combined report without changing or sharing the underlying live per-IED state.
/// </summary>
public static class IoFatReportPreviewService
{
    public static IoTestProject CreateIedScopedProject(IoTestProject project, IoTestIedPlan ied)
        => IoFatReportScope.CreateForIed(project, ied);

    public static IoTestProject CreateScopedProject(
        IoTestProject project,
        IEnumerable<IoTestIedPlan> ieds)
        => IoFatReportScope.CreateForIeds(project, ieds);
}
