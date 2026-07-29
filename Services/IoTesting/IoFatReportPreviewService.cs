// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Builds the in-workspace per-IED report preview. The preview follows the same
/// NavigateToString/WebBrowser pattern used by the project-owned ARIEC60870 report
/// preview while official PDF export remains the native PDF 1.4 writer.
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

    public static string BuildHtml(
        IoTestProject project,
        IoTestIedPlan ied,
        bool draft,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ied);
        var created = generatedAt ?? DateTimeOffset.Now;
        var enabled = ied.TestPoints.Where(point => point.TestEnabled).ToList();
        var passed = enabled.Count(point => point.Runtime.State == IoTestPointState.Passed);
        var review = enabled.Count(point => point.Runtime.State == IoTestPointState.Review);
        var failed = enabled.Count(point => point.Runtime.State == IoTestPointState.Failed);
        var pending = Math.Max(0, enabled.Count - passed - review - failed);

        var html = new StringBuilder(24_000);
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"/>");
        html.AppendLine($"<title>{E(ied.IedName)} - ARSAS IO FAT Preview</title>");
        html.AppendLine("<style>");
        html.AppendLine("@page{size:A4 landscape;margin:10mm}*{box-sizing:border-box}body{margin:0;background:#e9eef5;color:#172033;font-family:'Segoe UI',Arial,sans-serif;font-size:11px}.sheet{width:1120px;min-height:760px;margin:22px auto;background:#fff;box-shadow:0 10px 32px rgba(31,45,71,.16);padding:30px 34px}.top{display:flex;justify-content:space-between;gap:24px;border-bottom:1px solid #dce5f0;padding-bottom:18px}.eyebrow{font-size:10px;font-weight:700;letter-spacing:.12em;color:#2f67d8}.title{font-size:25px;font-weight:700;margin-top:6px;color:#101827}.sub{margin-top:7px;color:#627086}.mark{min-width:170px;border:1px solid #ccd9eb;border-radius:12px;padding:12px 14px;text-align:right}.draft{color:#a85d00;background:#fff7e6;border-color:#eccb82}.final{color:#157347;background:#edf9f3;border-color:#9bd2b5}.mark b{display:block;font-size:17px}.meta{display:grid;grid-template-columns:1.3fr 1fr 1fr 1fr;margin:18px 0;border:1px solid #dfe7f1;border-radius:12px;overflow:hidden}.meta div{padding:11px 13px;border-right:1px solid #e6edf5}.meta div:last-child{border:0}.label{font-size:9px;font-weight:700;letter-spacing:.08em;color:#718096}.value{margin-top:4px;font-weight:600;color:#182235}.metrics{display:grid;grid-template-columns:repeat(5,1fr);gap:9px;margin-bottom:18px}.metric{border:1px solid #e1e8f1;border-radius:10px;padding:10px 12px}.metric span{font-size:9px;color:#728095;font-weight:700}.metric b{display:block;font-size:18px;margin-top:4px}.pass{color:#148052}.review{color:#9a6500}.fail{color:#c33b46}.pending{color:#637187}table{width:100%;border-collapse:collapse;table-layout:fixed}th{background:#f3f6fa;color:#596980;text-align:left;font-size:9px;letter-spacing:.04em;padding:9px 7px;border-bottom:1px solid #dbe4ef}td{padding:9px 7px;border-bottom:1px solid #e8edf4;vertical-align:top;overflow-wrap:anywhere}.mono{font-family:Consolas,'Courier New',monospace;font-size:9.5px;color:#46566d}.muted{color:#6f7d91}.result{font-weight:700}.footer{margin-top:18px;padding-top:10px;border-top:1px solid #e0e7f0;color:#69778b;font-size:9px;display:flex;justify-content:space-between}@media print{body{background:#fff}.sheet{width:auto;min-height:0;margin:0;box-shadow:none;padding:0}}");
        html.AppendLine("</style></head><body><main class=\"sheet\">");
        html.AppendLine("<section class=\"top\"><div>");
        html.AppendLine("<div class=\"eyebrow\">ARSAS · IEC 61850 IO LIST FAT</div>");
        html.AppendLine($"<div class=\"title\">{E(ied.IedName)} evidence report</div>");
        html.AppendLine($"<div class=\"sub\">{E(project.ProjectName)} · relay-timestamped OFF → ON → OFF verification</div></div>");
        html.AppendLine(draft
            ? "<div class=\"mark draft\"><span class=\"label\">PREVIEW STATUS</span><b>DRAFT / LIVE</b><span>Stop the active session before issuing the final PDF.</span></div>"
            : "<div class=\"mark final\"><span class=\"label\">PREVIEW STATUS</span><b>SEALED VIEW</b><span>Ready for per-IED PDF export.</span></div>");
        html.AppendLine("</section>");

        html.AppendLine("<section class=\"meta\">");
        Meta(html, "IED", ied.IedName);
        Meta(html, "IP ADDRESS", ied.IpAddress);
        Meta(html, "PROJECT ID", project.ProjectId);
        Meta(html, "GENERATED", created.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        html.AppendLine("</section>");

        html.AppendLine("<section class=\"metrics\">");
        Metric(html, "TEST POINTS", enabled.Count, string.Empty);
        Metric(html, "PASS", passed, "pass");
        Metric(html, "REVIEW", review, "review");
        Metric(html, "FAILED", failed, "fail");
        Metric(html, "PENDING", pending, "pending");
        html.AppendLine("</section>");

        html.AppendLine("<table><colgroup><col style=\"width:15%\"><col style=\"width:22%\"><col style=\"width:8%\"><col style=\"width:10%\"><col style=\"width:13%\"><col style=\"width:13%\"><col style=\"width:8%\"><col style=\"width:11%\"></colgroup>");
        html.AppendLine("<thead><tr><th>SIGNAL</th><th>IEC 61850 REFERENCE</th><th>VALUE</th><th>ACQUISITION</th><th>ON · RELAY TIME</th><th>OFF · RELAY TIME</th><th>RESULT</th><th>STATUS / REASON</th></tr></thead><tbody>");
        foreach (var point in enabled)
        {
            var result = Result(point.Runtime.State);
            html.AppendLine("<tr>");
            Cell(html, point.SignalName);
            Cell(html, point.ObjectReference, "mono");
            Cell(html, $"{point.Runtime.CurrentValue} · {point.Runtime.CurrentQuality}");
            Cell(html, point.Runtime.CurrentSource);
            Cell(html, point.Runtime.OnRelayTimestampText, "mono");
            Cell(html, point.Runtime.OffRelayTimestampText, "mono");
            Cell(html, result.Text, $"result {result.Css}");
            Cell(html, $"{point.Runtime.StateText} · {point.Runtime.StatusReason}", "muted");
            html.AppendLine("</tr>");
        }
        if (enabled.Count == 0)
            html.AppendLine("<tr><td colspan=\"8\" class=\"muted\">No enabled IO-list test points are available for this IED.</td></tr>");
        html.AppendLine("</tbody></table>");
        html.AppendLine($"<footer class=\"footer\"><span>Workbook: {E(project.SourceWorkbookName)} · SHA-256 {E(ShortHash(project.SourceWorkbookSha256))}</span><span>ARSAS per-IED print preview</span></footer>");
        html.AppendLine("</main></body></html>");
        return html.ToString();
    }

    private static void Meta(StringBuilder html, string label, string value)
        => html.AppendLine($"<div><span class=\"label\">{E(label)}</span><div class=\"value\">{E(value)}</div></div>");

    private static void Metric(StringBuilder html, string label, int value, string css)
        => html.AppendLine($"<div class=\"metric\"><span>{E(label)}</span><b class=\"{css}\">{value.ToString(CultureInfo.InvariantCulture)}</b></div>");

    private static void Cell(StringBuilder html, string? value, string css = "")
        => html.AppendLine($"<td class=\"{css}\">{E(string.IsNullOrWhiteSpace(value) ? "—" : value)}</td>");

    private static (string Text, string Css) Result(IoTestPointState state) => state switch
    {
        IoTestPointState.Passed => ("✔ PASS", "pass"),
        IoTestPointState.Review => ("⚠ REVIEW", "review"),
        IoTestPointState.Failed => ("✖ FAILED", "fail"),
        _ => ("—", "pending")
    };

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string ShortHash(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= 16 ? normalized : normalized[..16] + "…";
    }
}
