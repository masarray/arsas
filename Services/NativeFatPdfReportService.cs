using System.Text;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester.Services;

/// <summary>
/// Native FAT PDF export driven by the same immutable snapshot used by the integrated
/// preview. It reuses ARSAS' existing embedded-font PDF serializer, but does not convert
/// the native state into the legacy IoTest evidence runtime or start another acquisition.
/// </summary>
public static class NativeFatPdfReportService
{
    private const double PageWidth = 842d;
    private const double PageHeight = 595d;
    private const double Margin = 30d;
    private const double HeaderBottom = 500d;
    private const double ContentTop = 484d;
    private const double ContentBottom = 54d;
    private const double ContentWidth = PageWidth - (Margin * 2d);

    private static readonly IoFatReportColor Navy = IoFatReportColor.FromHex("0F172A");
    private static readonly IoFatReportColor Blue = IoFatReportColor.FromHex("2563EB");
    private static readonly IoFatReportColor Muted = IoFatReportColor.FromHex("64748B");
    private static readonly IoFatReportColor Border = IoFatReportColor.FromHex("DDE7F3");
    private static readonly IoFatReportColor SoftLine = IoFatReportColor.FromHex("EEF2F7");
    private static readonly IoFatReportColor SoftBlue = IoFatReportColor.FromHex("EFF6FF");
    private static readonly IoFatReportColor White = IoFatReportColor.FromHex("FFFFFF");
    private static readonly IoFatReportColor Pass = IoFatReportColor.FromHex("15803D");
    private static readonly IoFatReportColor Review = IoFatReportColor.FromHex("B45309");
    private static readonly IoFatReportColor Fail = IoFatReportColor.FromHex("B91C1C");
    private static readonly IoFatReportColor SoftPass = IoFatReportColor.FromHex("F0FDF4");
    private static readonly IoFatReportColor SoftReview = IoFatReportColor.FromHex("FFFBEB");
    private static readonly IoFatReportColor SoftFail = IoFatReportColor.FromHex("FEF2F2");

    public static byte[] Generate(NativeFatReportSnapshot snapshot, bool includeHistorical = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var layout = BuildLayout(snapshot, includeHistorical);

        // IoFatNativePdfWriter only reads this project object for PDF document metadata.
        // The report command stream itself comes entirely from NativeFatReportSnapshot.
        var metadataProject = new IoTestProject
        {
            ProjectId = string.IsNullOrWhiteSpace(snapshot.DeviceId) ? snapshot.IedName : snapshot.DeviceId,
            SchemaVersion = "ARSAS-NATIVE-FAT-1",
            ProjectName = $"ARSAS Native FAT - {snapshot.IedName}"
        };
        return IoFatNativePdfWriter.Build(layout, metadataProject);
    }

    public static void Save(
        string fileName,
        NativeFatReportSnapshot snapshot,
        bool includeHistorical = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var bytes = Generate(snapshot, includeHistorical);
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
                       16 * 1024,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static IoFatReportLayoutPlan BuildLayout(
        NativeFatReportSnapshot snapshot,
        bool includeHistorical)
    {
        var rows = snapshot.Rows
            .Where(row => includeHistorical || !row.IsHistorical)
            .ToArray();
        var pages = new List<PageBuilder>();
        var page = NewPage(pages);
        var cursorY = ContentTop;

        DrawTableHeader(page, ref cursorY);
        var rowNumber = 0;
        foreach (var row in rows)
        {
            rowNumber++;
            var cells = BuildCells(row, rowNumber);
            var rowHeight = EstimateRowHeight(cells);
            if (cursorY - rowHeight < ContentBottom)
            {
                page = NewPage(pages);
                cursorY = ContentTop;
                DrawTableHeader(page, ref cursorY);
            }
            DrawRow(page, cells, rowHeight, ref cursorY);
        }

        if (rows.Length == 0)
        {
            page.Rect(Margin, cursorY, ContentWidth, 38d, 5d, SoftReview, Border, 0.7d);
            page.Text(Margin + 12d, cursorY - 23d, ContentWidth - 24d,
                "No current native FAT signal is available for this export scope.",
                IoFatReportFontKind.Regular, 8d, Review);
        }

        var totalPages = pages.Count;
        for (var index = 0; index < pages.Count; index++)
            DrawPageChrome(pages[index], snapshot, includeHistorical, index + 1, totalPages);

        return new IoFatReportLayoutPlan(
            string.IsNullOrWhiteSpace(snapshot.DeviceId) ? snapshot.IedName : snapshot.DeviceId,
            snapshot.GeneratedUtc.ToLocalTime(),
            Draft: false,
            pages.Select((item, index) => new IoFatReportPagePlan(
                    index + 1,
                    PageWidth,
                    PageHeight,
                    item.Commands.ToArray()))
                .ToArray());
    }

    private static PageBuilder NewPage(List<PageBuilder> pages)
    {
        var page = new PageBuilder();
        pages.Add(page);
        return page;
    }

    private static void DrawPageChrome(
        PageBuilder page,
        NativeFatReportSnapshot snapshot,
        bool includeHistorical,
        int pageNumber,
        int totalPages)
    {
        var tone = ResolveTone(snapshot);
        var toneColor = ToneColor(tone);
        var toneBackground = ToneBackground(tone);

        page.Line(Margin, HeaderBottom, PageWidth - Margin, HeaderBottom, Border, 0.8d);
        page.Text(Margin, 562d, 440d, "ARSAS | IEC 61850 FAT", IoFatReportFontKind.Bold, 7.4d, Muted);
        page.Text(Margin, 540d, 520d, "Native FAT Evidence Report", IoFatReportFontKind.Bold, 20.4d, Navy);
        page.Text(Margin, 520d, 590d,
            $"{Clean(snapshot.IedName)} | {Clean(snapshot.IpAddress)}:{snapshot.Port} | immutable capture snapshot",
            IoFatReportFontKind.Regular, 7.7d, Muted);

        const double cardWidth = 184d;
        const double cardHeight = 62d;
        var cardX = PageWidth - Margin - cardWidth;
        const double cardTop = 568d;
        page.Rect(cardX, cardTop, cardWidth, cardHeight, 6d, toneBackground, toneColor, 0.9d);
        page.Text(cardX + 11d, cardTop - 15d, cardWidth - 22d, "CURRENT SCOPE", IoFatReportFontKind.Bold, 6.2d, Muted);
        page.Text(cardX + 11d, cardTop - 34d, cardWidth - 22d, tone, IoFatReportFontKind.Bold, 14.2d, toneColor);
        page.Text(cardX + 11d, cardTop - 50d, cardWidth - 22d,
            $"P {snapshot.PassCount} | R {snapshot.ReviewCount} | F {snapshot.FailCount} | U {snapshot.UntestedCount}",
            IoFatReportFontKind.Regular, 6.3d, Muted);

        page.Line(Margin, 42d, PageWidth - Margin, 42d, Border, 0.6d);
        var historyText = includeHistorical
            ? $" | historical included: {snapshot.HistoricalCount}"
            : snapshot.HistoricalCount > 0 ? $" | historical omitted: {snapshot.HistoricalCount}" : string.Empty;
        page.Text(Margin, 24d, 650d,
            $"Generated {snapshot.GeneratedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}{historyText} | DeviceId {Clean(snapshot.DeviceId)}",
            IoFatReportFontKind.Regular, 6.1d, Muted);
        page.Text(PageWidth - Margin - 80d, 24d, 80d,
            $"Page {pageNumber} / {totalPages}", IoFatReportFontKind.Regular, 6.1d, Muted);
    }

    private static void DrawTableHeader(PageBuilder page, ref double cursorY)
    {
        var widths = ColumnWidths();
        var headers = new[] { "#", "Signal / IEC identity", "Value 1 evidence", "Value 2 evidence", "Result" };
        var x = Margin;
        const double height = 23d;
        for (var index = 0; index < headers.Length; index++)
        {
            page.Rect(x, cursorY, widths[index], height, 0d, SoftBlue, Border, 0.45d);
            page.Text(x + 5d, cursorY - 15d, widths[index] - 10d,
                headers[index], IoFatReportFontKind.Bold, 6.2d, Blue);
            x += widths[index];
        }
        cursorY -= height;
    }

    private static ReportCell[] BuildCells(NativeFatReportRow row, int rowNumber)
    {
        var scope = row.IsHistorical ? "HISTORICAL" : "CURRENT";
        var signal = $"{row.SignalName}\n{row.IecReference} [{row.FunctionalConstraint}] | {row.DataType}\n{scope}";
        var v1 = BuildEvidenceCell(
            row.Value1Text,
            row.Value1Quality,
            row.Value1DeviceTimestamp,
            row.Value1CapturedText,
            row.Value1SourceMode);
        var v2 = BuildEvidenceCell(
            row.Value2Text,
            row.Value2Quality,
            row.Value2DeviceTimestamp,
            row.Value2CapturedText,
            row.Value2SourceMode);
        var resultColor = ResultColor(row.Result, row.IsHistorical);
        var result = $"{row.Result}\nhistory {row.HistoryText}";

        return
        [
            new ReportCell(rowNumber.ToString(), 6.4d, IoFatReportFontKind.Regular, Muted),
            new ReportCell(signal, 6.25d, IoFatReportFontKind.Regular, Navy),
            new ReportCell(v1, 6.0d, IoFatReportFontKind.Regular, Navy),
            new ReportCell(v2, 6.0d, IoFatReportFontKind.Regular, Navy),
            new ReportCell(result, 6.4d, IoFatReportFontKind.Bold, resultColor)
        ];
    }

    private static string BuildEvidenceCell(
        string value,
        string quality,
        string iedTimestamp,
        string captured,
        string source)
    {
        var cleanValue = Clean(value);
        if (cleanValue is "-" or "—")
            return "Not captured";

        return $"{cleanValue}\nq={Clean(quality)} | {Clean(source)}\nIED {Clean(iedTimestamp)}\nARSAS {Clean(captured)}";
    }

    private static double EstimateRowHeight(IReadOnlyList<ReportCell> cells)
    {
        var widths = ColumnWidths();
        var maxLines = 1;
        for (var index = 0; index < cells.Count; index++)
            maxLines = Math.Max(maxLines, WrapText(cells[index].Text, widths[index] - 10d, cells[index].FontSize).Count);
        return Math.Max(38d, 9d + (maxLines * 8.0d));
    }

    private static void DrawRow(
        PageBuilder page,
        IReadOnlyList<ReportCell> cells,
        double rowHeight,
        ref double cursorY)
    {
        var widths = ColumnWidths();
        var x = Margin;
        for (var index = 0; index < cells.Count; index++)
        {
            var cell = cells[index];
            page.Rect(x, cursorY, widths[index], rowHeight, 0d, White, SoftLine, 0.35d);
            var lines = WrapText(cell.Text, widths[index] - 10d, cell.FontSize);
            var y = cursorY - 10d;
            foreach (var line in lines)
            {
                page.Text(x + 5d, y, widths[index] - 10d,
                    line, cell.Font, cell.FontSize, cell.Color);
                y -= 8.0d;
            }
            x += widths[index];
        }
        cursorY -= rowHeight;
    }

    private static double[] ColumnWidths()
        => [28d, 260d, 180d, 180d, 134d];

    private static IReadOnlyList<string> WrapText(string? value, double width, double fontSize)
    {
        var input = (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(input))
            return ["-"];

        var charsPerLine = Math.Max(7, (int)Math.Floor(width / Math.Max(2.4d, fontSize * 0.49d)));
        var lines = new List<string>();
        foreach (var paragraphValue in input.Split('\n'))
        {
            var paragraph = IoFatReportLayoutEngine.SanitizeReportText(paragraphValue);
            if (paragraph.Length == 0)
            {
                lines.Add("-");
                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new StringBuilder();
            foreach (var originalWord in words)
            {
                var word = originalWord;
                while (word.Length > charsPerLine)
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current.ToString());
                        current.Clear();
                    }
                    lines.Add(word[..charsPerLine]);
                    word = word[charsPerLine..];
                }
                if (word.Length == 0)
                    continue;
                if (current.Length == 0)
                    current.Append(word);
                else if (current.Length + 1 + word.Length <= charsPerLine)
                    current.Append(' ').Append(word);
                else
                {
                    lines.Add(current.ToString());
                    current.Clear().Append(word);
                }
            }
            if (current.Length > 0)
                lines.Add(current.ToString());
        }
        return lines.Count == 0 ? ["-"] : lines;
    }

    private static string ResolveTone(NativeFatReportSnapshot snapshot)
    {
        if (snapshot.CurrentCount == 0)
            return "NO CURRENT SIGNALS";
        if (snapshot.FailCount > 0)
            return "FAIL";
        if (snapshot.ReviewCount > 0 || snapshot.UntestedCount > 0)
            return "REVIEW";
        return snapshot.PassCount == snapshot.CurrentCount ? "PASS" : "REVIEW";
    }

    private static IoFatReportColor ToneColor(string tone)
        => tone == "PASS" ? Pass : tone == "FAIL" ? Fail : Review;

    private static IoFatReportColor ToneBackground(string tone)
        => tone == "PASS" ? SoftPass : tone == "FAIL" ? SoftFail : SoftReview;

    private static IoFatReportColor ResultColor(string result, bool historical)
    {
        if (historical)
            return Muted;
        return result switch
        {
            NativeFatResult.Pass => Pass,
            NativeFatResult.Fail => Fail,
            NativeFatResult.Review => Review,
            _ => Muted
        };
    }

    private static string Clean(string? value)
    {
        var clean = IoFatReportLayoutEngine.SanitizeReportText(value);
        return string.IsNullOrWhiteSpace(clean) ? "-" : clean;
    }

    private sealed record ReportCell(
        string Text,
        double FontSize,
        IoFatReportFontKind Font,
        IoFatReportColor Color);

    private sealed class PageBuilder
    {
        public List<IoFatReportCommand> Commands { get; } = new();

        public void Text(double x, double y, double width, string text, IoFatReportFontKind font, double size, IoFatReportColor color)
            => Commands.Add(new IoFatReportTextCommand(x, y, width, Clean(text), font, size, color));

        public void Line(double x1, double y1, double x2, double y2, IoFatReportColor stroke, double thickness)
            => Commands.Add(new IoFatReportLineCommand(x1, y1, x2, y2, stroke, thickness));

        public void Rect(double x, double topY, double width, double height, double radius, IoFatReportColor fill, IoFatReportColor stroke, double thickness)
            => Commands.Add(new IoFatReportRectCommand(x, topY, width, height, radius, fill, stroke, thickness));
    }
}
