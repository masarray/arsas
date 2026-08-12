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
    private static readonly IoFatReportColor SoftPass = Color("F0FDF4");
    private static readonly IoFatReportColor SoftFail = Color("FEF2F2");

    public static IoFatReportLayoutPlan Build(IoTestProject project, DateTimeOffset created, bool draft = false, bool blankForm = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new Builder(project, created, draft, blankForm).Render();
    }

    private sealed class Builder
    {
        private readonly IoTestProject _project;
        private readonly DateTimeOffset _created;
        private readonly bool _draft;
        private readonly bool _blankForm;
        private readonly ReportAssessment _assessment;
        private readonly List<PageBuilder> _pages = new();
        private PageBuilder _page = null!;
        private double _cursorY;

        public Builder(IoTestProject project, DateTimeOffset created, bool draft, bool blankForm)
        {
            _project = project;
            _created = created;
            _draft = draft;
            _blankForm = blankForm;
            _assessment = Assess(project, draft, blankForm);
        }

        public IoFatReportLayoutPlan Render()
        {
            NewPage();
            DrawCoverAndDocumentControl();
            NewPage();
            DrawScopeAndTestBasis();
            NewPage();
            DrawExecutiveSummary();
            NewPage();
            DrawAcceptanceAndHandover();

            NewPage();
            DrawAppendixHeading();
            var scopedIeds = _project.Ieds.Where(ied => ReportPoints(ied).Count > 0).ToList();
            foreach (var ied in scopedIeds)
                DrawIedSection(ied);

            if (scopedIeds.Count == 0)
                DrawEmptyProjectNotice();

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
            var issueStatus = _assessment.IssueStatus;
            var supplier = FirstNonEmpty(control.SupplierName, "Supplier not stated");

            page.Line(Margin, HeaderBottom, PageWidth - Margin, HeaderBottom, Border, 0.8d);
            page.Text(Margin, 566d, 480d, Clean(projectName), IoFatReportFontKind.Bold, 7.2d, Muted);
            page.Text(Margin, 544d, 500d,
                _blankForm ? "IEC 61850 IFAT Test Form" : "IEC 61850 FAT Evidence Report",
                IoFatReportFontKind.Bold, 16.8d, Navy);
            page.Text(Margin, 524d, 500d,
                _blankForm
                    ? "Planned signal checks and acceptance fields for customer review before IFAT."
                    : "Signal-state verification with IED timestamps and event-log traceability.",
                IoFatReportFontKind.Regular, 8.0d, Muted);
            page.Text(Margin, 507d, 520d, BuildSupplierLine(supplier), IoFatReportFontKind.Regular, 6.6d, Muted);

            const double cardWidth = 222d;
            const double cardHeight = 64d;
            var cardX = PageWidth - Margin - cardWidth;
            const double cardTop = 568d;

            // Document control is neutral metadata, not an alarm/status card.
            page.RoundRect(cardX, cardTop, cardWidth, cardHeight, 4d, DocumentFill, DocumentBorder, 0.7d);
            page.Text(cardX + 11d, cardTop - 14d, cardWidth - 22d, "DOCUMENT CONTROL", IoFatReportFontKind.Bold, 5.9d, Muted);
            page.Text(cardX + 11d, cardTop - 30d, cardWidth - 22d, Clean(documentNumber), IoFatReportFontKind.Bold, 8.2d, Navy);
            page.Text(cardX + 11d, cardTop - 45d, cardWidth - 22d, $"REV {Clean(revision)}  |  {issueStatus}", IoFatReportFontKind.Bold, 6.7d, _assessment.StatusColor);
            page.Text(cardX + 11d, cardTop - 57d, cardWidth - 22d, _assessment.RecordClass, IoFatReportFontKind.Regular, 5.8d, _assessment.StatusColor);

            page.Line(Margin, 42d, PageWidth - Margin, 42d, Border, 0.6d);
            page.Text(Margin, 24d, 540d,
                _blankForm
                    ? $"Generated {_created:yyyy-MM-dd HH:mm:ss zzz}  |  Blank form - no executed evidence  |  Auto-generated by ARSAS IEC 61850 Protocol Tester"
                    : $"Generated {_created:yyyy-MM-dd HH:mm:ss zzz}  |  IED timestamp format yyyy-MM-dd HH:mm:ss.fff",
                IoFatReportFontKind.Regular, 6.1d, Muted);
            page.Text(PageWidth - Margin - 118d, 24d, 118d, $"Page {pageNumber} / {totalPages}", IoFatReportFontKind.Regular, 6.1d, Muted);
        }

        private void DrawCoverAndDocumentControl()
        {
            DrawSectionTitle(
                _blankForm ? "CONTROLLED IFAT TEST FORM" : "CONTROLLED FAT REPORT",
                _blankForm
                    ? "Blank form submitted for customer review of test scope, method, and acceptance fields"
                    : "Document identity, issue classification, and current execution status");

            var fill = _assessment.IsAccepted ? SoftPass : _assessment.Failed > 0 ? SoftFail : SoftAttention;
            _page.RoundRect(Margin, _cursorY, ContentWidth, 72d, 6d, fill, _assessment.StatusColor, 1d);
            _page.Text(Margin + 16d, _cursorY - 19d, 180d,
                _blankForm ? "DOCUMENT ISSUE PURPOSE" : "OVERALL REPORT STATUS",
                IoFatReportFontKind.Bold, 6.2d, Muted);
            _page.Text(Margin + 16d, _cursorY - 43d, 260d, _assessment.IssueStatus, IoFatReportFontKind.Bold, 16d, _assessment.StatusColor);
            _page.Text(Margin + 290d, _cursorY - 24d, ContentWidth - 310d,
                _blankForm
                    ? $"PLANNED SIGNALS {_assessment.Total}  |  IEDS {ScopedIedCount(_project)}  |  PANELS {PanelCount(_project)}"
                    : $"PASS {_assessment.Passed}  |  REVIEW {_assessment.Review}  |  FAILED {_assessment.Failed}  |  PENDING {_assessment.Pending}",
                IoFatReportFontKind.Bold, 8.2d, Ink);
            _page.Text(Margin + 290d, _cursorY - 46d, ContentWidth - 310d, _assessment.Warning,
                IoFatReportFontKind.Regular, 7.0d, _assessment.StatusColor);
            _cursorY -= 88d;

            DrawKeyValueBlock("DOCUMENT CONTROL",
            [
                ("Client / project", FirstNonEmpty(_project.DocumentControl.ClientProject, _project.ProjectName, _project.ProjectId)),
                ("Supplier / form owner", FirstNonEmpty(_project.DocumentControl.SupplierName, "Supplier not stated")),
                ("Purchaser document no.", FirstNonEmpty(_project.DocumentControl.PurchaserDocumentNumber, "-")),
                ("Company project document no.", FirstNonEmpty(_project.DocumentControl.CompanyProjectDocumentNumber, _project.ProjectId, "-")),
                ("Revision / source status", $"{FirstNonEmpty(_project.DocumentControl.Revision, "-")} / {FirstNonEmpty(_project.DocumentControl.IssueStatus, "not stated")}"),
                ("Source workbook", FirstNonEmpty(_project.SourceWorkbookName, _project.DocumentControl.SourceDocumentName, "-")),
                ("Source SHA-256", FirstNonEmpty(_project.SourceWorkbookSha256, "not available"))
            ]);

            _cursorY -= 8d;
            _page.Text(Margin, _cursorY - 10d, ContentWidth,
                _blankForm ? "FORM ISSUE NOTE" : "CONTROL NOTE",
                IoFatReportFontKind.Bold, 6.2d, Muted);
            _page.Text(Margin, _cursorY - 27d, ContentWidth,
                _blankForm
                    ? "This form defines the planned IFAT checks for customer review. No test execution, device conformance, or customer acceptance is implied by this blank issue."
                    : "The ARSAS result state controls this PDF classification. Workbook document-control text cannot override incomplete or failed execution evidence.",
                IoFatReportFontKind.Regular, 7.0d, Ink);
        }

        private void DrawScopeAndTestBasis()
        {
            DrawSectionTitle("1. FAT SCOPE AND TEST BASIS", "Controlled procedure layer for the detailed IEC 61850 signal evidence");

            var panelTags = _project.Ieds.Select(ied => ied.PanelTag).Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            DrawKeyValueBlock("SCOPE SUMMARY",
            [
                ("Physical panels", panelTags.Count > 0 ? $"{panelTags.Count}: {string.Join(", ", panelTags)}" : "Not identified in source workbook"),
                ("IED inventory / with enabled scope", $"{_project.Ieds.Count} / {_project.Ieds.Count(ied => ReportPoints(ied).Count > 0)}"),
                ("IEDs with no enabled signal", _project.Ieds.Count(ied => ReportPoints(ied).Count == 0).ToString(CultureInfo.InvariantCulture)),
                ("Signals in report scope", _assessment.Total.ToString(CultureInfo.InvariantCulture)),
                ("Included data type", "IEC 61850 status indication points enabled by the approved import scope"),
                ("Excluded activities", "Control commands, protection injection, GOOSE performance, interlocking, and network performance unless separately documented")
            ]);

            _cursorY -= 10d;
            DrawParagraphBlock("TEST METHOD",
                "For each enabled point ARSAS requires a trustworthy FALSE baseline, captures the transition to TRUE, and captures the return to FALSE. " +
                "The exact IEC 61850 / event-log reference is retained for correlation. Evidence is evaluated from observed order, quality, connection generation, and transition state.");
            DrawParagraphBlock("TIME AND ORDERING PRINCIPLE",
                "IED timestamps are preserved exactly as reported by the device. ARSAS capture time and sequence are the ordering authority when relay timestamps are absent, repeated, or have insufficient resolution. Identical TRUE/FALSE IED timestamps are disclosed as a timestamp-integrity warning, not silently hidden.");
            DrawParagraphBlock("SUPPLEMENTARY RELAY TESTS",
                "Time synchronization is a capability-gated relay test: record the configured SNTP/NTP, PTP/IEEE 1588, IRIG-B, or other approved source, relay TimeQuality, relay UTC, ARSAS laptop UTC, calculated offset, tolerance, and verdict. ARSAS verifies evidence but does not set the relay clock or act as an SNTP server. COMTRADE / disturbance recording is also capability-gated and applies only when a protection relay exposes a fault-record service; BCU and other devices without that service are reported as N/A, not failed. See docs/RELAY_TIME_SYNC_AND_DISTURBANCE_TEST.md.");
            DrawParagraphBlock("PROCEDURE REFERENCE",
                "Project-approved Siemens I-FAT procedure / method statement is an external controlled document and must be confirmed before customer issue. This report does not claim to reproduce a proprietary Siemens template.");
            DrawParagraphBlock("ACCEPTANCE RULE",
                _blankForm
                    ? "During IFAT, a signal will pass only after accepted TRUE and FALSE transition evidence is recorded. The later test record becomes AS TESTED only when every signal in scope passes and no review, failure, or incomplete item remains."
                    : "A signal passes only after accepted TRUE and FALSE transition evidence is recorded. The overall report is AS TESTED only when every signal in scope passes and no review, failure, or pending item remains.");
        }

        private void DrawExecutiveSummary()
        {
            if (_blankForm)
            {
                DrawPlannedScopeSummary();
                return;
            }

            DrawSectionTitle("2. EXECUTIVE RESULT SUMMARY", "Result roll-up by IED; detailed signal evidence is retained in Appendix A");

            var boxWidth = ContentWidth / 5d;
            var summary = new[]
            {
                ("TOTAL", _assessment.Total, Navy), ("PASS", _assessment.Passed, Pass),
                ("REVIEW", _assessment.Review, Attention), ("FAILED", _assessment.Failed, Fail),
                ("PENDING", _assessment.Pending, Muted)
            };
            for (var index = 0; index < summary.Length; index++)
            {
                var x = Margin + (index * boxWidth);
                _page.Rect(x, _cursorY, boxWidth, 46d, DocumentFill, Border, 0.5d);
                _page.Text(x + 9d, _cursorY - 13d, boxWidth - 18d, summary[index].Item1, IoFatReportFontKind.Bold, 5.8d, Muted);
                _page.Text(x + 9d, _cursorY - 34d, boxWidth - 18d, summary[index].Item2.ToString(CultureInfo.InvariantCulture), IoFatReportFontKind.Bold, 13d, summary[index].Item3);
            }
            _cursorY -= 58d;
            DrawExecutiveTableHeader();

            foreach (var ied in _project.Ieds)
            {
                if (_cursorY - 14d < ContentBottom)
                {
                    NewPage();
                    DrawSectionTitle("2. EXECUTIVE RESULT SUMMARY (continued)", "Result roll-up by IED");
                    DrawExecutiveTableHeader();
                }
                DrawExecutiveRow(ied);
            }

            if (_project.Ieds.Count == 0)
            {
                _page.Text(Margin + 8d, _cursorY - 16d, ContentWidth - 16d, "No IED is present in the report scope.", IoFatReportFontKind.Bold, 7d, Attention);
                _cursorY -= 24d;
            }

            _cursorY -= 10d;
            _page.Text(Margin, _cursorY - 12d, ContentWidth,
                $"Timestamp integrity warnings: {_assessment.TimestampWarnings}. Identical TRUE/FALSE IED timestamps require reviewer attention; ARSAS capture time and sequence remain visible in Appendix A.",
                IoFatReportFontKind.Regular, 6.6d, _assessment.TimestampWarnings > 0 ? Attention : Muted);
        }

        private void DrawPlannedScopeSummary()
        {
            DrawSectionTitle("2. PLANNED IFAT TEST SCOPE", "Customer review of the IEDs and IEC 61850 signal checks that will be executed during IFAT");

            var boxWidth = ContentWidth / 4d;
            var labels = new[] { "PLANNED SIGNALS", "IEDS WITH SCOPE", "PHYSICAL PANELS", "EXECUTION STATE" };
            var values = new[]
            {
                _assessment.Total.ToString(CultureInfo.InvariantCulture),
                ScopedIedCount(_project).ToString(CultureInfo.InvariantCulture),
                PanelCount(_project).ToString(CultureInfo.InvariantCulture),
                "NOT STARTED"
            };
            for (var index = 0; index < labels.Length; index++)
            {
                var x = Margin + (index * boxWidth);
                _page.Rect(x, _cursorY, boxWidth, 46d, DocumentFill, Border, 0.5d);
                _page.Text(x + 9d, _cursorY - 13d, boxWidth - 18d, labels[index], IoFatReportFontKind.Bold, 5.8d, Muted);
                _page.Text(x + 9d, _cursorY - 34d, boxWidth - 18d, values[index], IoFatReportFontKind.Bold, index == 3 ? 9.2d : 13d, Blue);
            }
            _cursorY -= 58d;
            DrawPlannedTableHeader();

            foreach (var ied in _project.Ieds)
            {
                if (_cursorY - 14d < ContentBottom)
                {
                    NewPage();
                    DrawSectionTitle("2. PLANNED IFAT TEST SCOPE (continued)", "IED and signal-count schedule for customer review");
                    DrawPlannedTableHeader();
                }
                DrawPlannedRow(ied);
            }

            _cursorY -= 10d;
            _page.Text(Margin, _cursorY - 12d, ContentWidth,
                "No test result status is declared in this blank issue. Evidence and results will be completed during IFAT.",
                IoFatReportFontKind.Bold, 6.6d, Blue);
        }

        private void DrawPlannedTableHeader()
        {
            var widths = PlannedColumnWidths();
            var headers = new[] { "IED", "Panel", "IP / role", "Planned checks", "Test sequence", "Form status" };
            var x = Margin;
            for (var index = 0; index < headers.Length; index++)
            {
                _page.Rect(x, _cursorY, widths[index], 20d, SoftBlue, Border, 0.45d);
                _page.Text(x + 4d, _cursorY - 13d, widths[index] - 8d, headers[index], IoFatReportFontKind.Bold, 5.7d, Blue);
                x += widths[index];
            }
            _cursorY -= 20d;
        }

        private void DrawPlannedRow(IoTestIedPlan ied)
        {
            var points = ReportPoints(ied);
            var values = new[]
            {
                ied.IedName,
                FirstNonEmpty(ied.PanelTag, "-"),
                FirstNonEmpty($"{ied.IpAddress} {ied.IedRole}".Trim(), "-"),
                points.Count.ToString(CultureInfo.InvariantCulture),
                "FALSE > TRUE > FALSE",
                points.Count == 0 ? "NO ENABLED SCOPE" : "PLANNED"
            };
            var widths = PlannedColumnWidths();
            var x = Margin;
            const double height = 14d;
            for (var index = 0; index < values.Length; index++)
            {
                _page.Rect(x, _cursorY, widths[index], height, White, SoftLine, 0.35d);
                _page.Text(x + 4d, _cursorY - 9.5d, widths[index] - 8d, values[index], index < 3 ? IoFatReportFontKind.Regular : IoFatReportFontKind.Bold, 5.4d, index == values.Length - 1 ? Blue : Ink);
                x += widths[index];
            }
            _cursorY -= height;
        }

        private static double[] PlannedColumnWidths() => [105d, 145d, 220d, 76d, 120d, 116d];

        private void DrawAcceptanceAndHandover()
        {
            if (_blankForm)
            {
                DrawBlankFormReviewAndApproval();
                return;
            }

            DrawSectionTitle("3. ACCEPTANCE, DEVIATIONS, AND HANDOVER", "Customer acceptance is gated by the actual result state shown below");

            DrawKeyValueBlock("ACCEPTANCE DECISION",
            [
                ("Report classification", _assessment.IssueStatus),
                ("Customer record class", _assessment.RecordClass),
                ("Open execution items", $"{_assessment.Pending} pending; {_assessment.Review} review; {_assessment.Failed} failed"),
                ("Timestamp warnings", _assessment.TimestampWarnings.ToString(CultureInfo.InvariantCulture)),
                ("Decision", _assessment.IsAccepted ? "ACCEPTABLE FOR FINAL FAT SIGN-OFF" : "NOT ACCEPTABLE FOR FINAL FAT SIGN-OFF")
            ]);

            _cursorY -= 10d;
            DrawParagraphBlock("DEVIATIONS / PUNCH ITEMS",
                _assessment.IsAccepted
                    ? "None reported by the automated result assessment. Any externally agreed punch item must still be listed and referenced before issue."
                    : "Open items are summarized above and identified per IED / signal in Appendix A. Resolve, retest, and regenerate the report before final approval.");
            DrawParagraphBlock("HANDOVER CHECK",
                "Confirm project procedure reference, test equipment records, witness attendance, approved scope, exception disposition, and source workbook checksum before controlled issue.");

            DrawSignatureBlock(["TESTED BY", "CHECKED BY", "CLIENT WITNESS", "APPROVED BY"]);
            if (!_assessment.IsAccepted)
                _page.Text(Margin, _cursorY - 12d, ContentWidth, "FINAL APPROVAL MUST REMAIN OPEN WHILE EXCEPTIONS OR PENDING TESTS EXIST.", IoFatReportFontKind.Bold, 7.2d, Fail);
        }

        private void DrawBlankFormReviewAndApproval()
        {
            DrawSectionTitle("3. FORM REVIEW AND APPROVAL", "Approval on this page confirms the IFAT form and planned scope, not a test result");

            DrawKeyValueBlock("CUSTOMER REVIEW BASIS",
            [
                ("Document classification", _assessment.IssueStatus),
                ("Form class", _assessment.RecordClass),
                ("Execution status", "NOT STARTED - NO TEST EVIDENCE RECORDED"),
                ("Planned scope", $"{_assessment.Total} signal checks across {ScopedIedCount(_project)} IEDs and {PanelCount(_project)} physical panels"),
                ("Customer review action", "Review and approve the form layout, planned signal scope, expected states, method, and witness fields")
            ]);

            _cursorY -= 10d;
            DrawParagraphBlock("FIELDS TO BE COMPLETED DURING IFAT",
                "TRUE evidence, FALSE evidence, timestamps, quality, acquisition source, result, deviations, and final FAT signatures will be populated from recorded test activity during IFAT.");
            DrawParagraphBlock("APPROVAL BOUNDARY",
                "Approval of this blank form authorizes the proposed form and test scope only. It does not constitute device acceptance, successful test completion, IEC 61850 conformance certification, or approval of future results.");

            DrawSignatureBlock(["PREPARED BY", "CHECKED BY", "CUSTOMER REVIEWER", "FORM APPROVED BY"]);
            _page.Text(Margin, _cursorY - 12d, ContentWidth, "FORM / SCOPE APPROVAL ONLY - TEST RESULTS WILL BE ISSUED AFTER IFAT EXECUTION.", IoFatReportFontKind.Bold, 7.0d, Blue);
        }

        private void DrawSignatureBlock(IReadOnlyList<string> labels)
        {
            const double signHeight = 68d;
            var signWidth = ContentWidth / 4d;
            for (var index = 0; index < labels.Count; index++)
            {
                var x = Margin + (index * signWidth);
                _page.Rect(x, _cursorY, signWidth, signHeight, White, Border, 0.55d);
                _page.Text(x + 8d, _cursorY - 14d, signWidth - 16d, labels[index], IoFatReportFontKind.Bold, 5.8d, Muted);
                _page.Line(x + 8d, _cursorY - 38d, x + signWidth - 8d, _cursorY - 38d, Border, 0.55d);
                _page.Text(x + 8d, _cursorY - 51d, signWidth - 16d, "Name / signature", IoFatReportFontKind.Regular, 5.7d, Muted);
                _page.Text(x + 8d, _cursorY - 62d, signWidth - 16d, "Date: __________________", IoFatReportFontKind.Regular, 5.7d, Muted);
            }
            _cursorY -= signHeight + 10d;
        }

        private void DrawAppendixHeading()
        {
            DrawSectionTitle(
                _blankForm ? "APPENDIX A - PLANNED IEC 61850 IFAT CHECKS" : "APPENDIX A - DETAILED IEC 61850 SIGNAL EVIDENCE",
                _blankForm
                    ? "Per-IED signal scope, exact references, expected states, and blank fields to be completed during IFAT"
                    : "Per-IED transition evidence, exact references, timestamps, quality, source, sequence, and result");
        }

        private void DrawSectionTitle(string title, string subtitle)
        {
            _page.Text(Margin, _cursorY - 13d, ContentWidth, title, IoFatReportFontKind.Bold, 12.2d, Navy);
            _page.Text(Margin, _cursorY - 31d, ContentWidth, subtitle, IoFatReportFontKind.Regular, 7.0d, Muted);
            _page.Line(Margin, _cursorY - 42d, PageWidth - Margin, _cursorY - 42d, Blue, 1.1d);
            _cursorY -= 56d;
        }

        private void DrawKeyValueBlock(string title, IReadOnlyList<(string Label, string Value)> rows)
        {
            _page.Text(Margin, _cursorY - 11d, ContentWidth, title, IoFatReportFontKind.Bold, 6.3d, Muted);
            _cursorY -= 20d;
            foreach (var row in rows)
            {
                var valueLines = WrapText(row.Value, ContentWidth - 190d, 6.5d);
                var height = Math.Max(18d, 7d + (valueLines.Count * 8d));
                _page.Rect(Margin, _cursorY, 176d, height, DocumentFill, Border, 0.4d);
                _page.Rect(Margin + 176d, _cursorY, ContentWidth - 176d, height, White, Border, 0.4d);
                _page.Text(Margin + 8d, _cursorY - 12d, 160d, row.Label, IoFatReportFontKind.Bold, 6.1d, Muted);
                var y = _cursorY - 12d;
                foreach (var line in valueLines)
                {
                    _page.Text(Margin + 184d, y, ContentWidth - 192d, line, IoFatReportFontKind.Regular, 6.5d, Ink);
                    y -= 8d;
                }
                _cursorY -= height;
            }
        }

        private void DrawParagraphBlock(string title, string text)
        {
            var lines = WrapText(text, ContentWidth - 24d, 6.7d);
            var height = 28d + (lines.Count * 8d);
            Ensure(height + 8d);
            _page.RoundRect(Margin, _cursorY, ContentWidth, height, 4d, DocumentFill, Border, 0.5d);
            _page.Text(Margin + 12d, _cursorY - 13d, ContentWidth - 24d, title, IoFatReportFontKind.Bold, 6.2d, Blue);
            var y = _cursorY - 29d;
            foreach (var line in lines)
            {
                _page.Text(Margin + 12d, y, ContentWidth - 24d, line, IoFatReportFontKind.Regular, 6.7d, Ink);
                y -= 8d;
            }
            _cursorY -= height + 8d;
        }

        private void DrawExecutiveTableHeader()
        {
            var widths = ExecutiveColumnWidths();
            var headers = new[] { "IED", "Panel", "IP / role", "Total", "Pass", "Review", "Fail", "Pending", "Status" };
            var x = Margin;
            for (var index = 0; index < headers.Length; index++)
            {
                _page.Rect(x, _cursorY, widths[index], 20d, SoftBlue, Border, 0.45d);
                _page.Text(x + 4d, _cursorY - 13d, widths[index] - 8d, headers[index], IoFatReportFontKind.Bold, 5.7d, Blue);
                x += widths[index];
            }
            _cursorY -= 20d;
        }

        private void DrawExecutiveRow(IoTestIedPlan ied)
        {
            var points = ReportPoints(ied);
            var passed = points.Count(point => point.Runtime.State == IoTestPointState.Passed);
            var review = points.Count(point => point.Runtime.State == IoTestPointState.Review);
            var failed = points.Count(point => point.Runtime.State == IoTestPointState.Failed);
            var pending = Math.Max(0, points.Count - passed - review - failed);
            var status = points.Count == 0 ? "NO ENABLED SCOPE" : failed > 0 ? "FAILED" : review > 0 ? "REVIEW" : pending > 0 ? "PENDING" : "PASS";
            var color = failed > 0 ? Fail : review > 0 || pending > 0 || points.Count == 0 ? Attention : Pass;
            var values = new[]
            {
                ied.IedName, FirstNonEmpty(ied.PanelTag, "-"), FirstNonEmpty($"{ied.IpAddress} {ied.IedRole}".Trim(), "-"),
                points.Count.ToString(CultureInfo.InvariantCulture), passed.ToString(CultureInfo.InvariantCulture), review.ToString(CultureInfo.InvariantCulture),
                failed.ToString(CultureInfo.InvariantCulture), pending.ToString(CultureInfo.InvariantCulture), status
            };
            var widths = ExecutiveColumnWidths();
            var x = Margin;
            const double height = 13d;
            for (var index = 0; index < values.Length; index++)
            {
                _page.Rect(x, _cursorY, widths[index], height, White, SoftLine, 0.35d);
                _page.Text(x + 4d, _cursorY - 9d, widths[index] - 8d, values[index], index < 3 ? IoFatReportFontKind.Regular : IoFatReportFontKind.Bold, 5.4d, index == values.Length - 1 ? color : Ink);
                x += widths[index];
            }
            _cursorY -= height;
        }

        private static double[] ExecutiveColumnWidths() => [105d, 100d, 190d, 48d, 48d, 53d, 45d, 55d, 138d];

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
            var status = _blankForm ? $"{points.Count} PLANNED SIGNALS" : $"{passed} OF {points.Count} SIGNALS PASSED";
            var statusColor = _blankForm ? Blue : failed > 0 ? Fail : review > 0 || pending > 0 ? Attention : Pass;
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
                _blankForm ? "TRUE evidence (during IFAT)" : "TRUE evidence",
                _blankForm ? "FALSE evidence (during IFAT)" : "FALSE evidence",
                _blankForm ? "Result (during IFAT)" : "Result / reason"
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

        private ReportCell[] BuildCells(IoTestPointPlan point, int rowNumber)
        {
            var stateColor = ResolvePointColor(point.Runtime.State);
            return
            [
                new ReportCell(rowNumber.ToString(CultureInfo.InvariantCulture), IoFatReportFontKind.Mono, 5.8d, Ink),
                new ReportCell(point.SignalName, IoFatReportFontKind.Bold, 6.4d, Ink),
                new ReportCell(point.ReportIecReference, IoFatReportFontKind.Mono, 5.65d, Ink),
                new ReportCell(ExpectedStateText(point), IoFatReportFontKind.Regular, 6.0d, Ink),
                new ReportCell(_blankForm ? "To be completed during IFAT" : EvidenceText(point.Runtime.OnEvidence), _blankForm ? IoFatReportFontKind.Regular : IoFatReportFontKind.Mono, 5.35d, _blankForm ? Muted : Ink),
                new ReportCell(_blankForm ? "To be completed during IFAT" : EvidenceText(point.Runtime.OffEvidence), _blankForm ? IoFatReportFontKind.Regular : IoFatReportFontKind.Mono, 5.35d, _blankForm ? Muted : Ink),
                new ReportCell(_blankForm ? "To be completed during IFAT" : ResultWithReason(point), IoFatReportFontKind.Bold, 5.8d, _blankForm ? Muted : stateColor)
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

        private static double[] ColumnWidths() => [24d, 140d, 170d, 88d, 140d, 140d, 80d];

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

    private sealed record ReportAssessment(
        int Total,
        int Passed,
        int Review,
        int Failed,
        int Pending,
        int TimestampWarnings,
        string IssueStatus,
        string RecordClass,
        string Warning,
        IoFatReportColor StatusColor,
        bool IsAccepted);

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

    private static int ScopedIedCount(IoTestProject project)
        => project.Ieds.Count(ied => ReportPoints(ied).Count > 0);

    private static int PanelCount(IoTestProject project)
        => project.Ieds.Select(ied => ied.PanelTag)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static ReportAssessment Assess(IoTestProject project, bool draft, bool blankForm)
    {
        var points = project.Ieds.SelectMany(ReportPoints).ToList();
        var counts = Counts(project);
        var timestampWarnings = points.Count(point =>
            point.Runtime.OnEvidence?.IedTimestamp is { } on &&
            point.Runtime.OffEvidence?.IedTimestamp is { } off && on == off);

        if (blankForm)
            return new(points.Count, 0, 0, 0, 0, 0,
                "FOR CUSTOMER REVIEW", "BLANK IFAT TEST FORM", "Review and approve the planned form and IFAT scope. No test result is declared.", Blue, false);
        if (draft)
            return new(points.Count, counts.Passed, counts.Review, counts.Failed, counts.Pending, timestampWarnings,
                "PREVIEW", "NOT FOR ISSUE", "Preview output; regenerate from the controlled export action for issue.", Attention, false);
        if (counts.Failed > 0)
            return new(points.Count, counts.Passed, counts.Review, counts.Failed, counts.Pending, timestampWarnings,
                "FAILED", "FAT RECORD - NOT ACCEPTED", "Failed evidence exists. Correct, retest, and regenerate before approval.", Fail, false);
        if (counts.Review > 0)
            return new(points.Count, counts.Passed, counts.Review, counts.Failed, counts.Pending, timestampWarnings,
                "REVIEW REQUIRED", "PARTIAL TEST RECORD", "Reviewer disposition is required before final customer acceptance.", Attention, false);
        if (counts.Pending > 0 || points.Count == 0)
            return new(points.Count, counts.Passed, counts.Review, counts.Failed, counts.Pending, timestampWarnings,
                "PARTIAL", "NOT FOR CUSTOMER ACCEPTANCE", "Testing is incomplete. This PDF is a controlled progress record only.", Attention, false);

        return new(points.Count, counts.Passed, counts.Review, counts.Failed, counts.Pending, timestampWarnings,
            "AS TESTED", "CUSTOMER FAT RECORD", "All signals in the defined report scope have passed.", Pass, true);
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

    private static string ResultWithReason(IoTestPointPlan point)
    {
        var result = ResultText(point.Runtime.State);
        var reason = Clean(point.Runtime.StatusReason);
        return reason == "-" ? result : $"{result}\n{reason}";
    }

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

    private static string EvidenceText(IoTestTransitionEvidence? evidence)
    {
        if (evidence == null)
            return "-";

        var iedTime = evidence.IedTimestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) ?? "not supplied";
        var captured = evidence.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        return $"IED: {iedTime}\nARSAS: {captured}\nQuality: {Clean(evidence.Quality)}\nSource: {Clean(evidence.AcquisitionSource)}\nSeq: {evidence.Sequence} / Gen: {evidence.ConnectionGeneration}";
    }

    private static string BuildDeviceMeta(IoTestIedPlan ied)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(ied.IpAddress)) values.Add(ied.IpAddress.Trim());
        if (!string.IsNullOrWhiteSpace(ied.IedRole)) values.Add(ied.IedRole.Trim());
        if (!string.IsNullOrWhiteSpace(ied.Location)) values.Add(ied.Location.Trim());
        if (!string.IsNullOrWhiteSpace(ied.VoltageLevel)) values.Add(ied.VoltageLevel.Trim());
        if (!string.IsNullOrWhiteSpace(ied.Switchgear)) values.Add(ied.Switchgear.Trim());
        if (!string.IsNullOrWhiteSpace(ied.PanelTag)) values.Add("Panel " + ied.PanelTag.Trim());
        if (!string.IsNullOrWhiteSpace(ied.PrimarySntpServer) || !string.IsNullOrWhiteSpace(ied.RedundantSntpServer))
            values.Add($"SNTP {FirstNonEmpty(ied.PrimarySntpServer, "-")} / {FirstNonEmpty(ied.RedundantSntpServer, "-")}");
        return values.Count == 0 ? "Device details not supplied" : string.Join("  |  ", values);
    }

    private static string BuildSupplierLine(string supplier)
        => string.IsNullOrWhiteSpace(supplier) ? string.Empty : "Supplier: " + supplier.Trim();

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
