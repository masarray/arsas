using System.Globalization;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Appends only successfully verified auxiliary evidence already present in the immutable
/// native FAT snapshot. It performs no device access and leaves final page-total correction
/// to <see cref="NativeFatReportFinalization"/>.
/// </summary>
internal static class NativeFatAuxiliaryReportDecorator
{
    private const double PageWidth = 842d;
    private const double PageHeight = 595d;
    private const double Margin = 30d;
    private const double ContentWidth = PageWidth - (Margin * 2d);
    private const int ComtradeRowsPerPage = 8;

    private static readonly IoFatReportColor Navy = IoFatReportColor.FromHex("0F172A");
    private static readonly IoFatReportColor Blue = IoFatReportColor.FromHex("2563EB");
    private static readonly IoFatReportColor SoftBlue = IoFatReportColor.FromHex("EFF6FF");
    private static readonly IoFatReportColor Border = IoFatReportColor.FromHex("D9E4F0");
    private static readonly IoFatReportColor Muted = IoFatReportColor.FromHex("64748B");
    private static readonly IoFatReportColor Ink = IoFatReportColor.FromHex("1F2937");
    private static readonly IoFatReportColor White = IoFatReportColor.FromHex("FFFFFF");
    private static readonly IoFatReportColor Pass = IoFatReportColor.FromHex("15803D");
    private static readonly IoFatReportColor SoftPass = IoFatReportColor.FromHex("F0FDF4");

    public static IoFatReportLayoutPlan AppendSuccessfulEvidence(
        IoFatReportLayoutPlan baseLayout,
        NativeFatPrintPreviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(baseLayout);
        ArgumentNullException.ThrowIfNull(snapshot);

        var pages = baseLayout.Pages.ToList();
        var auxiliary = snapshot.AuxiliaryEvidence;

        if (auxiliary.ComtradeVerifiedAtUtc.HasValue && auxiliary.ComtradeRecords.Count > 0)
        {
            foreach (var chunk in auxiliary.ComtradeRecords.Chunk(ComtradeRowsPerPage))
            {
                var pageNumber = pages.Count + 1;
                pages.Add(BuildComtradePage(
                    snapshot,
                    auxiliary.ComtradeVerifiedAtUtc.Value,
                    chunk,
                    pageNumber,
                    continued: pageNumber > baseLayout.Pages.Count + 1,
                    baseLayout.CreatedAt));
            }
        }

        if (auxiliary.TimeSync is { IsSynchronized: true } timeSync)
        {
            var pageNumber = pages.Count + 1;
            pages.Add(BuildTimeSyncPage(snapshot, timeSync, pageNumber, baseLayout.CreatedAt));
        }

        return pages.Count == baseLayout.Pages.Count
            ? baseLayout
            : new IoFatReportLayoutPlan(baseLayout.ProjectId, baseLayout.CreatedAt, baseLayout.Draft, pages);
    }

    private static IoFatReportPagePlan BuildComtradePage(
        NativeFatPrintPreviewSnapshot snapshot,
        DateTimeOffset verifiedAtUtc,
        IReadOnlyList<NativeFatComtradeRecordEvidence> records,
        int pageNumber,
        bool continued,
        DateTimeOffset createdAt)
    {
        var commands = new List<IoFatReportCommand>();
        AddHeader(
            commands,
            "IEC 61850 Fault Record (COMTRADE)",
            continued ? "Available Fault Records · continued" : "Available Fault Records");
        AddScopeCard(
            commands,
            snapshot,
            $"FileDirectory verified · {verifiedAtUtc.ToUniversalTime():yyyy-MM-dd HH:mm:ss 'UTC'}",
            $"{snapshot.AuxiliaryEvidence.ComtradeRecords.Count:N0} record(s)");

        var widths = new[] { 330d, 190d, 160d, 102d };
        var headers = new[] { "Record Name", "Record Date", "Size", "Result" };
        var y = 438d;
        DrawTableHeader(commands, widths, headers, y);
        y -= 28d;

        foreach (var record in records)
        {
            const double rowHeight = 42d;
            var values = new[]
            {
                Fit(record.RecordName, 66),
                record.RecordDateUtc?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) ?? "—",
                FormatSize(record.KnownSizeBytes, record.HasUnknownSize),
                "OK"
            };
            DrawRow(commands, widths, values, y, rowHeight, resultColumn: 3);
            y -= rowHeight;
        }

        AddFooter(commands, pageNumber, createdAt, "Verified IEC 61850 FileDirectory evidence.");
        return new IoFatReportPagePlan(pageNumber, PageWidth, PageHeight, commands);
    }

    private static IoFatReportPagePlan BuildTimeSyncPage(
        NativeFatPrintPreviewSnapshot snapshot,
        NativeFatTimeSyncReportEvidence evidence,
        int pageNumber,
        DateTimeOffset createdAt)
    {
        var commands = new List<IoFatReportCommand>();
        AddHeader(commands, "IEC 61850 Time Synchronization Evidence", "Verified device-side time evidence");
        AddScopeCard(
            commands,
            snapshot,
            $"Evaluated · {evidence.VerifiedAtUtc.ToUniversalTime():yyyy-MM-dd HH:mm:ss 'UTC'}",
            "Time Sync OK");

        Rect(commands, Margin, 438d, ContentWidth, 68d, 4d, SoftPass, Border, 0.65d);
        Text(commands, Margin + 12d, 417d, 110d, "RESULT", IoFatReportFontKind.Bold, 6.1d, Muted);
        Text(commands, Margin + 12d, 394d, 110d, "OK", IoFatReportFontKind.Bold, 13.2d, Pass);
        Text(commands, Margin + 126d, 417d, ContentWidth - 138d, "VERIFICATION BASIS", IoFatReportFontKind.Bold, 6.1d, Muted);
        var summaryLines = Wrap(evidence.Summary, 104, 2);
        for (var index = 0; index < summaryLines.Count; index++)
            Text(commands, Margin + 126d, 399d - (index * 12d), ContentWidth - 138d, summaryLines[index], IoFatReportFontKind.Regular, 7.0d, Ink);

        Text(
            commands,
            Margin,
            350d,
            ContentWidth,
            evidence.LtmsPresent
                ? $"LTMS verified · {evidence.FreshPrimaryTimestampCount:N0} fresh independent IEC timestamp(s)"
                : $"LTMS not exposed · {evidence.FreshPrimaryTimestampCount:N0} fresh independent IEC timestamps verified",
            IoFatReportFontKind.Bold,
            7.1d,
            Navy);

        var widths = new[] { 82d, 226d, 92d, 68d, 158d, 88d, 68d };
        var headers = new[] { "Evidence", "IEC Reference", "Value", "Quality", "IED Timestamp", "Delta", "Result" };
        var y = 330d;
        DrawTableHeader(commands, widths, headers, y);
        y -= 28d;

        foreach (var point in evidence.SupportingPoints)
        {
            const double rowHeight = 48d;
            var values = new[]
            {
                Fit(point.Role, 14),
                Fit(FirstNonEmpty(point.IecReference, point.SignalName), 42),
                Fit(point.Value, 16),
                Fit(point.Quality, 12),
                Fit(point.DeviceTimestamp, 28),
                point.DeltaSeconds.HasValue
                    ? $"{point.DeltaSeconds.Value:0.000} s"
                    : "—",
                "OK"
            };
            DrawRow(commands, widths, values, y, rowHeight, resultColumn: 6);
            y -= rowHeight;
        }

        AddFooter(commands, pageNumber, createdAt, "Read-only evaluator; SNTP activity alone does not grant OK.");
        return new IoFatReportPagePlan(pageNumber, PageWidth, PageHeight, commands);
    }

    private static void AddHeader(ICollection<IoFatReportCommand> commands, string title, string subtitle)
    {
        NativeFatReportBranding.AddLogo(commands, PageWidth - Margin - 102d, 582d);
        Text(commands, Margin, 566d, 490d, "IEC 61850 FAT", IoFatReportFontKind.Bold, 7.2d, Muted);
        Text(commands, Margin, 544d, 570d, title, IoFatReportFontKind.Bold, 16.4d, Navy);
        Text(commands, Margin, 522d, 560d, subtitle, IoFatReportFontKind.Regular, 8.0d, Muted);
        Line(commands, Margin, 498d, PageWidth - Margin, 498d, Border, 0.8d);
    }

    private static void AddScopeCard(
        ICollection<IoFatReportCommand> commands,
        NativeFatPrintPreviewSnapshot snapshot,
        string detail,
        string result)
    {
        Rect(commands, Margin, 482d, ContentWidth, 34d, 3d, SoftBlue, Border, 0.6d);
        Text(commands, Margin + 10d, 461d, 390d,
            $"{Clean(snapshot.IedName)} · {Clean(snapshot.IpAddress)}:{snapshot.Port}",
            IoFatReportFontKind.Bold, 7.5d, Ink);
        Text(commands, Margin + 350d, 461d, 280d, detail, IoFatReportFontKind.Regular, 6.4d, Muted);
        Text(commands, PageWidth - Margin - 118d, 461d, 108d, result, IoFatReportFontKind.Bold, 7.0d, Pass);
    }

    private static void DrawTableHeader(
        ICollection<IoFatReportCommand> commands,
        IReadOnlyList<double> widths,
        IReadOnlyList<string> headers,
        double y)
    {
        var x = Margin;
        for (var index = 0; index < headers.Count; index++)
        {
            Rect(commands, x, y, widths[index], 28d, 0d, SoftBlue, Border, 0.45d);
            Text(commands, x + 5d, y - 18d, widths[index] - 10d, headers[index], IoFatReportFontKind.Bold, 6.2d, Blue);
            x += widths[index];
        }
    }

    private static void DrawRow(
        ICollection<IoFatReportCommand> commands,
        IReadOnlyList<double> widths,
        IReadOnlyList<string> values,
        double y,
        double height,
        int resultColumn)
    {
        var x = Margin;
        for (var index = 0; index < values.Count; index++)
        {
            Rect(commands, x, y, widths[index], height, 0d, White, Border, 0.4d);
            Text(
                commands,
                x + 5d,
                y - (height / 2d) - 2d,
                widths[index] - 10d,
                values[index],
                index == resultColumn ? IoFatReportFontKind.Bold : index is 1 or 4 or 5 ? IoFatReportFontKind.Mono : IoFatReportFontKind.Regular,
                index is 1 or 4 or 5 ? 5.6d : 6.4d,
                index == resultColumn ? Pass : Ink);
            x += widths[index];
        }
    }

    private static void AddFooter(
        ICollection<IoFatReportCommand> commands,
        int pageNumber,
        DateTimeOffset createdAt,
        string note)
    {
        Line(commands, Margin, 42d, PageWidth - Margin, 42d, Border, 0.6d);
        Text(commands, Margin, 24d, 620d,
            $"FAT evidence captured · {createdAt:yyyy-MM-dd HH:mm:ss zzz}  |  {note}",
            IoFatReportFontKind.Regular, 6.2d, Muted);
        Text(commands, PageWidth - Margin - 118d, 24d, 118d,
            $"Page {pageNumber} / {pageNumber}",
            IoFatReportFontKind.Regular, 6.2d, Muted);
    }

    private static string FormatSize(long knownSizeBytes, bool hasUnknownSize)
    {
        var prefix = hasUnknownSize ? ">= " : string.Empty;
        var size = Math.Max(0L, knownSizeBytes);
        if (size >= 1024L * 1024L * 1024L)
            return $"{prefix}{size / (1024d * 1024d * 1024d):0.##} GB";
        if (size >= 1024L * 1024L)
            return $"{prefix}{size / (1024d * 1024d):0.##} MB";
        if (size >= 1024L)
            return $"{prefix}{size / 1024d:0.##} KB";
        return hasUnknownSize && size == 0 ? "Unknown" : $"{size:N0} B";
    }

    private static IReadOnlyList<string> Wrap(string? value, int maxChars, int maxLines)
    {
        var remaining = Clean(value);
        var lines = new List<string>();
        while (remaining.Length > maxChars && lines.Count < maxLines - 1)
        {
            var split = remaining.LastIndexOf(' ', maxChars);
            if (split < maxChars / 2)
                split = maxChars;
            lines.Add(remaining[..split].Trim());
            remaining = remaining[split..].Trim();
        }
        lines.Add(Fit(remaining, maxChars));
        return lines;
    }

    private static string Fit(string? value, int maxChars)
    {
        var text = Clean(value);
        return text.Length <= maxChars ? text : text[..Math.Max(1, maxChars - 1)] + "…";
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "—";

    private static string Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

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
