// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Customer-facing IEC 61850 FAT report with document control, exact telegram
/// traceability, relay timestamps, acceptance summary, and handover sign-off.
/// </summary>
internal static class IoFatExecutiveReportLayoutEngine
{
    public const double PageWidth = 842d;
    public const double PageHeight = 595d;

    private const double Margin = 30d;
    private const double HeaderBottom = 496d;
    private const double ContentTop = 480d;
    private const double ContentBottom = 55d;
    private const double ContentWidth = PageWidth - (Margin * 2d);

    private static readonly IoFatReportColor Navy = Color("0F172A");
    private static readonly IoFatReportColor Blue = Color("2563EB");
    private static readonly IoFatReportColor SoftBlue = Color("EFF6FF");
    private static readonly IoFatReportColor DocumentFill = Color("F8FAFC");
    private static readonly IoFatReportColor DocumentBorder = Color("CBD5E1");
    private static readonly IoFatReportColor Border = Color("D9E4F0");
    private static readonly IoFatReportColor SoftLine = Color("EDF2F7");
    private static readonly IoFatReportColor Muted = Color("64748B");
    private static readonly IoFatReportColor Ink = Color("1F2937");
    private static readonly IoFatReportColor White = Color("FFFFFF");
    private static readonly IoFatReportColor Pass = Color("15803D");
    private static readonly IoFatReportColor Attention = Color("B45309");
    private static readonly IoFatReportColor Fail = Color("B91C1C");
    private static readonly IoFatReportColor SoftAttention = Color("FFFBEB");

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
            foreach (var ied in _project.Ieds)
                DrawIedSection(ied);

            if (_project.Ieds.Count == 0)
                DrawEmptyProjectNotice();
            else
                DrawCloseout();

            var totalPages = _pages.Count;
            for (var index = 0; index < totalPages; index++)
                DrawPageChrome(_pages[index], index + 1, totalPages);

            return new IoFatReportLayoutPlan(
                _project.ProjectId,
                _created,
                _draft,
                _pages.Select((page, index) =>
                    new IoFatReportPagePlan(index + 1, PageWidth, PageHeight, page.Commands.ToArray()))
                    .ToArray());
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
            var control = _project.DocumentControl;
            var projectName = FirstNonEmpty(control.ClientProject, _project.ProjectName, _project.ProjectId);
            var documentNumber = FirstNonEmpty(
                control.PurchaserDocumentNumber,
                control.CompanyProjectDocumentNumber,
                _project.ProjectId);
            var revision = FirstNonEmpty(control.Revision, "-");
            // A generated evidence attachment is an as-tested record. Source-workbook
            // review flags remain in project diagnostics but are not customer issue status.
            var issueStatus = _draft ? "PREVIEW" : "AS TESTED";
            var supplier = FirstNonEmpty(control.SupplierName, "Supplier not stated");
            var poTitle = FirstNonEmpty(control.PurchaseOrderTitle, control.DocumentTitle);

            page.Line(Margin, HeaderBottom, PageWidth - Margin, HeaderBottom, Border, 0.8d);
            page.Text(Margin, 566d, 480d, Clean(projectName), IoFatReportFontKind.Bold, 7.2d, Muted);
            page.Text(Margin, 544d, 500d, "IEC 61850 FAT Evidence Report", IoFatReportFontKind.Bold, 16.8d, Navy);
            page.Text(Margin, 524d, 500d, "Signal-state verification with IED timestamps and event-log traceability.", IoFatReportFontKind.Regular, 8.0d, Muted);
            page.Text(Margin, 507d, 520d, BuildSupplierLine(supplier, poTitle), IoFatReportFontKind.Regular, 6.6d, Muted);

            const double cardWidth = 222d;
            const double cardHeight = 64d;
            var cardX = PageWidth - Margin - cardWidth;
            const double cardTop = 568d;

            // Document control is neutral metadata, not an alarm/status card.
            page.RoundRect(cardX, cardTop, cardWidth, cardHeight, 4d, DocumentFill, DocumentBorder, 0.7d);
            page.Text(cardX + 11d, cardTop - 14d, cardWidth - 22d, "DOCUMENT CONTROL", IoFatReportFontKind.Bold, 5.9d, Muted);
            page.Text(cardX + 11d, cardTop - 30d, cardWidth - 22d, Clean(documentNumber), IoFatReportFontKind.Bold, 8.2d, Navy);
            page.Text(cardX + 11d, cardTop - 45d, cardWidth - 22d, $"REV {Clean(revision)}  |  {issueStatus}", IoFatReportFontKind.Bold, 6.7d, Navy);
            page.Text(cardX + 11d, cardTop - 57d, cardWidth - 22d, _draft ? "NOT FOR ISSUE" : "CUSTOMER FAT RECORD", IoFatReportFontKind.Regular, 5.8d, Muted);

            page.Line(Margin, 42d, PageWidth - Margin, 42d, Border, 0.6d);
            page.Text(Margin, 24d, 540d,
                $"Generated {_created:yyyy-MM-dd HH:mm:ss zzz}  |  IED timestamp format yyyy-MM-dd HH:mm:ss.fff",
                IoFatReportFontKind.Regular, 6.1d, Muted);
            page.Text(PageWidth - Margin - 118d, 24d, 118d, $"Page {pageNumber} / {totalPages}", IoFatReportFontKind.Regular, 6.1d, Muted);
        }

        private void DrawIedSection(IoTestIedPlan ied)
        {
            var points = ReportPoints(ied);
            Ensure(76d);
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
                _page.RoundRect(Margin, _cursorY, ContentWidth, 28d, 4d, SoftAttention, Border, 0.6d);
                _page.Text(Margin + 10d, _cursorY - 18d, ContentWidth - 20d,
                    "No signal is currently selected or completed for this device.",
                    IoFatReportFontKind.Regular, 7.2d, Attention);
                _cursorY -= 38d;
            }
            _cursorY -= 10d;
        }

        private void DrawIedHeader(IoTestIedPlan ied, IReadOnlyList<IoTestPointPlan> points, bool continued)
        {
            var title = continued ? $"{ied.IedName} (continued)" : ied.IedName;
            var passed = points.Count(point => point.Runtime.State == IoTestPointState.Passed);
            var review = points.Count(point => point.Runtime.State == IoTestPointState.Review);
            var failed = points.Count(point => point.Runtime.State == IoTestPointState.Failed);
            var pending = Math.Max(0, points.Count - passed - review - failed);
            var status = $"{passed} OF {points.Count} SIGNALS PASSED";
            var statusColor = failed > 0 ? Fail : review > 0 || pending > 0 ? Attention : Pass;
            const double height = 43d;

            // A report section should read like a controlled document, not an application card.
            _page.Rect(Margin, _cursorY, 4d, height - 3d, statusColor, statusColor, 0d);
            _page.Text(Margin + 14d, _cursorY - 15d, 390d, Clean(title), IoFatReportFontKind.Bold, 10.5d, Navy);
            _page.Text(Margin + 14d, _cursorY - 32d, 500d, BuildDeviceMeta(ied), IoFatReportFontKind.Regular, 6.9d, Muted);
            _page.Text(PageWidth - Margin - 190d, _cursorY - 32d, 180d, status, IoFatReportFontKind.Bold, 7.1d, statusColor);
            _page.Line(Margin + 4d, _cursorY - height, PageWidth - Margin, _cursorY - height, Border, 0.65d);
            _cursorY -= height + 7d;
        }

        private void DrawTableHeader()
        {
            Ensure(24d);
            var widths = ColumnWidths();
            var headers = new[]
            {
                "#", "Signal", "IEC 61850 / event-log reference", "Expected state",
                "TRUE IED timestamp", "FALSE IED timestamp", "Result"
            };
            var x = Margin;
            const double height = 22d;
            for (var index = 0; index < headers.Length; index++)
            {
                _page.Rect(x, _cursorY, widths[index], height, SoftBlue, Border, 0.45d);
                _page.Text(x + 5d, _cursorY - 14.5d, widths[index] - 10d,
                    headers[index], IoFatReportFontKind.Bold, 5.8d, Blue);
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
                var lines = WrapText(cell.Text, widths[index] - 10d, cell.FontSize);
                var y = _cursorY - 10d;
                foreach (var line in lines)
                {
                    _page.Text(x + 5d, y, widths[index] - 10d, line, cell.Font, cell.FontSize, cell.Color);
                    y -= cell.FontSize + 1.7d;
                }
                x += widths[index];
            }
            _cursorY -= rowHeight;
        }

        private static ReportCell[] BuildCells(IoTestPointPlan point, int rowNumber)
        {
            var stateColor = ResolvePointColor(point.Runtime.State);
            return
            [
                new ReportCell(rowNumber.ToString(CultureInfo.InvariantCulture), IoFatReportFontKind.Mono, 5.8d, Ink),
                new ReportCell(point.SignalName, IoFatReportFontKind.Bold, 6.4d, Ink),
                new ReportCell(point.ReportIecReference, IoFatReportFontKind.Mono, 5.65d, Ink),
                new ReportCell(ExpectedStateText(point), IoFatReportFontKind.Regular, 6.0d, Ink),
                new ReportCell(RelayTime(point.Runtime.OnEvidence), IoFatReportFontKind.Mono, 5.85d, Ink),
                new ReportCell(RelayTime(point.Runtime.OffEvidence), IoFatReportFontKind.Mono, 5.85d, Ink),
                new ReportCell(ResultText(point.Runtime.State), IoFatReportFontKind.Bold, 6.3d, stateColor)
            ];
        }

        private static double EstimateRowHeight(IReadOnlyList<ReportCell> cells)
        {
            var widths = ColumnWidths();
            var maximum = 1;
            for (var index = 0; index < cells.Count; index++)
                maximum = Math.Max(maximum, WrapText(cells[index].Text, widths[index] - 10d, cells[index].FontSize).Count);
            return Math.Max(29d, 10d + (maximum * 7.8d));
        }

        private static double[] ColumnWidths() => [24d, 178d, 218d, 108d, 94d, 94d, 66d];

        private void DrawCloseout()
        {
            Ensure(116d);
            var counts = Counts(_project);
            var exceptionText = counts.Failed > 0
                ? $"{counts.Failed} failed signal(s)"
                : counts.Review > 0
                    ? $"{counts.Review} signal(s) require review"
                    : counts.Pending > 0
                        ? $"{counts.Pending} signal(s) not completed"
                        : "None - all reported signals passed";

            const double summaryHeight = 42d;
            _page.RoundRect(Margin, _cursorY, ContentWidth, summaryHeight, 5d, SoftBlue, Border, 0.7d);
            _page.Text(Margin + 12d, _cursorY - 14d, 150d, "TEST BASIS", IoFatReportFontKind.Bold, 5.8d, Muted);
            _page.Text(Margin + 12d, _cursorY - 29d, 350d,
                "Sequence: FALSE > TRUE > FALSE  |  Timestamp source: IED report/event",
                IoFatReportFontKind.Regular, 6.6d, Ink);
            _page.Text(Margin + 390d, _cursorY - 14d, 150d, "EVENT-LOG CORRELATION", IoFatReportFontKind.Bold, 5.8d, Muted);
            _page.Text(Margin + 390d, _cursorY - 29d, 365d,
                "Match key: exact IEC 61850 reference  |  Exceptions: " + exceptionText,
                IoFatReportFontKind.Regular, 6.6d, Ink);
            _cursorY -= summaryHeight + 8d;

            const double signHeight = 48d;
            var signWidth = ContentWidth / 4d;
            var labels = new[] { "TESTED BY", "CHECKED BY", "CLIENT WITNESS", "APPROVED BY" };
            for (var index = 0; index < labels.Length; index++)
            {
                var x = Margin + (index * signWidth);
                _page.Rect(x, _cursorY, signWidth, signHeight, White, Border, 0.55d);
                _page.Text(x + 8d, _cursorY - 13d, signWidth - 16d, labels[index], IoFatReportFontKind.Bold, 5.8d, Muted);
                _page.Line(x + 8d, _cursorY - 30d, x + signWidth - 8d, _cursorY - 30d, Border, 0.55d);
                _page.Text(x + 8d, _cursorY - 42d, signWidth - 16d, "Name / signature / date", IoFatReportFontKind.Regular, 5.7d, Muted);
            }
            _cursorY -= signHeight + 8d;
        }

        private void DrawEmptyProjectNotice()
        {
            Ensure(48d);
            _page.RoundRect(Margin, _cursorY, ContentWidth, 42d, 5d, SoftAttention, Border, 0.7d);
            _page.Text(Margin + 12d, _cursorY - 25d, ContentWidth - 24d,
                "No device test plan is present in this project.", IoFatReportFontKind.Bold, 9d, Attention);
            _cursorY -= 52d;
        }
    }

    private sealed record ReportCell(string Text, IoFatReportFontKind Font, double FontSize, IoFatReportColor Color);

    private sealed class PageBuilder
    {
        public List<IoFatReportCommand> Commands { get; } = new();

        public void Text(double x, double baselineY, double width, string text, IoFatReportFontKind font, double size, IoFatReportColor color)
        {
            var safe = SanitizeReportText(text);
            if (safe.Length > 0)
                Commands.Add(new IoFatReportTextCommand(x, baselineY, Math.Max(4d, width), safe, font, size, color));
        }

        public void Line(double x1, double y1, double x2, double y2, IoFatReportColor stroke, double width)
            => Commands.Add(new IoFatReportLineCommand(x1, y1, x2, y2, stroke, width));

        public void Rect(double x, double top, double width, double height, IoFatReportColor fill, IoFatReportColor stroke, double lineWidth)
            => Commands.Add(new IoFatReportRectCommand(x, top, width, height, 0d, fill, stroke, lineWidth));

        public void RoundRect(double x, double top, double width, double height, double radius, IoFatReportColor fill, IoFatReportColor stroke, double lineWidth)
            => Commands.Add(new IoFatReportRectCommand(x, top, width, height, radius, fill, stroke, lineWidth));
    }

    private readonly record struct ProjectCounts(int Passed, int Review, int Failed, int Pending);

    private static List<IoTestPointPlan> ReportPoints(IoTestIedPlan ied)
        => ied.TestPoints.Where(point => point.TestEnabled || point.Runtime.IsComplete).ToList();

    private static ProjectCounts Counts(IoTestProject project)
    {
        var points = project.Ieds.SelectMany(ReportPoints).ToList();
        var passed = points.Count(point => point.Runtime.State == IoTestPointState.Passed);
        var review = points.Count(point => point.Runtime.State == IoTestPointState.Review);
        var failed = points.Count(point => point.Runtime.State == IoTestPointState.Failed);
        return new ProjectCounts(passed, review, failed, Math.Max(0, points.Count - passed - review - failed));
    }

    private static IoFatReportColor ResolvePointColor(IoTestPointState state) => state switch
    {
        IoTestPointState.Passed => Pass,
        IoTestPointState.Failed => Fail,
        IoTestPointState.Review => Attention,
        _ => Muted
    };

    private static string ResultText(IoTestPointState state) => state switch
    {
        IoTestPointState.Passed => "PASS",
        IoTestPointState.Review => "REVIEW",
        IoTestPointState.Failed => "FAILED",
        _ => "PENDING"
    };

    private static string ExpectedStateText(IoTestPointPlan point)
    {
        var trueLabel = NormalizeExpectedLabel(point.ExpectedOnText, "ON");
        var falseLabel = NormalizeExpectedLabel(point.ExpectedOffText, "OFF");
        return $"{ExpectedLine(trueLabel, true)}\n{ExpectedLine(falseLabel, false)}";
    }

    private static string ExpectedLine(string label, bool value)
    {
        var booleanText = value ? "True" : "False";
        return string.IsNullOrWhiteSpace(label) ? booleanText.ToUpperInvariant() : $"{label} ({booleanText})";
    }

    private static string NormalizeExpectedLabel(string? value, string prefix)
    {
        var clean = Clean(value);
        foreach (var suffix in new[] { " (1)", " (0)", " (True)", " (False)" })
        {
            if (clean.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                clean = clean[..^suffix.Length].Trim();
                break;
            }
        }

        if (clean.Equals("TBA", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("NA", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("N/A", StringComparison.OrdinalIgnoreCase) ||
            clean == "-")
            return string.Empty;

        if (clean.Equals("1", StringComparison.OrdinalIgnoreCase) || clean.Equals("true", StringComparison.OrdinalIgnoreCase))
            return "ON";
        if (clean.Equals("0", StringComparison.OrdinalIgnoreCase) || clean.Equals("false", StringComparison.OrdinalIgnoreCase))
            return "OFF";

        var repeatedPrefix = prefix + " ";
        if (clean.StartsWith(repeatedPrefix, StringComparison.OrdinalIgnoreCase))
            clean = clean[repeatedPrefix.Length..].Trim();
        return clean;
    }

    private static string RelayTime(IoTestTransitionEvidence? evidence)
        => global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(evidence?.IedTimestamp, "yyyy-MM-dd\nHH:mm:ss.fff", "-");

    private static string BuildDeviceMeta(IoTestIedPlan ied)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(ied.IpAddress)) values.Add(ied.IpAddress.Trim());
        if (!string.IsNullOrWhiteSpace(ied.IedRole)) values.Add(ied.IedRole.Trim());
        if (!string.IsNullOrWhiteSpace(ied.Location)) values.Add(ied.Location.Trim());
        if (!string.IsNullOrWhiteSpace(ied.VoltageLevel)) values.Add(ied.VoltageLevel.Trim());
        if (!string.IsNullOrWhiteSpace(ied.Switchgear)) values.Add(ied.Switchgear.Trim());
        return values.Count == 0 ? "Device details not supplied" : string.Join("  |  ", values);
    }

    private static string BuildSupplierLine(string supplier, string poTitle)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(supplier)) parts.Add("Supplier: " + supplier.Trim());
        if (!string.IsNullOrWhiteSpace(poTitle) && poTitle != "-") parts.Add("Package: " + poTitle.Trim());
        return parts.Count == 0 ? string.Empty : string.Join("  |  ", parts);
    }

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
            var paragraph = SanitizeReportText(paragraphValue);
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

    internal static string SanitizeReportText(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "-";

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
        var normalized = (value ?? string.Empty)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "-" : normalized;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static IoFatReportColor Color(string hex) => IoFatReportColor.FromHex(hex);
}
