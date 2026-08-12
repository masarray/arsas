// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Dependency-free PDF 1.4 serializer for the shared IO FAT report layout plan.
/// The WPF preview consumes the same command stream through
/// IoFatReportPreviewDocumentBuilder.
/// </summary>
internal static class IoFatNativePdfWriter
{
    public static byte[] Build(IoFatReportLayoutPlan layout, IoTestProject project)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(project);
        if (layout.Pages.Count == 0)
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

        foreach (var page in layout.Pages)
        {
            var content = BuildPageContent(page);
            var contentBytes = Encoding.ASCII.GetBytes(content);
            var contentId = AddObject($"<< /Length {contentBytes.Length.ToString(CultureInfo.InvariantCulture)} >>\nstream\n{content}endstream");
            var pageId = AddObject(
                $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {Number(page.Width)} {Number(page.Height)}] " +
                $"/Resources << /Font << /F1 {fontRegularId} 0 R /F2 {fontBoldId} 0 R /F3 {fontMonoId} 0 R >> >> " +
                $"/Contents {contentId} 0 R >>");
            pageIds.Add(pageId);
        }

        var primaryReference = project.Ieds
            .SelectMany(ied => ied.TestPoints)
            .Select(point => point.ObjectReference)
            .FirstOrDefault(reference => !string.IsNullOrWhiteSpace(reference))
            ?? project.ProjectId;
        var blankForm = IoFatPdfReportService.IsBlankForm(project);
        var title = blankForm
            ? $"{project.ProjectName} - IEC 61850 IFAT Test Form"
            : $"{project.ProjectName} - IEC 61850 FAT Evidence Report";
        var subject = blankForm
            ? $"Blank IFAT form for customer review of the planned test scope. No executed test evidence is declared. Primary IEC 61850 reference: {primaryReference}"
            : $"Controlled IEC 61850 FAT report with procedure basis, acceptance summary, and detailed transition evidence. Primary IEC 61850 reference: {primaryReference}";
        var infoId = AddObject(
            $"<< /Title ({EscapeLiteral(IoFatReportLayoutEngine.SanitizeReportText(title))}) " +
            $"/Subject ({EscapeLiteral(IoFatReportLayoutEngine.SanitizeReportText(subject))}) " +
            $"/Keywords ({(blankForm ? "IEC 61850 IFAT blank test form planned scope ARSAS" : "IEC 61850 FAT IFAT FALSE TRUE FALSE ARSAS evidence traceability")}) " +
            "/Author (ARSAS) " +
            "/Creator (ARSAS Native PDF and FixedDocument Engine, adapted from ARIEC60870) " +
            "/Producer (ARSAS Native PDF Engine) " +
            $"/CreationDate ({PdfDate(layout.CreatedAt)}) >>");

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

    private static string BuildPageContent(IoFatReportPagePlan page)
    {
        var output = new StringBuilder(32_000);
        foreach (var command in page.Commands)
        {
            switch (command)
            {
                case IoFatReportTextCommand text:
                    WriteText(output, text);
                    break;
                case IoFatReportLineCommand line:
                    WriteLine(output, line);
                    break;
                case IoFatReportRectCommand rect when rect.Radius > 0d:
                    WriteRoundRect(output, rect);
                    break;
                case IoFatReportRectCommand rect:
                    WriteRect(output, rect);
                    break;
            }
        }
        return output.ToString();
    }

    private static void WriteText(StringBuilder output, IoFatReportTextCommand command)
    {
        var safe = IoFatReportLayoutEngine.SanitizeReportText(command.Text);
        if (safe.Length == 0)
            return;
        output.Append("BT ")
            .Append(Fill(command.Color)).Append(' ')
            .Append('/').Append(ResourceName(command.Font)).Append(' ').Append(Number(command.FontSize)).Append(" Tf ")
            .Append("1 0 0 1 ").Append(Number(command.X)).Append(' ').Append(Number(command.BaselineY)).Append(" Tm ")
            .Append('(').Append(EscapeLiteral(safe)).Append(") Tj ET\n");
    }

    private static void WriteLine(StringBuilder output, IoFatReportLineCommand command)
    {
        output.Append(Number(command.StrokeThickness)).Append(" w ")
            .Append(Stroke(command.Stroke)).Append(' ')
            .Append(Number(command.X1)).Append(' ').Append(Number(command.Y1)).Append(" m ")
            .Append(Number(command.X2)).Append(' ').Append(Number(command.Y2)).Append(" l S\n");
    }

    private static void WriteRect(StringBuilder output, IoFatReportRectCommand command)
    {
        var y = command.TopY - command.Height;
        if (command.StrokeThickness <= 0d || command.Fill.Equals(command.Stroke))
        {
            output.Append(Fill(command.Fill)).Append(' ')
                .Append(Number(command.X)).Append(' ').Append(Number(y)).Append(' ')
                .Append(Number(command.Width)).Append(' ').Append(Number(command.Height)).Append(" re f\n");
            return;
        }

        output.Append(Number(command.StrokeThickness)).Append(" w ")
            .Append(Fill(command.Fill)).Append(' ')
            .Append(Stroke(command.Stroke)).Append(' ')
            .Append(Number(command.X)).Append(' ').Append(Number(y)).Append(' ')
            .Append(Number(command.Width)).Append(' ').Append(Number(command.Height)).Append(" re B\n");
    }

    private static void WriteRoundRect(StringBuilder output, IoFatReportRectCommand command)
    {
        var y = command.TopY - command.Height;
        var radius = Math.Min(command.Radius, Math.Min(command.Width, command.Height) / 2d);
        var curve = radius * 0.55228475d;
        if (command.StrokeThickness > 0d)
            output.Append(Number(command.StrokeThickness)).Append(" w ");
        output.Append(Fill(command.Fill)).Append(' ');
        if (command.StrokeThickness > 0d)
            output.Append(Stroke(command.Stroke)).Append(' ');

        output.Append(Number(command.X + radius)).Append(' ').Append(Number(y)).Append(" m ")
            .Append(Number(command.X + command.Width - radius)).Append(' ').Append(Number(y)).Append(" l ")
            .Append(Number(command.X + command.Width - radius + curve)).Append(' ').Append(Number(y)).Append(' ')
            .Append(Number(command.X + command.Width)).Append(' ').Append(Number(y + radius - curve)).Append(' ')
            .Append(Number(command.X + command.Width)).Append(' ').Append(Number(y + radius)).Append(" c ")
            .Append(Number(command.X + command.Width)).Append(' ').Append(Number(y + command.Height - radius)).Append(" l ")
            .Append(Number(command.X + command.Width)).Append(' ').Append(Number(y + command.Height - radius + curve)).Append(' ')
            .Append(Number(command.X + command.Width - radius + curve)).Append(' ').Append(Number(y + command.Height)).Append(' ')
            .Append(Number(command.X + command.Width - radius)).Append(' ').Append(Number(y + command.Height)).Append(" c ")
            .Append(Number(command.X + radius)).Append(' ').Append(Number(y + command.Height)).Append(" l ")
            .Append(Number(command.X + radius - curve)).Append(' ').Append(Number(y + command.Height)).Append(' ')
            .Append(Number(command.X)).Append(' ').Append(Number(y + command.Height - radius + curve)).Append(' ')
            .Append(Number(command.X)).Append(' ').Append(Number(y + command.Height - radius)).Append(" c ")
            .Append(Number(command.X)).Append(' ').Append(Number(y + radius)).Append(" l ")
            .Append(Number(command.X)).Append(' ').Append(Number(y + radius - curve)).Append(' ')
            .Append(Number(command.X + radius - curve)).Append(' ').Append(Number(y)).Append(' ')
            .Append(Number(command.X + radius)).Append(' ').Append(Number(y))
            .Append(command.StrokeThickness > 0d ? " c B\n" : " c f\n");
    }

    private static string Fill(IoFatReportColor color)
        => $"{Channel(color.R)} {Channel(color.G)} {Channel(color.B)} rg";

    private static string Stroke(IoFatReportColor color)
        => $"{Channel(color.R)} {Channel(color.G)} {Channel(color.B)} RG";

    private static string Channel(byte value)
        => (value / 255d).ToString("0.###", CultureInfo.InvariantCulture);

    private static string ResourceName(IoFatReportFontKind font) => font switch
    {
        IoFatReportFontKind.Bold => "F2",
        IoFatReportFontKind.Mono => "F3",
        _ => "F1"
    };

    private static string EscapeLiteral(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string PdfDate(DateTimeOffset value)
        => "D:" + value.ToLocalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static string Number(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void WriteAscii(Stream stream, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
