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
        var layout = BuildLayout(project, generatedAt, draft: false);
        return IoFatNativePdfWriter.Build(layout, project);
    }

    internal static IoFatReportLayoutPlan BuildLayout(
        IoTestProject project,
        DateTimeOffset? generatedAt = null,
        bool draft = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        var layout = IoFatExecutiveReportLayoutEngine.Build(project, generatedAt ?? DateTimeOffset.Now, draft);
        return IoFatSupplementalReportLayoutDecorator.AppendFileServiceEvidence(project, layout);
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