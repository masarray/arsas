namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// P4D bridge from the immutable native FAT snapshot to the shared report command model.
/// This class owns no acquisition/runtime state and performs no SCL import or reconnect.
/// Rendering remains delegated to IoFatReportPreviewDocumentBuilder.Render so WPF preview
/// and the native report stack keep one FixedDocument authority.
/// </summary>
internal static class NativeFatP4DReportAdapter
{
    private const double PageWidth = 842d;
    private const double PageHeight = 595d;
    private const double Margin = 30d;
    private const double ContentTop = 466d;
    private const double ContentBottom = 52d;
    private const double HeaderHeight = 24d;
    private const double MinimumRowHeight = 30d;
    private const int TelegramCharsPerLine = 38;

    // Exact P4C visible contract. Total width = 782 pt (842 - 2 * 30 margin).
    // Timestamp columns are intentionally first-class columns so the immutable report mirrors
    // the FAT grid rather than collapsing observation metadata into Value 1 / Value 2 text.
    private static readonly double[] Widths = [90d, 200d, 65d, 70d, 62d, 90d, 62d, 90d, 53d];
    private static readonly string[] Headers =
        ["Signal", "IEC Telegram", "Quality", "Live Value", "Value 1", "V1 Timestamp", "Value 2", "V2 Timestamp", "Result"];

    private static readonly IoFatReportColor Navy = IoFatReportColor.FromHex("0F172A");
    private static readonly IoFatReportColor Blue = IoFatReportColor.FromHex("2563EB");
    private static readonly IoFatReportColor SoftBlue = IoFatReportColor.FromHex("EFF6FF");
    private static readonly IoFatReportColor Border = IoFatReportColor.FromHex("DCE5F0");
    private static readonly IoFatReportColor Ink = IoFatReportColor.FromHex("243146");
    private static readonly IoFatReportColor Muted = IoFatReportColor.FromHex("64748B");
    private static readonly IoFatReportColor White = IoFatReportColor.FromHex("FFFFFF");
    private static readonly IoFatReportColor Pass = IoFatReportColor.FromHex("15803D");
    private static readonly IoFatReportColor Attention = IoFatReportColor.FromHex("B45309");
    private static readonly IoFatReportColor Fail = IoFatReportColor.FromHex("B91C1C");

    public static IoFatReportLayoutPlan Build(NativeFatPrintPreviewSnapshot snapshot, bool draft = true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var pages = new List<List<IoFatReportCommand>>();
        var page = NewPage(pages, snapshot, continued: false);
        var y = ContentTop;
        DrawTableHeader(page, ref y);

        foreach (var row in snapshot.Rows)
        {
            var height = GetRowHeight(row);
            if (y - height < ContentBottom)
            {
                page = NewPage(pages, snapshot, continued: true);
                y = ContentTop;
                DrawTableHeader(page, ref y);
            }

            DrawRow(page, row, height, ref y);
        }

        if (snapshot.Rows.Count == 0)
        {
            page.Add(new IoFatReportTextCommand(
                Margin,
                y - 20d,
                600d,
                "No canonical FAT row is present in this snapshot.",
                IoFatReportFontKind.Bold,
                8.5d,
                Attention));
        }

        for (var index = 0; index < pages.Count; index++)
        {
            pages[index].Add(new IoFatReportLineCommand(Margin, 42d, PageWidth - Margin, 42d, Border, 0.6d));
            pages[index].Add(new IoFatReportTextCommand(
                Margin,
                24d,
                520d,
                $"Immutable Engineering FAT snapshot · {snapshot.CapturedAt:yyyy-MM-dd HH:mm:ss zzz}",
                IoFatReportFontKind.Regular,
                6.2d,
                Muted));
            pages[index].Add(new IoFatReportTextCommand(
                PageWidth - Margin - 100d,
                24d,
                100d,
                $"Page {index + 1} / {pages.Count}",
                IoFatReportFontKind.Regular,
                6.2d,
                Muted));
        }

        return new IoFatReportLayoutPlan(
            snapshot.DeviceId,
            snapshot.CapturedAt,
            draft,
            pages.Select((commands, index) =>
                new IoFatReportPagePlan(index + 1, PageWidth, PageHeight, commands.ToArray())).ToArray());
    }

    private static List<IoFatReportCommand> NewPage(
        List<List<IoFatReportCommand>> pages,
        NativeFatPrintPreviewSnapshot snapshot,
        bool continued)
    {
        var page = new List<IoFatReportCommand>();
        pages.Add(page);

        page.Add(new IoFatReportTextCommand(
            Margin,
            562d,
            520d,
            "IEC 61850 FAT Evidence Report",
            IoFatReportFontKind.Bold,
            16.8d,
            Navy));
        page.Add(new IoFatReportTextCommand(
            Margin,
            542d,
            560d,
            "Canonical Explorer snapshot · sparse FAT evidence · no acquisition restart",
            IoFatReportFontKind.Regular,
            7.6d,
            Muted));
        page.Add(new IoFatReportRectCommand(
            Margin,
            520d,
            PageWidth - (Margin * 2d),
            38d,
            3d,
            SoftBlue,
            Border,
            0.6d));
        page.Add(new IoFatReportTextCommand(
            Margin + 12d,
            499d,
            470d,
            continued
                ? $"{Clean(snapshot.IedName)} (continued) · {Clean(snapshot.IpAddress)}:{snapshot.Port}"
                : $"{Clean(snapshot.IedName)} · {Clean(snapshot.IpAddress)}:{snapshot.Port}",
            IoFatReportFontKind.Bold,
            8.6d,
            Ink));
        page.Add(new IoFatReportTextCommand(
            PageWidth - Margin - 220d,
            499d,
            208d,
            snapshot.ProgressText,
            IoFatReportFontKind.Bold,
            8.2d,
            snapshot.CompleteCount == snapshot.Rows.Count && snapshot.Rows.Count > 0 ? Pass : Blue));
        page.Add(new IoFatReportLineCommand(Margin, 482d, PageWidth - Margin, 482d, Border, 0.7d));
        return page;
    }

    private static void DrawTableHeader(List<IoFatReportCommand> page, ref double y)
    {
        var x = Margin;
        for (var index = 0; index < Headers.Length; index++)
        {
            page.Add(new IoFatReportRectCommand(x, y, Widths[index], HeaderHeight, 0d, SoftBlue, Border, 0.45d));
            page.Add(new IoFatReportTextCommand(
                x + 4d,
                y - 15.5d,
                Widths[index] - 8d,
                Headers[index],
                IoFatReportFontKind.Bold,
                index is 5 or 7 ? 5.25d : 5.8d,
                Blue));
            x += Widths[index];
        }
        y -= HeaderHeight;
    }

    private static double GetRowHeight(NativeFatPrintPreviewRow row)
        => Math.Max(MinimumRowHeight, 12d + (WrapTelegram(row.IecTelegram).Count * 8.6d));

    private static void DrawRow(
        List<IoFatReportCommand> page,
        NativeFatPrintPreviewRow row,
        double height,
        ref double y)
    {
        var cells = new[]
        {
            Clean(row.Signal),
            string.Empty,
            Clean(row.Quality),
            Clean(row.LiveValue),
            Clean(row.Value1),
            Clean(row.Value1TimestampText),
            Clean(row.Value2),
            Clean(row.Value2TimestampText),
            Clean(row.Result)
        };

        var x = Margin;
        for (var index = 0; index < cells.Length; index++)
        {
            page.Add(new IoFatReportRectCommand(x, y, Widths[index], height, 0d, White, Border, 0.35d));

            if (index == 1)
            {
                var lineY = y - 12d;
                foreach (var line in WrapTelegram(row.IecTelegram))
                {
                    page.Add(new IoFatReportTextCommand(
                        x + 4d,
                        lineY,
                        Widths[index] - 8d,
                        line,
                        IoFatReportFontKind.Mono,
                        5.35d,
                        Ink));
                    lineY -= 8.6d;
                }
            }
            else
            {
                var isTimestamp = index is 5 or 7;
                page.Add(new IoFatReportTextCommand(
                    x + 4d,
                    y - 18d,
                    Widths[index] - 8d,
                    cells[index],
                    isTimestamp
                        ? IoFatReportFontKind.Mono
                        : index is 0 or 8 ? IoFatReportFontKind.Bold : IoFatReportFontKind.Regular,
                    isTimestamp ? 4.9d : 5.8d,
                    index == 8 ? ResultColor(row.Result) : Ink));
            }

            x += Widths[index];
        }

        y -= height;
    }

    private static IReadOnlyList<string> WrapTelegram(string? value)
    {
        var text = Clean(value);
        if (text.Length == 0)
            return ["—"];
        if (text.Length <= TelegramCharsPerLine)
            return [text];

        var lines = new List<string>();
        for (var offset = 0; offset < text.Length; offset += TelegramCharsPerLine)
            lines.Add(text.Substring(offset, Math.Min(TelegramCharsPerLine, text.Length - offset)));
        return lines;
    }

    private static IoFatReportColor ResultColor(string? result)
    {
        var value = Clean(result);
        if (value.Contains("PASS", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("COMPLETE", StringComparison.OrdinalIgnoreCase))
            return Pass;
        if (value.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
            return Fail;
        if (value.Contains("REVIEW", StringComparison.OrdinalIgnoreCase))
            return Attention;
        return Muted;
    }

    private static string Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
