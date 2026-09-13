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
    private const double HeaderHeight = 26d;
    private const double TableRowHeight = 30d;
    private const double TableBodyFontSize = 7.2d;
    private const double TableTimestampFontSize = 6.2d;
    private const double TelegramBaseFontSize = 6.8d;
    private const double TelegramMinimumFontSize = 5.2d;

    // Customer-facing evidence table. Total width = 782 pt (842 - 2 * 30 margin).
    // Live Value is intentionally omitted from the report: FAT evidence is Value 1 / Value 2.
    // IEC 61850 Reference keeps the dominant width; the status column is widened enough for
    // the explicit customer-facing Evidence Status wording without sacrificing timestamps.
    private static readonly double[] Widths = [66d, 280d, 40d, 72d, 92d, 72d, 92d, 68d];
    private static readonly string[] Headers =
        ["Signal", "IEC 61850 Reference", "Quality", "Value 1", "V1 Timestamp", "Value 2", "V2 Timestamp", "Evidence Status"];

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
                "No FAT signal is available for this report.",
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
                560d,
                $"FAT evidence captured · {NativeFatReportFormatting.LocalTimestamp(snapshot.CapturedAt)} · Local time",
                IoFatReportFontKind.Regular,
                6.5d,
                Muted));
            pages[index].Add(new IoFatReportTextCommand(
                PageWidth - Margin - 100d,
                24d,
                100d,
                $"Page {index + 1} / {pages.Count}",
                IoFatReportFontKind.Regular,
                6.5d,
                Muted));
        }

        var baseLayout = new IoFatReportLayoutPlan(
            snapshot.DeviceId,
            snapshot.CapturedAt,
            draft,
            pages.Select((commands, index) =>
                new IoFatReportPagePlan(index + 1, PageWidth, PageHeight, commands.ToArray())).ToArray());

        // Auxiliary pages consume only evidence already copied into the immutable snapshot.
        // Final acceptance sign-off remains the last page shared by Preview and Save PDF.
        var withAuxiliaryEvidence = NativeFatAuxiliaryReportDecorator.AppendSuccessfulEvidence(baseLayout, snapshot);
        return NativeFatReportFinalization.AppendSignOff(withAuxiliaryEvidence, snapshot);
    }

    private static List<IoFatReportCommand> NewPage(
        List<List<IoFatReportCommand>> pages,
        NativeFatPrintPreviewSnapshot snapshot,
        bool continued)
    {
        var page = new List<IoFatReportCommand>();
        pages.Add(page);

        NativeFatReportBranding.AddLogo(page, PageWidth - Margin - 102d, 582d);
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
            "Factory Acceptance Test · IEC 61850 Signal Evidence",
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
                ? $"IED: {Clean(snapshot.IedName)} (continued) · Endpoint: {Clean(snapshot.IpAddress)}:{snapshot.Port}"
                : $"IED: {Clean(snapshot.IedName)} · Endpoint: {Clean(snapshot.IpAddress)}:{snapshot.Port}",
            IoFatReportFontKind.Bold,
            8.6d,
            Ink));
        page.Add(new IoFatReportTextCommand(
            PageWidth - Margin - 250d,
            499d,
            238d,
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
                CenteredBaseline(y, HeaderHeight),
                Widths[index] - 8d,
                Headers[index],
                IoFatReportFontKind.Bold,
                index is 4 or 6 ? 6.2d : index is 1 or 7 ? 6.4d : 6.8d,
                Blue));
            x += Widths[index];
        }
        y -= HeaderHeight;
    }

    private static double GetRowHeight(NativeFatPrintPreviewRow row)
        => TableRowHeight;

    private static void DrawRow(
        List<IoFatReportCommand> page,
        NativeFatPrintPreviewRow row,
        double height,
        ref double y)
    {
        var reportResult = ReportResult(row.Result);
        var cells = new[]
        {
            Clean(row.Signal),
            string.Empty,
            NativeFatReportFormatting.Quality(row.Quality),
            Clean(row.Value1),
            Clean(row.Value1TimestampText),
            Clean(row.Value2),
            Clean(row.Value2TimestampText),
            reportResult
        };

        var baseline = CenteredBaseline(y, height);
        var x = Margin;
        for (var index = 0; index < cells.Length; index++)
        {
            page.Add(new IoFatReportRectCommand(x, y, Widths[index], height, 0d, White, Border, 0.35d));

            if (index == 1)
            {
                page.Add(new IoFatReportTextCommand(
                    x + 4d,
                    baseline,
                    Widths[index] - 8d,
                    Clean(row.IecTelegram),
                    IoFatReportFontKind.Mono,
                    TelegramFontSize(row.IecTelegram),
                    Ink));
            }
            else
            {
                var isTimestamp = index is 4 or 6;
                page.Add(new IoFatReportTextCommand(
                    x + 4d,
                    baseline,
                    Widths[index] - 8d,
                    cells[index],
                    isTimestamp
                        ? IoFatReportFontKind.Mono
                        : index is 0 or 7 ? IoFatReportFontKind.Bold : IoFatReportFontKind.Regular,
                    isTimestamp ? TableTimestampFontSize : TableBodyFontSize,
                    index == 7 ? ResultColor(reportResult) : Ink));
            }

            x += Widths[index];
        }

        y -= height;
    }

    private static double CenteredBaseline(double top, double height)
        => top - (height / 2d) - 2d;

    private static double TelegramFontSize(string? value)
    {
        var text = Clean(value);
        if (text.Length == 0)
            return TelegramBaseFontSize;

        var availableWidth = Widths[1] - 8d;
        var fitted = availableWidth / (text.Length * 0.62d);
        return Math.Clamp(fitted, TelegramMinimumFontSize, TelegramBaseFontSize);
    }

    private static string ReportResult(string? result)
    {
        var value = Clean(result);
        return value.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("OK", StringComparison.OrdinalIgnoreCase)
            ? "Complete"
            : value;
    }

    private static IoFatReportColor ResultColor(string? result)
    {
        var value = Clean(result);
        if (value.Equals("Complete", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("PASS", StringComparison.OrdinalIgnoreCase) ||
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
