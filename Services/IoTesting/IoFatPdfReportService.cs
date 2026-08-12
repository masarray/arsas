// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Public native PDF contract for ARSAS IO List FAT evidence.
/// PDF output and the WPF print preview consume one shared report layout plan.
/// </summary>
public static class IoFatPdfReportService
{
    public static byte[] Generate(IoTestProject project, DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var layout = BuildLayout(project, generatedAt, draft: false, blankForm: IsBlankForm(project));
        return IoFatNativePdfWriter.Build(layout, project);
    }

    /// <summary>
    /// Returns true only while the enabled FAT scope has never entered a test attempt.
    /// A fresh import is therefore exported as a customer-review blank form; any
    /// baseline, transition, review, failure, or completed result becomes a test record.
    /// </summary>
    internal static bool IsBlankForm(IoTestProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var points = project.Ieds
            .SelectMany(ied => ied.TestPoints)
            .Where(point => point.TestEnabled || point.Runtime.IsComplete)
            .ToList();
        return points.Count > 0 && points.All(point =>
            point.Runtime.State == IoTestPointState.NotStarted &&
            point.Runtime.Attempt == 0 &&
            point.Runtime.OnEvidence == null &&
            point.Runtime.OffEvidence == null);
    }

    internal static IoFatReportLayoutPlan BuildLayout(
        IoTestProject project,
        DateTimeOffset? generatedAt = null,
        bool draft = false,
        bool? blankForm = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        return IoFatExecutiveReportLayoutEngine.Build(
            project,
            generatedAt ?? DateTimeOffset.Now,
            draft,
            blankForm ?? IsBlankForm(project));
    }

    public static void Save(string fileName, IoTestProject project, DateTimeOffset? generatedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var bytes = Generate(project, generatedAt);
        var fullPath = Path.GetFullPath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
