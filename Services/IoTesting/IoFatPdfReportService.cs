// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0
//
// Native PDF primitives adapted from the project-owned ARIEC60870 PDF engine.
// The IO FAT layout is purpose-built for ARSAS IEC 61850 evidence reports.

using System.Globalization;
using System.Text;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Dependency-free native PDF 1.4 writer for ARSAS IO List FAT evidence.
///
/// The implementation deliberately supports only the primitives needed by this
/// report: built-in Type 1 fonts, vector rectangles and lines, wrapped text,
/// paged tables, a cross-reference table, and document metadata. It does not
/// use a browser, HTML conversion, printer driver, or third-party PDF package.
/// </summary>
public static class IoFatPdfReportService
{
    private const float PageWidth = 842f; // A4 landscape in PDF points.
    private const float PageHeight = 595f;
    private const float Margin = 30f;
    private const float HeaderBottom = 500f;
    private const float ContentTop = 484f;
    private const float ContentBottom = 55f;
    private const float ContentWidth = PageWidth - (Margin * 2f);

    private static readonly PdfColor BrandNavy = PdfColor.FromHex("0F172A");
    private static readonly PdfColor BrandBlue = PdfColor.FromHex("2563EB");
    private static readonly PdfColor SoftBlue = PdfColor.FromHex("EFF6FF");
    private static readonly PdfColor SoftSlate = PdfColor.FromHex("F8FAFC");
    private static readonly PdfColor Border = PdfColor.FromHex("DDE7F3");
    private static readonly PdfColor SoftLine = PdfColor.FromHex("EEF2F7");
    private static readonly PdfColor Muted = PdfColor.FromHex("64748B");
    private static readonly PdfColor Ink = PdfColor.FromHex("111827");
    private static readonly PdfColor White = PdfColor.FromHex("FFFFFF");
    private static readonly PdfColor Pass = PdfColor.FromHex("15803D");
    private static readonly PdfColor Attention = PdfColor.FromHex("B45309");
    private static readonly PdfColor Fail = PdfColor.FromHex("B91C1C");
    private static readonly PdfColor SoftPass = PdfColor.FromHex("F0FDF4");
    private static readonly PdfColor SoftAttention = PdfColor.FromHex("FFFBEB");
    private static readonly PdfColor SoftFail = PdfColor.FromHex("FEF2F2");

    public static byte[] Generate(IoTestProject project, DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var created = generatedAt ?? DateTimeOffset.Now;
        var pages = new Renderer(project, created).Render();
        return NativePdfDocument.Build(pages, project, created);
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

    private sealed class Renderer
    {
        private readonly IoTestProject _project;
        private readonly DateTimeOffset _created;
        private readonly List<PdfPageBuffer> _pages = new();
        private PdfPageBuffer _page = null!;
        private float _cursorY;

        public Renderer(IoTestProject project, DateTimeOffset created)
        {
            _project = project;
            _created = created;
        }

        public IReadOnlyList<PdfPageBuffer> Render()
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

            return _pages;
        }

        private void NewPage()
        {
            _page = new PdfPageBuffer(PageWidth, PageHeight);
            _pages.Add(_page);
            _cursorY = ContentTop;
        }

        private void Ensure(float requiredHeight)
        {
            if (_cursorY - requiredHeight < ContentBottom)
                NewPage();
        }

        private void DrawPageChrome(PdfPageBuffer page, int pageNumber, int totalPages)
        {
            var counts = Counts(_project);
            var tone = ResolveOverallTone(counts);
            var toneColor = ResolveToneColor(tone);
            var toneBackground = ResolveToneBackground(tone);

            page.Line(Margin, HeaderBottom, PageWidth - Margin, HeaderBottom, Border, 0.8f);
            page.Text(Margin, 562f, "ARSAS IO LIST TESTING", PdfFont.Bold, 7.2f, Muted);
            page.Text(Margin, 541f, "IEC 61850 FAT Evidence Report", PdfFont.Bold, 20.2f, BrandNavy);
            page.Text(
                Margin,
                523f,
                "Ordered OFF > ON > OFF transition evidence with IED and ARSAS timestamps.",
                PdfFont.Regular,
                7.3f,
                Muted);

            const float cardWidth = 142f;
            const float cardHeight = 58f;
            var cardX = PageWidth - Margin - cardWidth;
            const float cardTop = 566f;
            page.RoundRect(cardX, cardTop, cardWidth, cardHeight, 5f, toneBackground, toneColor, 0.8f);
            page.Text(cardX + 10f, cardTop - 15f, "PROJECT STATUS", PdfFont.Bold, 6.2f, Muted);
            page.Text(cardX + 10f, cardTop - 35f, tone, PdfFont.Bold, 16.4f, toneColor);
            page.Text(cardX + 10f, cardTop - 49f, Truncate(_project.ProjectId, 28), PdfFont.Regular, 5.8f, Muted);

            page.Line(Margin, 42f, PageWidth - Margin, 42f, Border, 0.6f);
            page.Text(
                Margin,
                24f,
                $"Generated {_created:yyyy-MM-dd HH:mm:ss zzz}  |  Project {_project.ProjectId}  |  Workbook SHA256 {ShortHash(_project.SourceWorkbookSha256)}",
                PdfFont.Regular,
                6.2f,
                Muted);
            page.Text(PageWidth - Margin - 72f, 24f, $"Page {pageNumber} / {totalPages}", PdfFont.Regular, 6.2f, Muted);
        }

        private void DrawExecutiveSummary()
        {
            var counts = Counts(_project);
            const float height = 104f;
            Ensure(height + 12f);

            _page.RoundRect(Margin, _cursorY, ContentWidth, height, 5f, SoftSlate, Border, 0.8f);
            _page.Text(Margin + 13f, _cursorY - 19f, "Project Evidence Summary", PdfFont.Bold, 11.2f, BrandNavy);
            _page.Text(Margin + 13f, _cursorY - 36f, $"{Clean(_project.ProjectName)}  |  {Clean(_project.ProjectId)}", PdfFont.Bold, 8.2f, Ink);
            _page.Text(Margin + 13f, _cursorY - 51f, $"Source: {Clean(_project.SourceWorkbookName)}", PdfFont.Regular, 6.8f, Muted);
            _page.Text(Margin + 13f, _cursorY - 64f, $"Workbook SHA-256: {Clean(_project.SourceWorkbookSha256)}", PdfFont.Mono, 5.8f, Muted);

            const float metricTop = 86f;
            const float gap = 7f;
            var metricWidth = (ContentWidth - 26f - (gap * 5f)) / 6f;
            var x = Margin + 13f;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "IED", _project.Ieds.Count.ToString(CultureInfo.InvariantCulture), BrandBlue, SoftBlue);
            x += metricWidth + gap;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "SIGNALS", _project.SignalCount.ToString(CultureInfo.InvariantCulture), BrandNavy, White);
            x += metricWidth + gap;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "PASS", counts.Passed.ToString(CultureInfo.InvariantCulture), Pass, SoftPass);
            x += metricWidth + gap;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "REVIEW", counts.Review.ToString(CultureInfo.InvariantCulture), Attention, SoftAttention);
            x += metricWidth + gap;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "FAIL", counts.Failed.ToString(CultureInfo.InvariantCulture), Fail, SoftFail);
            x += metricWidth + gap;
            DrawMetric(x, _cursorY - metricTop, metricWidth, "PENDING", counts.Pending.ToString(CultureInfo.InvariantCulture), Muted, White);

            _cursorY -= height + 12f;
        }

        private void DrawMetric(float x, float top, float width, string label, string value, PdfColor color, PdfColor background)
        {
            _page.RoundRect(x, top, width, 29f, 4f, background, Border, 0.55f);
            _page.Text(x + 7f, top - 11f, label, PdfFont.Bold, 5.3f, Muted);
            _page.Text(x + 7f, top - 23f, value, PdfFont.Bold, 9.2f, color);
        }

        private void DrawIedSection(IoTestIedPlan ied)
        {
            Ensure(82f);
            DrawIedHeader(ied, continued: false);
            DrawTableHeader();

            var rowNumber = 0;
            foreach (var point in ied.TestPoints)
            {
                rowNumber++;
                var cells = BuildCells(point, rowNumber);
                var rowHeight = EstimateRowHeight(cells);
                if (_cursorY - rowHeight < ContentBottom)
                {
                    NewPage();
                    DrawIedHeader(ied, continued: true);
                    DrawTableHeader();
                }
                DrawRow(cells, rowHeight);
            }

            if (ied.TestPoints.Count == 0)
            {
                Ensure(34f);
                _page.RoundRect(Margin, _cursorY, ContentWidth, 28f, 4f, SoftSlate, Border, 0.6f);
                _page.Text(Margin + 10f, _cursorY - 18f, "No IO-list signals are available for this IED.", PdfFont.Regular, 7f, Muted);
                _cursorY -= 38f;
            }

            _cursorY -= 11f;
        }

        private void DrawIedHeader(IoTestIedPlan ied, bool continued)
        {
            var title = continued ? $"{ied.IedName} (continued)" : ied.IedName;
            var pending = Math.Max(0, ied.TestPoints.Count - ied.PassedCount - ied.ReviewCount - ied.TestPoints.Count(point => point.Runtime.State == IoTestPointState.Failed));
            const float height = 48f;
            _page.RoundRect(Margin, _cursorY, ContentWidth, height, 5f, White, Border, 0.7f);
            _page.Rect(Margin, _cursorY, 4f, height, BrandBlue, BrandBlue, 0f);
            _page.Text(Margin + 13f, _cursorY - 17f, Clean(title), PdfFont.Bold, 10.2f, BrandNavy);
            _page.Text(
                Margin + 13f,
                _cursorY - 33f,
                $"{Clean(ied.IpAddress)}  |  {Clean(ied.IedRole)}  |  {Clean(ied.Location)}  |  {Clean(ied.VoltageLevel)}  |  {Clean(ied.Switchgear)}",
                PdfFont.Regular,
                6.3f,
                Muted);
            _page.Text(
                PageWidth - Margin - 218f,
                _cursorY - 18f,
                $"{ied.TestPoints.Count} signals  |  {ied.PassedCount} PASS  |  {ied.ReviewCount} review  |  {pending} pending",
                PdfFont.Bold,
                6.1f,
                BrandBlue);
            _cursorY -= height + 7f;
        }

        private void DrawTableHeader()
        {
            Ensure(22f);
            var widths = ColumnWidths();
            var headers = new[]
            {
                "#", "Signal", "IEC 61850 reference", "Expected ON / OFF", "ON evidence", "OFF evidence", "Result", "Reason"
            };
            var x = Margin;
            const float height = 19f;
            for (var index = 0; index < headers.Length; index++)
            {
                _page.Rect(x, _cursorY, widths[index], height, SoftBlue, Border, 0.45f);
                _page.Text(x + 4f, _cursorY - 12.5f, headers[index], PdfFont.Bold, 5.55f, BrandBlue);
                x += widths[index];
            }
            _cursorY -= height;
        }

        private void DrawRow(IReadOnlyList<ReportCell> cells, float rowHeight)
        {
            var widths = ColumnWidths();
            var x = Margin;
            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                _page.Rect(x, _cursorY, widths[index], rowHeight, White, SoftLine, 0.35f);
                var lines = WrapText(cell.Text, widths[index] - 8f, cell.FontSize, cell.MaxLines);
                var y = _cursorY - 8.5f;
                foreach (var line in lines)
                {
                    _page.Text(x + 4f, y, line, cell.Font, cell.FontSize, cell.Color);
                    y -= cell.FontSize + 1.35f;
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
                new ReportCell(rowNumber.ToString(CultureInfo.InvariantCulture), PdfFont.Mono, 5.35f, Ink, 1),
                new ReportCell(point.SignalName, PdfFont.Bold, 5.65f, Ink, 3),
                new ReportCell(point.ObjectReference, PdfFont.Mono, 5.15f, Ink, 3),
                new ReportCell($"ON {point.ExpectedOnText} ({point.ExpectedOnRaw})\nOFF {point.ExpectedOffText} ({point.ExpectedOffRaw})", PdfFont.Regular, 5.35f, Ink, 3),
                new ReportCell(EvidenceText(point.Runtime.OnEvidence), PdfFont.Regular, 5.05f, Ink, 4),
                new ReportCell(EvidenceText(point.Runtime.OffEvidence), PdfFont.Regular, 5.05f, Ink, 4),
                new ReportCell(point.Runtime.State.ToString().ToUpperInvariant(), PdfFont.Bold, 5.45f, stateColor, 2),
                new ReportCell(point.Runtime.StatusReason, PdfFont.Regular, 5.2f, Ink, 3)
            };
        }

        private static float EstimateRowHeight(IReadOnlyList<ReportCell> cells)
        {
            var widths = ColumnWidths();
            var maximum = 1;
            for (var index = 0; index < cells.Count; index++)
            {
                var lineCount = WrapText(cells[index].Text, widths[index] - 8f, cells[index].FontSize, cells[index].MaxLines).Count;
                maximum = Math.Max(maximum, lineCount);
            }
            return Math.Max(18f, 8f + (maximum * 7.15f));
        }

        private static float[] ColumnWidths()
            => new[] { 24f, 108f, 165f, 78f, 119f, 119f, 52f, 117f };

        private void DrawEmptyProjectNotice()
        {
            Ensure(48f);
            _page.RoundRect(Margin, _cursorY, ContentWidth, 42f, 5f, SoftAttention, Border, 0.7f);
            _page.Text(Margin + 12f, _cursorY - 25f, "No IED test plan is present in this project.", PdfFont.Bold, 9f, Attention);
            _cursorY -= 52f;
        }
    }

    private sealed record ReportCell(string Text, PdfFont Font, float FontSize, PdfColor Color, int MaxLines);

    private sealed class PdfPageBuffer
    {
        private readonly StringBuilder _operations = new();

        public PdfPageBuffer(float width, float height)
        {
            Width = width;
            Height = height;
        }

        public float Width { get; }
        public float Height { get; }
        public string Content => _operations.ToString();

        public void Text(float x, float baselineY, string text, PdfFont font, float size, PdfColor color)
        {
            var safe = SanitizePdfText(text);
            if (safe.Length == 0)
                return;

            _operations.Append("BT ")
                .Append(color.FillOperation()).Append(' ')
                .Append('/').Append(font.ResourceName()).Append(' ').Append(Number(size)).Append(" Tf ")
                .Append("1 0 0 1 ").Append(Number(x)).Append(' ').Append(Number(baselineY)).Append(" Tm ")
                .Append('(').Append(EscapeLiteral(safe)).Append(") Tj ET\n");
        }

        public void Line(float x1, float y1, float x2, float y2, PdfColor stroke, float width)
        {
            _operations.Append(Number(width)).Append(" w ")
                .Append(stroke.StrokeOperation()).Append(' ')
                .Append(Number(x1)).Append(' ').Append(Number(y1)).Append(" m ")
                .Append(Number(x2)).Append(' ').Append(Number(y2)).Append(" l S\n");
        }

        public void Rect(float x, float top, float width, float height, PdfColor fill, PdfColor stroke, float lineWidth)
        {
            var y = top - height;
            if (lineWidth <= 0f || fill.Equals(stroke))
            {
                _operations.Append(fill.FillOperation()).Append(' ')
                    .Append(Number(x)).Append(' ').Append(Number(y)).Append(' ')
                    .Append(Number(width)).Append(' ').Append(Number(height)).Append(" re f\n");
                return;
            }

            _operations.Append(Number(lineWidth)).Append(" w ")
                .Append(fill.FillOperation()).Append(' ')
                .Append(stroke.StrokeOperation()).Append(' ')
                .Append(Number(x)).Append(' ').Append(Number(y)).Append(' ')
                .Append(Number(width)).Append(' ').Append(Number(height)).Append(" re B\n");
        }

        public void RoundRect(float x, float top, float width, float height, float radius, PdfColor fill, PdfColor stroke, float lineWidth)
        {
            if (radius <= 0f)
            {
                Rect(x, top, width, height, fill, stroke, lineWidth);
                return;
            }

            var y = top - height;
            var r = Math.Min(radius, Math.Min(width, height) / 2f);
            var c = r * 0.55228475f;

            if (lineWidth > 0f)
                _operations.Append(Number(lineWidth)).Append(" w ");
            _operations.Append(fill.FillOperation()).Append(' ');
            if (lineWidth > 0f)
                _operations.Append(stroke.StrokeOperation()).Append(' ');

            _operations.Append(Number(x + r)).Append(' ').Append(Number(y)).Append(" m ")
                .Append(Number(x + width - r)).Append(' ').Append(Number(y)).Append(" l ")
                .Append(Number(x + width - r + c)).Append(' ').Append(Number(y)).Append(' ')
                .Append(Number(x + width)).Append(' ').Append(Number(y + r - c)).Append(' ')
                .Append(Number(x + width)).Append(' ').Append(Number(y + r)).Append(" c ")
                .Append(Number(x + width)).Append(' ').Append(Number(y + height - r)).Append(" l ")
                .Append(Number(x + width)).Append(' ').Append(Number(y + height - r + c)).Append(' ')
                .Append(Number(x + width - r + c)).Append(' ').Append(Number(y + height)).Append(' ')
                .Append(Number(x + width - r)).Append(' ').Append(Number(y + height)).Append(" c ")
                .Append(Number(x + r)).Append(' ').Append(Number(y + height)).Append(" l ")
                .Append(Number(x + r - c)).Append(' ').Append(Number(y + height)).Append(' ')
                .Append(Number(x)).Append(' ').Append(Number(y + height - r + c)).Append(' ')
                .Append(Number(x)).Append(' ').Append(Number(y + height - r)).Append(" c ")
                .Append(Number(x)).Append(' ').Append(Number(y + r)).Append(" l ")
                .Append(Number(x)).Append(' ').Append(Number(y + r - c)).Append(' ')
                .Append(Number(x + r - c)).Append(' ').Append(Number(y)).Append(' ')
                .Append(Number(x + r)).Append(' ').Append(Number(y))
                .Append(lineWidth > 0f ? " c B\n" : " c f\n");
        }
    }

    private static class NativePdfDocument
    {
        public static byte[] Build(
            IReadOnlyList<PdfPageBuffer> pages,
            IoTestProject project,
            DateTimeOffset created)
        {
            if (pages.Count == 0)
                throw new InvalidOperationException("At least one PDF page is required.");

            var objects = new List<byte[]>();
            int AddObject(string body)
            {
                objects.Add(Encoding.ASCII.GetBytes(body));
                return objects.Count;
            }

            var catalogId = AddObject("<< /Type /Catalog /Pages 2 0 R >>");
            var pagesId = AddObject("__PAGES__");
            var fontRegularId = AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            var fontBoldId = AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
            var fontMonoId = AddObject("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");
            var pageIds = new List<int>();

            foreach (var page in pages)
            {
                var contentBytes = Encoding.ASCII.GetBytes(page.Content);
                var contentId = AddObject(
                    $"<< /Length {contentBytes.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n{page.Content}endstream");
                var pageId = AddObject(
                    $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {Number(page.Width)} {Number(page.Height)}] " +
                    $"/Resources << /Font << /F1 {fontRegularId} 0 R /F2 {fontBoldId} 0 R /F3 {fontMonoId} 0 R >> >> " +
                    $"/Contents {contentId} 0 R >>");
                pageIds.Add(pageId);
            }

            var title = $"{project.ProjectName} - ARSAS IO FAT Evidence Report";
            var infoId = AddObject(
                $"<< /Title ({EscapeLiteral(SanitizePdfText(title))}) " +
                "/Author (ARSAS) " +
                "/Creator (ARSAS Native PDF Engine, ported from ARIEC60870) " +
                "/Producer (ARSAS Native PDF Engine) " +
                $"/CreationDate ({PdfDate(created)}) >>");

            objects[pagesId - 1] = Encoding.ASCII.GetBytes(
                $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Count} >>");

            using var stream = new MemoryStream();
            WriteAscii(stream, "%PDF-1.4\n%ARSAS native PDF\n");
            var offsets = new long[objects.Count + 1];
            for (var index = 0; index < objects.Count; index++)
            {
                offsets[index + 1] = stream.Position;
                WriteAscii(stream, $"{index + 1} 0 obj\n");
                stream.Write(objects[index], 0, objects[index].Length);
                WriteAscii(stream, "\nendobj\n");
            }

            var xrefOffset = stream.Position;
            WriteAscii(stream, $"xref\n0 {objects.Count + 1}\n");
            WriteAscii(stream, "0000000000 65535 f \n");
            for (var index = 1; index < offsets.Length; index++)
                WriteAscii(stream, offsets[index].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");

            WriteAscii(
                stream,
                $"trailer\n<< /Size {objects.Count + 1} /Root {catalogId} 0 R /Info {infoId} 0 R >>\n" +
                $"startxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
            return stream.ToArray();
        }

        private static void WriteAscii(Stream stream, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    private readonly record struct ProjectCounts(int Passed, int Review, int Failed, int Pending);

    private static ProjectCounts Counts(IoTestProject project)
    {
        var points = project.Ieds.SelectMany(ied => ied.TestPoints).ToList();
        var passed = points.Count(point => point.Runtime.State == IoTestPointState.Passed);
        var review = points.Count(point => point.Runtime.State == IoTestPointState.Review);
        var failed = points.Count(point => point.Runtime.State == IoTestPointState.Failed);
        return new ProjectCounts(passed, review, failed, Math.Max(0, points.Count - passed - review - failed));
    }

    private static string ResolveOverallTone(ProjectCounts counts)
    {
        if (counts.Failed > 0)
            return "FAILED";
        if (counts.Review > 0)
            return "REVIEW";
        if (counts.Pending > 0)
            return "IN PROGRESS";
        return counts.Passed > 0 ? "PASSED" : "NOT STARTED";
    }

    private static PdfColor ResolveToneColor(string tone) => tone switch
    {
        "PASSED" => Pass,
        "FAILED" => Fail,
        "REVIEW" => Attention,
        _ => BrandBlue
    };

    private static PdfColor ResolveToneBackground(string tone) => tone switch
    {
        "PASSED" => SoftPass,
        "FAILED" => SoftFail,
        "REVIEW" => SoftAttention,
        _ => SoftBlue
    };

    private static string EvidenceText(IoTestTransitionEvidence? evidence)
    {
        if (evidence == null)
            return "-";
        var ied = evidence.IedTimestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture) ?? "not supplied";
        return $"IED {ied}\nARSAS {evidence.CapturedAt:yyyy-MM-dd HH:mm:ss.fff zzz}\n{evidence.RawValue} | {evidence.Quality} | {evidence.AcquisitionSource}";
    }

    private static IReadOnlyList<string> WrapText(string? value, float width, float fontSize, int maxLines)
    {
        var input = (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(input))
            return new[] { "-" };

        var charsPerLine = Math.Max(7, (int)Math.Floor(width / Math.Max(2.4f, fontSize * 0.49f)));
        var lines = new List<string>();
        var truncated = false;

        foreach (var paragraphValue in input.Split('\n'))
        {
            var paragraph = SanitizePdfText(paragraphValue);
            if (paragraph.Length == 0)
                paragraph = "-";
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
                        if (lines.Count >= maxLines)
                        {
                            truncated = true;
                            break;
                        }
                    }
                    lines.Add(word[..charsPerLine]);
                    word = word[charsPerLine..];
                    if (lines.Count >= maxLines)
                    {
                        truncated = word.Length > 0;
                        break;
                    }
                }
                if (lines.Count >= maxLines)
                    break;

                if (current.Length == 0)
                    current.Append(word);
                else if (current.Length + 1 + word.Length <= charsPerLine)
                    current.Append(' ').Append(word);
                else
                {
                    lines.Add(current.ToString());
                    current.Clear().Append(word);
                    if (lines.Count >= maxLines)
                    {
                        truncated = true;
                        break;
                    }
                }
            }

            if (lines.Count >= maxLines)
                break;
            if (current.Length > 0)
                lines.Add(current.ToString());
            if (lines.Count >= maxLines)
            {
                truncated = true;
                break;
            }
        }

        if (lines.Count == 0)
            lines.Add("-");
        if (lines.Count > maxLines)
            lines = lines.Take(maxLines).ToList();
        if (truncated && lines[^1].Length > 3)
            lines[^1] = lines[^1][..Math.Max(0, lines[^1].Length - 3)] + "...";
        return lines;
    }

    private static string Clean(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "-" : normalized;
    }

    private static string ShortHash(string? value)
    {
        var clean = Clean(value);
        return clean.Length <= 16 ? clean : clean[..16];
    }

    private static string Truncate(string? value, int maximum)
    {
        var clean = Clean(value);
        if (clean.Length <= maximum || maximum <= 3)
            return clean;
        return clean[..(maximum - 3)] + "...";
    }

    private static string SanitizePdfText(string? value)
    {
        var input = Clean(value);
        var builder = new StringBuilder(input.Length);
        foreach (var character in input)
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

    private static string EscapeLiteral(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string PdfDate(DateTimeOffset value)
        => "D:" + value.ToLocalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static string Number(float value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private readonly struct PdfColor : IEquatable<PdfColor>
    {
        public PdfColor(float red, float green, float blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
        }

        public float Red { get; }
        public float Green { get; }
        public float Blue { get; }

        public static PdfColor FromHex(string hex)
        {
            var value = hex.StartsWith("#", StringComparison.Ordinal) ? hex[1..] : hex;
            if (value.Length != 6)
                throw new ArgumentException("PDF color must be a six-digit RGB hex value.", nameof(hex));
            return new PdfColor(
                Convert.ToInt32(value[..2], 16) / 255f,
                Convert.ToInt32(value.Substring(2, 2), 16) / 255f,
                Convert.ToInt32(value.Substring(4, 2), 16) / 255f);
        }

        public string FillOperation() => $"{Number(Red)} {Number(Green)} {Number(Blue)} rg";
        public string StrokeOperation() => $"{Number(Red)} {Number(Green)} {Number(Blue)} RG";
        public bool Equals(PdfColor other)
            => Math.Abs(Red - other.Red) < 0.0001f && Math.Abs(Green - other.Green) < 0.0001f && Math.Abs(Blue - other.Blue) < 0.0001f;
        public override bool Equals(object? obj) => obj is PdfColor other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Red, Green, Blue);
    }

    private enum PdfFont
    {
        Regular,
        Bold,
        Mono
    }

    private static string ResourceName(this PdfFont font) => font switch
    {
        PdfFont.Bold => "F2",
        PdfFont.Mono => "F3",
        _ => "F1"
    };
}
