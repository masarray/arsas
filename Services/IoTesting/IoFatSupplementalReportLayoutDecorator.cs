using System.Globalization;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Appends controlled IED-level file-service evidence and a mandatory final FAT acceptance
/// sign-off page to the same layout consumed by native PDF output and WPF Print Preview.
/// Existing page totals are corrected after all supplemental pages are added.
/// </summary>
internal static class IoFatSupplementalReportLayoutDecorator
{
    private const double PageWidth = 842d;
    private const double PageHeight = 595d;
    private const double Margin = 30d;
    private const double ContentWidth = PageWidth - (Margin * 2d);
    private const int RowsPerPage = 6;

    private static readonly IoFatReportColor Navy = Color("0F172A");
    private static readonly IoFatReportColor Blue = Color("2563EB");
    private static readonly IoFatReportColor SoftBlue = Color("EFF6FF");
    private static readonly IoFatReportColor Border = Color("D9E4F0");
    private static readonly IoFatReportColor Muted = Color("64748B");
    private static readonly IoFatReportColor Ink = Color("1F2937");
    private static readonly IoFatReportColor White = Color("FFFFFF");
    private static readonly IoFatReportColor Pass = Color("15803D");
    private static readonly IoFatReportColor SoftPass = Color("F0FDF4");

    public static IoFatReportLayoutPlan AppendFileServiceEvidence(
        IoTestProject project,
        IoFatReportLayoutPlan baseLayout)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(baseLayout);

        var evidenceIeds = project.Ieds
            .Where(ied => ied.HasRemoteComtradeEvidence)
            .ToArray();
        var evidencePageCount = (int)Math.Ceiling(evidenceIeds.Length / (double)RowsPerPage);

        // Sign-off is always the final report page, even when there is no COMTRADE evidence.
        const int signOffPageCount = 1;
        var totalPages = baseLayout.Pages.Count + evidencePageCount + signOffPageCount;
        var pages = new List<IoFatReportPagePlan>(totalPages);

        for (var index = 0; index < baseLayout.Pages.Count; index++)
        {
            var page = baseLayout.Pages[index];
            var corrected = page.Commands
                .Select(command => CorrectPageTotal(command, index + 1, totalPages))
                .ToArray();
            pages.Add(new IoFatReportPagePlan(index + 1, page.Width, page.Height, corrected));
        }

        for (var pageIndex = 0; pageIndex < evidencePageCount; pageIndex++)
        {
            var rows = evidenceIeds
                .Skip(pageIndex * RowsPerPage)
                .Take(RowsPerPage)
                .ToArray();
            var pageNumber = baseLayout.Pages.Count + pageIndex + 1;
            pages.Add(BuildEvidencePage(project, rows, pageNumber, totalPages, baseLayout.CreatedAt, baseLayout.Draft));
        }

        var signOffPageNumber = totalPages;
        pages.Add(BuildSignOffPage(project, signOffPageNumber, totalPages, baseLayout.CreatedAt, baseLayout.Draft));

        return new IoFatReportLayoutPlan(
            baseLayout.ProjectId,
            baseLayout.CreatedAt,
            baseLayout.Draft,
            pages);
    }

    private static IoFatReportPagePlan BuildEvidencePage(
        IoTestProject project,
        IReadOnlyList<IoTestIedPlan> rows,
        int pageNumber,
        int totalPages,
        DateTimeOffset createdAt,
        bool draft)
    {
        var commands = new List<IoFatReportCommand>();
        var projectName = FirstNonEmpty(project.DocumentControl.ClientProject, project.ProjectName, project.ProjectId);
        var documentNumber = FirstNonEmpty(
            project.DocumentControl.PurchaserDocumentNumber,
            project.DocumentControl.CompanyProjectDocumentNumber,
            project.ProjectId);
        var revision = FirstNonEmpty(project.DocumentControl.Revision, "-");

        Line(commands, Margin, 496d, PageWidth - Margin, 496d, Border, 0.8d);
        Text(commands, Margin, 566d, 480d, projectName, IoFatReportFontKind.Bold, 7.2d, Muted);
        Text(commands, Margin, 544d, 520d, "IEC 61850 File Service / COMTRADE Evidence", IoFatReportFontKind.Bold, 16.8d, Navy);
        Text(commands, Margin, 524d, 520d,
            "Remote fault-record discovery evidence captured directly from each IED.",
            IoFatReportFontKind.Regular, 8.0d, Muted);

        Rect(commands, 590d, 568d, 222d, 64d, 4d, SoftBlue, Border, 0.7d);
        Text(commands, 601d, 554d, 200d, "DOCUMENT CONTROL", IoFatReportFontKind.Bold, 5.9d, Muted);
        Text(commands, 601d, 538d, 200d, documentNumber, IoFatReportFontKind.Bold, 8.2d, Navy);
        Text(commands, 601d, 523d, 200d, $"REV {revision}  |  {(draft ? "PREVIEW" : "AS TESTED")}", IoFatReportFontKind.Bold, 6.7d, Navy);
        Text(commands, 601d, 511d, 200d, draft ? "NOT FOR ISSUE" : "CUSTOMER FAT RECORD", IoFatReportFontKind.Regular, 5.8d, Muted);

        Rect(commands, Margin, 480d, ContentWidth, 42d, 5d, SoftPass, Border, 0.7d);
        Text(commands, Margin + 12d, 466d, 170d, "FILE SERVICE ACCEPTANCE BASIS", IoFatReportFontKind.Bold, 5.9d, Pass);
        Text(commands, Margin + 12d, 450d, ContentWidth - 24d,
            "PASS = the IED returned a supported COMTRADE/fault-record entry through IEC 61850 FileDirectory; remote file identity and relay-modified time are preserved as FAT evidence.",
            IoFatReportFontKind.Regular, 6.6d, Ink);

        var widths = new[] { 105d, 58d, 334d, 125d, 160d };
        var headers = new[] { "IED", "Result", "Latest remote COMTRADE file(s)", "Relay modified", "Evidence source" };
        var y = 426d;
        var x = Margin;
        for (var i = 0; i < headers.Length; i++)
        {
            Rect(commands, x, y, widths[i], 23d, 0d, SoftBlue, Border, 0.45d);
            Text(commands, x + 5d, y - 15d, widths[i] - 10d, headers[i], IoFatReportFontKind.Bold, 5.8d, Blue);
            x += widths[i];
        }
        y -= 23d;

        foreach (var ied in rows)
        {
            const double rowHeight = 55d;
            x = Margin;
            for (var i = 0; i < widths.Length; i++)
            {
                Rect(commands, x, y, widths[i], rowHeight, 0d, White, Border, 0.35d);
                x += widths[i];
            }

            Text(commands, Margin + 5d, y - 17d, widths[0] - 10d, Fit(ied.IedName, 25), IoFatReportFontKind.Bold, 6.5d, Ink);
            Text(commands, Margin + 5d, y - 33d, widths[0] - 10d, Fit(ied.IpAddress, 25), IoFatReportFontKind.Mono, 5.7d, Muted);

            var resultX = Margin + widths[0];
            Text(commands, resultX + 5d, y - 24d, widths[1] - 10d, "PASS", IoFatReportFontKind.Bold, 7.2d, Pass);

            var fileX = resultX + widths[1];
            var fileLines = Wrap(ied.LatestComtradeFiles, 60, 3);
            var fileY = y - 14d;
            foreach (var line in fileLines)
            {
                Text(commands, fileX + 5d, fileY, widths[2] - 10d, line, IoFatReportFontKind.Mono, 5.9d, Ink);
                fileY -= 10.5d;
            }
            if (!string.IsNullOrWhiteSpace(ied.LatestComtradeCompleteness))
                Text(commands, fileX + 5d, y - 48d, widths[2] - 10d, Fit(ied.LatestComtradeCompleteness, 78), IoFatReportFontKind.Regular, 5.4d, Muted);

            var modifiedX = fileX + widths[2];
            Text(commands, modifiedX + 5d, y - 21d, widths[3] - 10d,
                ied.LatestComtradeModifiedAtUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "not supplied",
                IoFatReportFontKind.Mono, 5.7d, Ink);
            Text(commands, modifiedX + 5d, y - 34d, widths[3] - 10d,
                ied.LatestComtradeModifiedAtUtc?.ToString("HH:mm:ss zzz", CultureInfo.InvariantCulture) ?? string.Empty,
                IoFatReportFontKind.Mono, 5.7d, Muted);

            var sourceX = modifiedX + widths[3];
            Text(commands, sourceX + 5d, y - 20d, widths[4] - 10d, "IEC 61850", IoFatReportFontKind.Bold, 5.9d, Ink);
            Text(commands, sourceX + 5d, y - 34d, widths[4] - 10d, "FileDirectory", IoFatReportFontKind.Mono, 5.7d, Muted);

            y -= rowHeight;
        }

        AddFooter(commands, pageNumber, totalPages, createdAt,
            "Remote listing evidence is IED-scoped and persisted with the FAT project.");
        return new IoFatReportPagePlan(pageNumber, PageWidth, PageHeight, commands);
    }

    private static IoFatReportPagePlan BuildSignOffPage(
        IoTestProject project,
        int pageNumber,
        int totalPages,
        DateTimeOffset createdAt,
        bool draft)
    {
        var commands = new List<IoFatReportCommand>();
        var projectName = FirstNonEmpty(project.DocumentControl.ClientProject, project.ProjectName, project.ProjectId);
        var documentNumber = FirstNonEmpty(
            project.DocumentControl.PurchaserDocumentNumber,
            project.DocumentControl.CompanyProjectDocumentNumber,
            project.ProjectId);
        var revision = FirstNonEmpty(project.DocumentControl.Revision, "-");

        Text(commands, Margin, 566d, 480d, projectName, IoFatReportFontKind.Bold, 7.2d, Muted);
        Text(commands, Margin, 544d, 520d, "FAT Acceptance Sign-Off", IoFatReportFontKind.Bold, 17.2d, Navy);
        Text(commands, Margin, 522d, 540d,
            "Final acceptance record for the IEC 61850 FAT evidence contained in this report.",
            IoFatReportFontKind.Regular, 8.0d, Muted);
        Line(commands, Margin, 498d, PageWidth - Margin, 498d, Border, 0.8d);

        Rect(commands, 590d, 568d, 222d, 64d, 4d, SoftBlue, Border, 0.7d);
        Text(commands, 601d, 554d, 200d, "DOCUMENT CONTROL", IoFatReportFontKind.Bold, 5.9d, Muted);
        Text(commands, 601d, 538d, 200d, documentNumber, IoFatReportFontKind.Bold, 8.2d, Navy);
        Text(commands, 601d, 523d, 200d, $"REV {revision}  |  {(draft ? "PREVIEW" : "AS TESTED")}", IoFatReportFontKind.Bold, 6.7d, Navy);
        Text(commands, 601d, 511d, 200d, draft ? "NOT FOR ISSUE" : "CUSTOMER FAT RECORD", IoFatReportFontKind.Regular, 5.8d, Muted);

        Text(commands, Margin, 470d, ContentWidth,
            "By signing below, the parties acknowledge the FAT execution and evidence recorded in the preceding pages.",
            IoFatReportFontKind.Regular, 7.2d, Ink);

        const double gap = 14d;
        var boxWidth = (ContentWidth - (gap * 2d)) / 3d;
        var x = Margin;
        foreach (var heading in new[] { "TESTED BY", "WITNESSED BY", "APPROVED BY" })
        {
            DrawSignOffBox(commands, x, 430d, boxWidth, 286d, heading);
            x += boxWidth + gap;
        }

        AddFooter(commands, pageNumber, totalPages, createdAt,
            "Final FAT acceptance signatures.");
        return new IoFatReportPagePlan(pageNumber, PageWidth, PageHeight, commands);
    }

    private static void DrawSignOffBox(
        ICollection<IoFatReportCommand> commands,
        double x,
        double top,
        double width,
        double height,
        string heading)
    {
        Rect(commands, x, top, width, height, 4d, White, Border, 0.8d);
        Rect(commands, x, top, width, 34d, 4d, SoftBlue, Border, 0.6d);
        Text(commands, x + 12d, top - 21d, width - 24d, heading, IoFatReportFontKind.Bold, 8.3d, Navy);

        var labelX = x + 12d;
        var lineX = x + 12d;
        var lineRight = x + width - 12d;

        Text(commands, labelX, top - 63d, width - 24d, "Name", IoFatReportFontKind.Bold, 6.1d, Muted);
        Line(commands, lineX, top - 92d, lineRight, top - 92d, Border, 0.65d);

        Text(commands, labelX, top - 115d, width - 24d, "Company / Organization", IoFatReportFontKind.Bold, 6.1d, Muted);
        Line(commands, lineX, top - 144d, lineRight, top - 144d, Border, 0.65d);

        Text(commands, labelX, top - 168d, width - 24d, "Signature", IoFatReportFontKind.Bold, 6.1d, Muted);
        Rect(commands, lineX, top - 183d, width - 24d, 54d, 0d, White, Border, 0.55d);

        Text(commands, labelX, top - 255d, width - 24d, "Date", IoFatReportFontKind.Bold, 6.1d, Muted);
        Line(commands, lineX, top - 275d, lineRight, top - 275d, Border, 0.65d);
    }

    private static void AddFooter(
        ICollection<IoFatReportCommand> commands,
        int pageNumber,
        int totalPages,
        DateTimeOffset createdAt,
        string note)
    {
        Line(commands, Margin, 42d, PageWidth - Margin, 42d, Border, 0.6d);
        Text(commands, Margin, 24d, 590d,
            $"Generated {createdAt:yyyy-MM-dd HH:mm:ss zzz}  |  {note}",
            IoFatReportFontKind.Regular, 6.1d, Muted);
        Text(commands, PageWidth - Margin - 118d, 24d, 118d, $"Page {pageNumber} / {totalPages}", IoFatReportFontKind.Regular, 6.1d, Muted);
    }

    private static IoFatReportCommand CorrectPageTotal(IoFatReportCommand command, int pageNumber, int totalPages)
    {
        if (command is IoFatReportTextCommand text && text.Text.StartsWith("Page ", StringComparison.Ordinal))
            return text with { Text = $"Page {pageNumber} / {totalPages}" };
        return command;
    }

    private static IReadOnlyList<string> Wrap(string? value, int maxChars, int maxLines)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        var lines = new List<string>();
        while (text.Length > maxChars && lines.Count < maxLines - 1)
        {
            var split = text.LastIndexOf(' ', maxChars);
            if (split < maxChars / 2)
                split = maxChars;
            lines.Add(text[..split].Trim());
            text = text[split..].Trim();
        }
        if (lines.Count < maxLines)
            lines.Add(Fit(text, maxChars));
        return lines;
    }

    private static string Fit(string? value, int maxChars)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        return text.Length <= maxChars ? text : text[..Math.Max(1, maxChars - 1)] + "…";
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "-";

    private static IoFatReportColor Color(string hex) => IoFatReportColor.FromHex(hex);

    private static void Rect(
        ICollection<IoFatReportCommand> commands,
        double x,
        double top,
        double width,
        double height,
        double radius,
        IoFatReportColor fill,
        IoFatReportColor stroke,
        double strokeThickness)
        => commands.Add(new IoFatReportRectCommand(x, top, width, height, radius, fill, stroke, strokeThickness));

    private static void Line(
        ICollection<IoFatReportCommand> commands,
        double x1,
        double y1,
        double x2,
        double y2,
        IoFatReportColor stroke,
        double strokeThickness)
        => commands.Add(new IoFatReportLineCommand(x1, y1, x2, y2, stroke, strokeThickness));

    private static void Text(
        ICollection<IoFatReportCommand> commands,
        double x,
        double baselineY,
        double width,
        string text,
        IoFatReportFontKind font,
        double fontSize,
        IoFatReportColor color)
        => commands.Add(new IoFatReportTextCommand(x, baselineY, width, text, font, fontSize, color));
}
