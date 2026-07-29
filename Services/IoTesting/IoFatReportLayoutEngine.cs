// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

internal enum IoFatReportFontKind
{
    Regular,
    Bold,
    Mono
}

internal readonly record struct IoFatReportColor(byte R, byte G, byte B)
{
    public static IoFatReportColor FromHex(string hex)
    {
        var value = hex.StartsWith("#", StringComparison.Ordinal) ? hex[1..] : hex;
        if (value.Length != 6)
            throw new ArgumentException("Report color must be a six-digit RGB hex value.", nameof(hex));
        return new IoFatReportColor(
            Convert.ToByte(value[..2], 16),
            Convert.ToByte(value.Substring(2, 2), 16),
            Convert.ToByte(value.Substring(4, 2), 16));
    }
}

internal abstract record IoFatReportCommand;
internal sealed record IoFatReportRectCommand(double X, double TopY, double Width, double Height, double Radius, IoFatReportColor Fill, IoFatReportColor Stroke, double StrokeThickness) : IoFatReportCommand;
internal sealed record IoFatReportLineCommand(double X1, double Y1, double X2, double Y2, IoFatReportColor Stroke, double StrokeThickness) : IoFatReportCommand;
internal sealed record IoFatReportTextCommand(double X, double BaselineY, double Width, string Text, IoFatReportFontKind Font, double FontSize, IoFatReportColor Color) : IoFatReportCommand;
internal sealed record IoFatReportPagePlan(int PageNumber, double Width, double Height, IReadOnlyList<IoFatReportCommand> Commands);
internal sealed record IoFatReportLayoutPlan(string ProjectId, DateTimeOffset CreatedAt, bool Draft, IReadOnlyList<IoFatReportPagePlan> Pages);

/// <summary>
/// Single layout source for the native PDF writer and WPF FixedDocument preview.
/// Coordinates are expressed in PDF points.
/// </summary>
internal static class IoFatReportLayoutEngine
{
    public const double PageWidth = 842d;
    public const double PageHeight = 595d;
    private const double Margin = 30d;
    private const double HeaderBottom = 500d;
    private const double ContentTop = 484d;
    private const double ContentBottom = 55d;
    private const double ContentWidth = PageWidth - (Margin * 2d);

    private static readonly IoFatReportColor BrandNavy = Color("0F172A");
    private static readonly IoFatReportColor BrandBlue = Color("2563EB");
    private static readonly IoFatReportColor SoftBlue = Color("EFF6FF");
    private static readonly IoFatReportColor SoftSlate = Color("F8FAFC");
    private static readonly IoFatReportColor Border = Color("DDE7F3");
    private static readonly IoFatReportColor SoftLine = Color("EEF2F7");
    private static readonly IoFatReportColor Muted = Color("64748B");
    private static readonly IoFatReportColor Ink = Color("111827");
    private static readonly IoFatReportColor White = Color("FFFFFF");
    private static readonly IoFatReportColor Pass = Color("15803D");
    private static readonly IoFatReportColor Attention = Color("B45309");
    private static readonly IoFatReportColor Fail = Color("B91C1C");
    private static readonly IoFatReportColor SoftPass = Color("F0FDF4");
    private static readonly IoFatReportColor SoftAttention = Color("FFFBEB");
    private static readonly IoFatReportColor SoftFail = Color("FEF2F2");

    public static IoFatReportLayoutPlan Build(IoTestProject project, DateTimeOffset created, bool draft = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new Builder(project, created, draft).Render();
    }

    private sealed class Builder
    {
        private readonly IoTestProject _project;
        private readonly DateTimeOffset _created;
        private readonly bool _draft;
        private readonly List<PageBuilder> _pages = new();
        private PageBuilder _page = null!;
        private double _cursorY;

        public Builder(IoTestProject project, DateTimeOffset created, bool draft)
        {
            _project = project;
            _created = created;
            _draft = draft;
        }

        public IoFatReportLayoutPlan Render()
        {
            NewPage();
            DrawExecutiveSummary();
            foreach (var ied in _project.Ieds)
                DrawIedSection(ied);
            if (_project.Ieds.Count == 0)
                DrawEmptyProjectNotice();

            var totalPages = _pages.Count;
            for (var index = 0; index < totalPages; index++)
                DrawPageChrome(_pages[index], index + 1, totalPages);

            return new IoFatReportLayoutPlan(
                _project.ProjectId,
                _created,
                _draft,
                _pages.Select((page, index) => new IoFatReportPagePlan(index + 1, PageWidth, PageHeight, page.Commands.ToArray())).ToArray());
        }

        private void NewPage()
        {
            _page = new PageBuilder();
            _pages.Add(_page);
            _cursorY = ContentTop;
        }

        private void Ensure(double requiredHeight)
        {
            if (_cursorY - requiredHeight < ContentBottom)
                NewPage();
        }

        private void DrawPageChrome(PageBuilder page, int pageNumber, int totalPages)
        {
            var counts = Counts(_project);
            var tone = _draft ? "DRAFT / LIVE" : ResolveOverallTone(counts);
            var toneColor = _draft ? Attention : ResolveToneColor(tone);
            var toneBackground = _draft ? SoftAttention : ResolveToneBackground(tone);
            var scope = _project.Ieds.Count == 1 ? _project.Ieds[0].IedName : _project.ProjectId;

            page.Line(Margin, HeaderBottom, PageWidth - Margin, HeaderBottom, Border, 0.8d);
            page.Text(Margin, 562d, 390d, "ARSAS - IEC 61850 IO LIST FAT", IoFatReportFontKind.Bold, 7.3d, Muted);
            page.Text(Margin, 541d, 500d, "IEC 61850 FAT Evidence Report", IoFatReportFontKind.Bold, 20.4d, BrandNavy);
            page.Text(Margin, 523d, 590d, "Ordered OFF > ON > OFF verification with relay timestamps, quality and acquisition source.", IoFatReportFontKind.Regular, 7.3d, Muted);

            const double cardWidth = 142d;
            const double cardHeight = 58d;
            var cardX = PageWidth - Margin - cardWidth;
            const double cardTop = 566d;
            page.RoundRect(cardX, cardTop, cardWidth, cardHeight, 5d, toneBackground, toneColor, 0.8d);
            page.Text(cardX + 10d, cardTop - 15d, cardWidth - 20d, _draft ? "PREVIEW STATUS" : "EVIDENCE STATUS", IoFatReportFontKind.Bold, 6.2d, Muted);
            page.Text(cardX + 10d, cardTop - 35d, cardWidth - 20d, tone, IoFatReportFontKind.Bold, 16.2d, toneColor);
            page.Text(cardX + 10d, cardTop - 49d, cardWidth - 20d, Truncate(scope, 28), IoFatReportFontKind.Regular, 5.8d, Muted);

            page.Line(Margin, 42d, PageWidth - Margin, 42d, Border, 0.6d);
            page.Text(Margin, 24d, 650d, $"Generated {_created:yyyy-MM-dd HH:mm:ss zzz}  |  Project {_project.ProjectId}  |  Workbook SHA256 {ShortHash(_project.SourceWorkbookSha256)}", IoFatReportFontKind.Regular, 6.2d, Muted);
            page.Text(PageWidth - Margin - 72d, 24d, 72d, $"Page {pageNumber} / {totalPages}", IoFatReportFontKind.Regular, 6.2d, Muted);
        }

        private void DrawExecutiveSummary()
        {
            var counts = Counts(_project);
            var enabledSignals = _project.Ieds.Sum(ied => ReportPoints(ied).Count);
            const double height = 104d;
            Ensure(height + 12d);

            _page.RoundRect(Margin, _cursorY, ContentWidth, height, 5d, SoftSlate, Border, 0.8d);
            _page.Text(Margin + 13d, _cursorY - 19d, ContentWidth - 26d, "Project Evidence Summary", IoFatReportFontKind.Bold, 11.2d, BrandNavy);
            _page.Text(Margin + 13d, _cursorY - 36d, ContentWidth - 26d, $"{Clean(_project.ProjectName)}  |  {Clean(_project.ProjectId)}", IoFatReportFontKind.Bold, 8.2d, Ink);
            _page.Text(Margin + 13d, _cursorY - 51d, ContentWidth - 26d, $"Source: {Clean(_project.SourceWorkbookName)}", IoFatReportFontKind.Regular, 6.8d, Muted);
            _page.Text(Margin + 13d, _cursorY - 64d, ContentWidth - 26d, $"Workbook SHA-256: {Clean(_project.SourceWorkbookSha256)}", IoFatReportFontKind.Mono, 5.8d, Muted);

            const double metricTop = 86d;
            const double gap = 7d;
            var metricWidth = (ContentWidth - 26d - (gap * 5d)) / 6d;
            var x = Margin + 13d;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "IED", _project.Ieds.Count.ToString(CultureInfo.InvariantCulture), BrandBlue, SoftBlue);
            x += metricWidth + gap;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "TEST POINTS", enabledSignals.ToString(CultureInfo.InvariantCulture), BrandNavy, White);
            x += metricWidth + gap;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "PASS", counts.Passed.ToString(CultureInfo.InvariantCulture), Pass, SoftPass);
            x += metricWidth + gap;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "REVIEW", counts.Review.ToString(CultureInfo.InvariantCulture), Attention, SoftAttention);
            x += metricWidth + gap;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "FAIL", counts.Failed.ToString(CultureInfo.InvariantCulture), Fail, SoftFail);
            x += metricWidth + gap;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "PENDING", counts.Pending.ToString(CultureInfo.InvariantCulture), Muted, White);
            _cursorY -= height + 12d;
        }

        private void DrawMetric(double x, double top, double width, string label, string value, IoFatReportColor color, IoFatReportColor background)
        {
            _page.RoundRect(x, top, width, 29d, 4d, background, Border, 0.55d);
            _page.Text(x + 7d, top - 11d, width - 14d, label, IoFatReportFontKind.Bold, 5.3d, Muted);
            _page.Text(x + 7d, top - 23d, width - 14d, value, IoFatReportFontKind.Bold, 9.2d, color);
        }

        private void DrawIedSection(IoTestIedPlan ied)
        {
            var points = ReportPoints(ied);
            Ensure(82d);
            DrawIedHeader(ied, points, continued: false);
            DrawTableHeader();

            var rowNumber = 0;
            foreach (var point in points)
            {
                rowNumber++;
                var cells = BuildCells(point, rowNumber);
                var rowHeight = EstimateRowHeight(cells);
                if (_cursorY - rowHeight < ContentBottom)
                {
                    NewPage();
                    DrawIedHeader(ied, points, continued: true);
                    DrawTableHeader();
                }
                DrawRow(cells, rowHeight);
            }

            if (points.Count == 0)
            {
                Ensure(34d);
                _page.RoundRect(Margin, _cursorY, ContentWidth, 28d, 4d, SoftSlate, Border, 0.6d);
                _page.Text(Margin + 10d, _cursorY - 18d, ContentWidth - 20d, "No enabled IO-list test point is available for this IED.", IoFatReportFontKind.Regular, 7d, Muted);
                _cursorY -= 38d;
            }
            _cursorY -= 11d;
        }

        private void DrawIedHeader(IoTestIedPlan ied, IReadOnlyList<IoTestPointPlan> points, bool continued)
        {
            var title = continued ? $"{ied.IedName} (continued)" : ied.IedName;
            var passed = points.Count(point => point.Runtime.State == IoTestPointState.Passed);
            var review = points.Count(point => point.Runtime.State == IoTestPointState.Review);
            var failed = points.Count(point => point.Runtime.State == IoTestPointState.Failed);
            var pending = Math.Max(0, points.Count - passed - review - failed);
            const double height = 48d;

            _page.RoundRect(Margin, _cursorY, ContentWidth, height, 5d, White, Border, 0.7d);
            _page.Rect(Margin, _cursorY, 4d, height, BrandBlue, BrandBlue, 0d);
            _page.Text(Margin + 13d, _cursorY - 17d, 360d, Clean(title), IoFatReportFontKind.Bold, 10.2d, BrandNavy);
            _page.Text(Margin + 13d, _cursorY - 33d, 520d, $"{Clean(ied.IpAddress)}  |  {Clean(ied.IedRole)}  |  {Clean(ied.Location)}  |  {Clean(ied.VoltageLevel)}  |  {Clean(ied.Switchgear)}", IoFatReportFontKind.Regular, 6.3d, Muted);
            _page.Text(PageWidth - Margin - 244d, _cursorY - 18d, 232d, $"{points.Count} signals  |  {passed} PASS  |  {review} review  |  {failed} fail  |  {pending} pending", IoFatReportFontKind.Bold, 6.1d, BrandBlue);
            _cursorY -= height + 7d;
        }

        private void DrawTableHeader()
        {
            Ensure(22d);
            var widths = ColumnWidths();
            var headers = new[] { "#", "Signal", "IEC 61850 reference", "Expected ON / OFF", "ON evidence", "OFF evidence", "Result", "Reason" };
            var x = Margin;
            const double height = 19d;
            for (var index = 0; index < headers.Length; index++)
            {
                _page.Rect(x, _cursorY, widths[index], height, SoftBlue, Border, 0.45d);
                _page.Text(x + 4d, _cursorY - 12.5d, widths[index] - 8d, headers[index], IoFatReportFontKind.Bold, 5.55d, BrandBlue);
                x += widths[index];
            }
            _cursorY -= height;
        }

        private void DrawRow(IReadOnlyList<ReportCell> cells, double rowHeight)
        {
            var widths = ColumnWidths();
            var x = Margin;
            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                _page.Rect(x, _cursorY, widths[index], rowHeight, White, SoftLine, 0.35d);
                var lines = WrapText(cell.Text, widths[index] - 8d, cell.FontSize, cell.MaxLines);
                var y = _cursorY - 8.5d;
                foreach (var line in lines)
                {
                    _page.Text(x + 4d, y, widths[index] - 8d, line, cell.Font, cell.FontSize, cell.Color);
                    y -= cell.FontSize + 1.35d;
                }
                x += widths[index];
            }
            _cursorY -= rowHeight;
        }

        private static ReportCell[] BuildCells(IoTestPointPlan point, int rowNumber)
        {
            var stateColor = point.Runtime.State switch
            {
                IoTestPointState.Passed => Pass,
                IoTestPointState.Failed => Fail,
                IoTestPointState.Review => Attention,
                _ => Muted
            };
            return new[]
            {
                new ReportCell(rowNumber.ToString(CultureInfo.InvariantCulture), IoFatReportFontKind.Mono, 5.35d, Ink, 1),
                new ReportCell(point.SignalName, IoFatReportFontKind.Bold, 5.65d, Ink, 3),
                new ReportCell(point.ObjectReference, IoFatReportFontKind.Mono, 5.15d, Ink, 3),
                new ReportCell($"ON {point.ExpectedOnText} ({point.ExpectedOnRaw})\nOFF {point.ExpectedOffText} ({point.ExpectedOffRaw})", IoFatReportFontKind.Regular, 5.35d, Ink, 3),
                new ReportCell(EvidenceText(point.Runtime.OnEvidence), IoFatReportFontKind.Regular, 5.05d, point.Runtime.OnEvidence == null ? Muted : Pass, 4),
                new ReportCell(EvidenceText(point.Runtime.OffEvidence), IoFatReportFontKind.Regular, 5.05d, point.Runtime.OffEvidence == null ? Muted : Pass, 4),
                new ReportCell(point.Runtime.State.ToString().ToUpperInvariant(), IoFatReportFontKind.Bold, 5.45d, stateColor, 2),
                new ReportCell(point.Runtime.StatusReason, IoFatReportFontKind.Regular, 5.2d, Ink, 3)
            };
        }

        private static double EstimateRowHeight(IReadOnlyList<ReportCell> cells)
        {
            var widths = ColumnWidths();
            var maximum = 1;
            for (var index = 0; index < cells.Count; index++)
                maximum = Math.Max(maximum, WrapText(cells[index].Text, widths[index] - 8d, cells[index].FontSize, cells[index].MaxLines).Count);
            return Math.Max(18d, 8d + (maximum * 7.15d));
        }

        private static double[] ColumnWidths() => new[] { 24d, 108d, 165d, 78d, 119d, 119d, 52d, 117d };

        private void DrawEmptyProjectNotice()
        {
            Ensure(48d);
            _page.RoundRect(Margin, _cursorY, ContentWidth, 42d, 5d, SoftAttention, Border, 0.7d);
            _page.Text(Margin + 12d, _cursorY - 25d, ContentWidth - 24d, "No IED test plan is present in this project.", IoFatReportFontKind.Bold, 9d, Attention);
            _cursorY -= 52d;
        }
    }

    private sealed record ReportCell(string Text, IoFatReportFontKind Font, double FontSize, IoFatReportColor Color, int MaxLines);

    private sealed class PageBuilder
    {
        public List<IoFatReportCommand> Commands { get; } = new();
        public void Text(double x, double baselineY, double width, string text, IoFatReportFontKind font, double size, IoFatReportColor color)
        {
            var safe = SanitizeReportText(text);
            if (safe.Length > 0)
                Commands.Add(new IoFatReportTextCommand(x, baselineY, Math.Max(4d, width), safe, font, size, color));
        }
        public void Line(double x1, double y1, double x2, double y2, IoFatReportColor stroke, double width) => Commands.Add(new IoFatReportLineCommand(x1, y1, x2, y2, stroke, width));
        public void Rect(double x, double top, double width, double height, IoFatReportColor fill, IoFatReportColor stroke, double lineWidth) => Commands.Add(new IoFatReportRectCommand(x, top, width, height, 0d, fill, stroke, lineWidth));
        public void RoundRect(double x, double top, double width, double height, double radius, IoFatReportColor fill, IoFatReportColor stroke, double lineWidth) => Commands.Add(new IoFatReportRectCommand(x, top, width, height, radius, fill, stroke, lineWidth));
    }

    private readonly record struct ProjectCounts(int Passed, int Review, int Failed, int Pending);
    private static List<IoTestPointPlan> ReportPoints(IoTestIedPlan ied) => ied.TestPoints.Where(point => point.TestEnabled).ToList();

    private static ProjectCounts Counts(IoTestProject project)
    {
        var points = project.Ieds.SelectMany(ReportPoints).ToList();
        var passed = points.Count(point => point.Runtime.State == IoTestPointState.Passed);
        var review = points.Count(point => point.Runtime.State == IoTestPointState.Review);
        var failed = points.Count(point => point.Runtime.State == IoTestPointState.Failed);
        return new ProjectCounts(passed, review, failed, Math.Max(0, points.Count - passed - review - failed));
    }

    private static string ResolveOverallTone(ProjectCounts counts)
    {
        if (counts.Failed > 0) return "FAILED";
        if (counts.Review > 0) return "REVIEW";
        if (counts.Pending > 0) return "IN PROGRESS";
        return counts.Passed > 0 ? "PASSED" : "NOT STARTED";
    }

    private static IoFatReportColor ResolveToneColor(string tone) => tone switch { "PASSED" => Pass, "FAILED" => Fail, "REVIEW" => Attention, _ => BrandBlue };
    private static IoFatReportColor ResolveToneBackground(string tone) => tone switch { "PASSED" => SoftPass, "FAILED" => SoftFail, "REVIEW" => SoftAttention, _ => SoftBlue };

    private static string EvidenceText(IoTestTransitionEvidence? evidence)
    {
        if (evidence == null) return "-";
        var ied = evidence.IedTimestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture) ?? "not supplied";
        return $"IED {ied}\nARSAS {evidence.CapturedAt:yyyy-MM-dd HH:mm:ss.fff zzz}\n{evidence.RawValue} | {evidence.Quality} | {evidence.AcquisitionSource}";
    }

    private static IReadOnlyList<string> WrapText(string? value, double width, double fontSize, int maxLines)
    {
        var input = (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(input)) return new[] { "-" };
        var charsPerLine = Math.Max(7, (int)Math.Floor(width / Math.Max(2.4d, fontSize * 0.49d)));
        var lines = new List<string>();
        var truncated = false;
        foreach (var paragraphValue in input.Split('\n'))
        {
            var paragraph = SanitizeReportText(paragraphValue);
            if (paragraph.Length == 0) paragraph = "-";
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new StringBuilder();
            foreach (var originalWord in words)
            {
                var word = originalWord;
                while (word.Length > charsPerLine)
                {
                    if (current.Length > 0)
                    {
                        lines.Add(current.ToString()); current.Clear();
                        if (lines.Count >= maxLines) { truncated = true; break; }
                    }
                    lines.Add(word[..charsPerLine]); word = word[charsPerLine..];
                    if (lines.Count >= maxLines) { truncated = word.Length > 0; break; }
                }
                if (lines.Count >= maxLines) break;
                if (current.Length == 0) current.Append(word);
                else if (current.Length + 1 + word.Length <= charsPerLine) current.Append(' ').Append(word);
                else
                {
                    lines.Add(current.ToString()); current.Clear().Append(word);
                    if (lines.Count >= maxLines) { truncated = true; break; }
                }
            }
            if (lines.Count >= maxLines) break;
            if (current.Length > 0) lines.Add(current.ToString());
            if (lines.Count >= maxLines) { truncated = true; break; }
        }
        if (lines.Count == 0) lines.Add("-");
        if (lines.Count > maxLines) lines = lines.Take(maxLines).ToList();
        if (truncated && lines[^1].Length > 3) lines[^1] = lines[^1][..Math.Max(0, lines[^1].Length - 3)] + "...";
        return lines;
    }

    internal static string SanitizeReportText(string? value)
    {
        var normalized = (value ?? string.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(normalized)) return "-";
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            builder.Append(character switch
            {
                '\u2013' or '\u2014' or '\u2212' => '-',
                '\u2192' => '>',
                '\u2190' => '<',
                '\u00B7' => '|',
                '\u00A0' => ' ',
                >= ' ' and <= '~' => character,
                _ => ' '
            });
        }
        return builder.ToString().Trim();
    }

    private static string Clean(string? value)
    {
        var normalized = (value ?? string.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "-" : normalized;
    }
    private static string ShortHash(string? value) { var clean = Clean(value); return clean.Length <= 16 ? clean : clean[..16]; }
    private static string Truncate(string? value, int maximum) { var clean = Clean(value); return clean.Length <= maximum || maximum <= 3 ? clean : clean[..(maximum - 3)] + "..."; }
    private static IoFatReportColor Color(string hex) => IoFatReportColor.FromHex(hex);
}
