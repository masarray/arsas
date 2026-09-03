// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Native WPF renderer for the shared IO FAT report layout plan. This is adapted
/// from the project-owned ARIEC60870 FixedDocument preview architecture.
/// </summary>
internal static class IoFatReportPreviewDocumentBuilder
{
    private const double DipPerPdfPoint = 96d / 72d;
    private static readonly FontFamily ReportFont = new("Inter, Segoe UI, Aptos, Arial");
    private static readonly FontFamily MonoFont = new("Consolas, Cascadia Mono");

    public static FixedDocument Build(
        IoTestProject project,
        bool draft,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        // Replaces the legacy IoFatReportLayoutEngine.Build route with the exact same
        // scoped layout contract used by native PDF. Remove from FAT is therefore a hard
        // exclusion in both PDF and on-screen form/print preview, including rows that own
        // historical completed evidence.
        var layout = IoFatPdfReportService.BuildLayout(project, generatedAt, draft);
        return Render(layout);
    }

    public static FixedDocument Render(IoFatReportLayoutPlan layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var document = new FixedDocument();
        foreach (var pagePlan in layout.Pages)
        {
            var fixedPage = new FixedPage
            {
                Width = pagePlan.Width * DipPerPdfPoint,
                Height = pagePlan.Height * DipPerPdfPoint,
                Background = Brushes.White,
                SnapsToDevicePixels = true
            };
            fixedPage.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Ideal);
            fixedPage.SetValue(TextOptions.TextRenderingModeProperty, TextRenderingMode.ClearType);

            foreach (var command in pagePlan.Commands)
            {
                switch (command)
                {
                    case IoFatReportRectCommand rect:
                        AddRectangle(fixedPage, pagePlan.Height, rect);
                        break;
                    case IoFatReportLineCommand line:
                        AddLine(fixedPage, pagePlan.Height, line);
                        break;
                    case IoFatReportTextCommand text:
                        AddText(fixedPage, pagePlan.Height, text);
                        break;
                }
            }

            var content = new PageContent();
            ((IAddChild)content).AddChild(fixedPage);
            document.Pages.Add(content);
        }
        return document;
    }

    private static void AddRectangle(FixedPage page, double pageHeight, IoFatReportRectCommand command)
    {
        var border = new Border
        {
            Background = ToBrush(command.Fill),
            BorderBrush = ToBrush(command.Stroke),
            BorderThickness = command.StrokeThickness <= 0d
                ? new Thickness(0)
                : new Thickness(Math.Max(0.5d, command.StrokeThickness * DipPerPdfPoint)),
            CornerRadius = new CornerRadius(Math.Max(0d, command.Radius * DipPerPdfPoint)),
            SnapsToDevicePixels = true
        };
        Add(
            page,
            border,
            command.X * DipPerPdfPoint,
            (pageHeight - command.TopY) * DipPerPdfPoint,
            command.Width * DipPerPdfPoint,
            command.Height * DipPerPdfPoint);
    }

    private static void AddLine(FixedPage page, double pageHeight, IoFatReportLineCommand command)
    {
        var x1 = command.X1 * DipPerPdfPoint;
        var y1 = (pageHeight - command.Y1) * DipPerPdfPoint;
        var x2 = command.X2 * DipPerPdfPoint;
        var y2 = (pageHeight - command.Y2) * DipPerPdfPoint;
        var left = Math.Min(x1, x2);
        var top = Math.Min(y1, y2);
        var line = new Line
        {
            X1 = x1 - left,
            Y1 = y1 - top,
            X2 = x2 - left,
            Y2 = y2 - top,
            Stroke = ToBrush(command.Stroke),
            StrokeThickness = Math.Max(0.5d, command.StrokeThickness * DipPerPdfPoint),
            SnapsToDevicePixels = true
        };
        Add(page, line, left, top, Math.Max(1d, Math.Abs(x2 - x1) + 1d), Math.Max(1d, Math.Abs(y2 - y1) + 1d));
    }

    private static void AddText(FixedPage page, double pageHeight, IoFatReportTextCommand command)
    {
        var fontSize = Math.Max(1d, command.FontSize * DipPerPdfPoint);
        var top = (pageHeight - command.BaselineY - (command.FontSize * 0.82d)) * DipPerPdfPoint;
        var block = new TextBlock
        {
            Text = command.Text,
            FontFamily = command.Font == IoFatReportFontKind.Mono ? MonoFont : ReportFont,
            FontSize = fontSize,
            FontWeight = command.Font == IoFatReportFontKind.Bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = ToBrush(command.Color),
            // The layout engine has already split every cell into explicit lines.
            // Evidence must never be replaced with dots in the preview.
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap,
            ClipToBounds = false,
            SnapsToDevicePixels = true
        };
        block.SetValue(TextOptions.TextFormattingModeProperty, TextFormattingMode.Ideal);
        block.SetValue(TextOptions.TextRenderingModeProperty, TextRenderingMode.ClearType);

        // Downscale only when the actual Windows font metrics are wider than the
        // shared PDF-point estimate. This keeps PASS and timestamps complete while
        // preserving the intended size whenever they already fit.
        var textPresenter = new Viewbox
        {
            Child = block,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = false,
            SnapsToDevicePixels = true
        };

        Add(
            page,
            textPresenter,
            command.X * DipPerPdfPoint,
            top,
            Math.Max(4d, command.Width * DipPerPdfPoint),
            Math.Max(fontSize + 6d, fontSize * 1.65d));
    }

    private static void Add(FixedPage page, UIElement element, double x, double y, double width, double height)
    {
        element.SetValue(FrameworkElement.WidthProperty, width);
        element.SetValue(FrameworkElement.HeightProperty, height);
        FixedPage.SetLeft(element, x);
        FixedPage.SetTop(element, y);
        page.Children.Add(element);
    }

    private static Brush ToBrush(IoFatReportColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
