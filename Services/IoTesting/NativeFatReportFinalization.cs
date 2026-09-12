namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Finalizes the immutable native FAT layout without rebuilding an IoTestProject.
/// The sign-off page is intentionally blank evidence: it provides the controlled
/// TESTED BY / WITNESSED BY / APPROVED BY acceptance fields and never invents names,
/// signatures, dates, COMTRADE records, or time-sync evidence.
/// </summary>
internal static class NativeFatReportFinalization
{
    private const double PageWidth = 842d;
    private const double PageHeight = 595d;
    private const double Margin = 30d;
    private const double ContentWidth = PageWidth - (Margin * 2d);

    private static readonly IoFatReportColor Navy = IoFatReportColor.FromHex("0F172A");
    private static readonly IoFatReportColor Blue = IoFatReportColor.FromHex("2563EB");
    private static readonly IoFatReportColor SoftBlue = IoFatReportColor.FromHex("EFF6FF");
    private static readonly IoFatReportColor Border = IoFatReportColor.FromHex("D9E4F0");
    private static readonly IoFatReportColor Muted = IoFatReportColor.FromHex("64748B");
    private static readonly IoFatReportColor Ink = IoFatReportColor.FromHex("1F2937");
    private static readonly IoFatReportColor White = IoFatReportColor.FromHex("FFFFFF");

    public static IoFatReportLayoutPlan AppendSignOff(
        IoFatReportLayoutPlan baseLayout,
        NativeFatPrintPreviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(baseLayout);
        ArgumentNullException.ThrowIfNull(snapshot);

        var totalPages = baseLayout.Pages.Count + 1;
        var pages = new List<IoFatReportPagePlan>(totalPages);

        for (var index = 0; index < baseLayout.Pages.Count; index++)
        {
            var corrected = baseLayout.Pages[index].Commands
                .Select(command => CorrectPageTotal(command, index + 1, totalPages))
                .ToArray();
            pages.Add(new IoFatReportPagePlan(index + 1, PageWidth, PageHeight, corrected));
        }

        pages.Add(BuildSignOffPage(snapshot, totalPages, totalPages, baseLayout.CreatedAt));
        return new IoFatReportLayoutPlan(baseLayout.ProjectId, baseLayout.CreatedAt, baseLayout.Draft, pages);
    }

    private static IoFatReportPagePlan BuildSignOffPage(
        NativeFatPrintPreviewSnapshot snapshot,
        int pageNumber,
        int totalPages,
        DateTimeOffset createdAt)
    {
        var commands = new List<IoFatReportCommand>();

        Text(commands, Margin, 566d, 490d, "ARSAS | IEC 61850 FAT", IoFatReportFontKind.Bold, 7.2d, Muted);
        Text(commands, Margin, 544d, 520d, "FAT Acceptance Sign-Off", IoFatReportFontKind.Bold, 17.2d, Navy);
        Text(commands, Margin, 522d, 540d,
            "Final acceptance record for the immutable IEC 61850 FAT evidence contained in this report.",
            IoFatReportFontKind.Regular, 8.0d, Muted);
        Line(commands, Margin, 498d, PageWidth - Margin, 498d, Border, 0.8d);

        Rect(commands, 590d, 568d, 222d, 64d, 4d, SoftBlue, Border, 0.7d);
        Text(commands, 601d, 554d, 200d, "IED / REPORT SCOPE", IoFatReportFontKind.Bold, 5.9d, Muted);
        Text(commands, 601d, 538d, 200d, Clean(snapshot.IedName), IoFatReportFontKind.Bold, 8.2d, Navy);
        Text(commands, 601d, 523d, 200d, Clean(snapshot.DeviceId), IoFatReportFontKind.Mono, 5.8d, Blue);
        Text(commands, 601d, 511d, 200d, "FOR FAT RECORD", IoFatReportFontKind.Regular, 5.8d, Muted);

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

        Line(commands, Margin, 42d, PageWidth - Margin, 42d, Border, 0.6d);
        Text(commands, Margin, 24d, 620d,
            $"Immutable FAT snapshot · {createdAt:yyyy-MM-dd HH:mm:ss zzz} · blank sign-off fields are intentionally not prefilled",
            IoFatReportFontKind.Regular, 6.2d, Muted);
        Text(commands, PageWidth - Margin - 118d, 24d, 118d,
            $"Page {pageNumber} / {totalPages}",
            IoFatReportFontKind.Regular, 6.2d, Muted);

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

        var lineX = x + 12d;
        var lineRight = x + width - 12d;
        Text(commands, lineX, top - 63d, width - 24d, "Name", IoFatReportFontKind.Bold, 6.1d, Muted);
        Line(commands, lineX, top - 92d, lineRight, top - 92d, Border, 0.65d);
        Text(commands, lineX, top - 115d, width - 24d, "Company / Organization", IoFatReportFontKind.Bold, 6.1d, Muted);
        Line(commands, lineX, top - 144d, lineRight, top - 144d, Border, 0.65d);
        Text(commands, lineX, top - 168d, width - 24d, "Signature", IoFatReportFontKind.Bold, 6.1d, Muted);
        Rect(commands, lineX, top - 183d, width - 24d, 54d, 0d, White, Border, 0.55d);
        Text(commands, lineX, top - 255d, width - 24d, "Date", IoFatReportFontKind.Bold, 6.1d, Muted);
        Line(commands, lineX, top - 275d, lineRight, top - 275d, Border, 0.65d);
    }

    private static IoFatReportCommand CorrectPageTotal(IoFatReportCommand command, int pageNumber, int totalPages)
    {
        if (command is IoFatReportTextCommand text && text.Text.StartsWith("Page ", StringComparison.Ordinal))
            return text with { Text = $"Page {pageNumber} / {totalPages}" };
        return command;
    }

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

    private static string Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
