// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Public native PDF contract for ARSAS FAT evidence.
/// PDF output and the WPF print preview consume one shared report layout plan.
/// Legacy workbook projects retain their reviewed executive layout; SCL-backed FAT v2
/// uses the generic Value 1 / Value 2 layout.
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

        // FAT disposition and shared Engineering/FAT workspace membership are applied
        // before either report engine. This is a hard boundary: Remove from FAT excludes
        // the row even when it already owns completed historical evidence.
        var reportProject = IoFatReportScope.Create(project);
        var created = generatedAt ?? DateTimeOffset.Now;
        var isSclFat = reportProject.SchemaVersion.StartsWith("ARSAS-FAT-SCL-", StringComparison.OrdinalIgnoreCase);
        var layout = isSclFat
            ? IoFatV2ReportLayoutEngine.Build(reportProject, created, draft)
            : IoFatExecutiveReportLayoutEngine.Build(reportProject, created, draft);

        // Presentation polish is deliberately downstream of the evidence engine. It may
        // improve labels and typography, but it cannot alter scope, values, verdicts,
        // relay timestamps or source identities.
        if (isSclFat)
            layout = IoFatReportProfessionalPolish.Apply(reportProject, layout);

        return IoFatSupplementalReportLayoutDecorator.AppendFileServiceEvidence(reportProject, layout);
    }

    /// <summary>
    /// P4D native FAT export. The immutable selected-IED snapshot has already been mapped
    /// to one report layout plan, so both DocumentViewer and PDF serialize that same plan.
    /// No IoTestProject/runtime workspace is rebuilt for export.
    /// </summary>
    internal static byte[] GenerateLayout(
        IoFatReportLayoutPlan layout,
        string reportName,
        string primaryReference)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return IoFatNativePdfWriter.Build(layout, reportName, primaryReference);
    }

    public static void Save(string fileName, IoTestProject project, DateTimeOffset? generatedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var bytes = Generate(project, generatedAt);
        SaveBytesAtomic(fileName, bytes);
    }

    internal static void SaveLayout(
        string fileName,
        IoFatReportLayoutPlan layout,
        string reportName,
        string primaryReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var bytes = GenerateLayout(layout, reportName, primaryReference);
        SaveBytesAtomic(fileName, bytes);
    }

    private static void SaveBytesAtomic(string fileName, byte[] bytes)
    {
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
