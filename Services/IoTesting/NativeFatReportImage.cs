using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Raster image command shared by the WPF report preview and native PDF writer.
/// Pixels are stored as opaque RGB so the exact same decoded image is rendered by both paths.
/// </summary>
internal sealed record IoFatReportImageCommand(
    double X,
    double TopY,
    double Width,
    double Height,
    int PixelWidth,
    int PixelHeight,
    byte[] RgbPixels) : IoFatReportCommand;

internal sealed record NativeFatReportLogo(
    int PixelWidth,
    int PixelHeight,
    byte[] RgbPixels,
    string SourceName);

internal readonly record struct NativeFatLogoPlacement(
    double X,
    double TopY,
    double Width,
    double Height);

/// <summary>
/// Native FAT report logo authority. The default mark is the real packaged ARSAS app icon.
/// Custom logos are decoded once and stored in the immutable report command stream, so Preview
/// and Save PDF never depend on the source file after selection.
/// </summary>
internal static class NativeFatReportLogoService
{
    private const int MaxPixelDimension = 512;
    private const byte VisibleAlphaThreshold = 8;

    // Legacy synthetic ARSAS mark coordinates retained only so the decorator can remove it.
    private const double LegacySyntheticLogoX = 710d;
    private const double LegacySyntheticLogoTop = 582d;
    private const double LegacySyntheticLogoSize = 22d;

    // Professional adaptive header slot. The top header has substantially more room than the
    // legacy 22 x 22 icon box. Wide corporate wordmarks can now use the available width while
    // square/circular marks use the full height without distortion or cropping.
    private const double HeaderLogoSlotLeft = 656d;
    private const double HeaderLogoSlotTopY = 578d;
    private const double HeaderLogoSlotWidth = 156d;
    private const double HeaderLogoSlotHeight = 42d;

    private static readonly string[] DefaultLogoUris =
    [
        "pack://application:,,,/ARSAS;component/Assets/app-icon-256.png",
        "pack://application:,,,/ARSAS;component/Assets/app-icon.png"
    ];

    public static NativeFatReportLogo? TryLoadDefault()
    {
        foreach (var uriText in DefaultLogoUris)
        {
            try
            {
                var resource = Application.GetResourceStream(new Uri(uriText, UriKind.Absolute));
                if (resource?.Stream == null)
                    continue;
                using (resource.Stream)
                    return Decode(resource.Stream, uriText);
            }
            catch (IOException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        return null;
    }

    public static NativeFatReportLogo LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Logo file path is required.", nameof(path));

        using var stream = File.OpenRead(path);
        return Decode(stream, Path.GetFileName(path));
    }

    public static IoFatReportLayoutPlan Apply(IoFatReportLayoutPlan layout, NativeFatReportLogo? logo)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var pages = layout.Pages
            .Select(page =>
            {
                var commands = new List<IoFatReportCommand>(page.Commands.Count + 1);
                foreach (var command in page.Commands)
                {
                    if (IsLegacyBrandingCommand(command))
                        continue;
                    commands.Add(command);
                }

                if (logo != null)
                {
                    var placement = CalculatePlacement(logo);
                    if (placement.Width > 0d && placement.Height > 0d)
                    {
                        commands.Add(new IoFatReportImageCommand(
                            placement.X,
                            placement.TopY,
                            placement.Width,
                            placement.Height,
                            logo.PixelWidth,
                            logo.PixelHeight,
                            logo.RgbPixels));
                    }
                }

                return new IoFatReportPagePlan(page.PageNumber, page.Width, page.Height, commands.ToArray());
            })
            .ToArray();

        return new IoFatReportLayoutPlan(layout.ProjectId, layout.CreatedAt, layout.Draft, pages);
    }

    /// <summary>
    /// Uniform-fit placement inside one fixed header field. This deliberately preserves aspect
    /// ratio: wide logos consume width, square/circular logos consume height, and neither is
    /// stretched or cropped. The result is right-aligned and vertically centered in the slot.
    /// </summary>
    internal static NativeFatLogoPlacement CalculatePlacement(NativeFatReportLogo logo)
    {
        ArgumentNullException.ThrowIfNull(logo);
        if (logo.PixelWidth <= 0 || logo.PixelHeight <= 0)
            return default;

        var scale = Math.Min(
            HeaderLogoSlotWidth / logo.PixelWidth,
            HeaderLogoSlotHeight / logo.PixelHeight);
        if (!double.IsFinite(scale) || scale <= 0d)
            return default;

        var width = logo.PixelWidth * scale;
        var height = logo.PixelHeight * scale;
        var x = HeaderLogoSlotLeft + HeaderLogoSlotWidth - width;
        var topY = HeaderLogoSlotTopY - ((HeaderLogoSlotHeight - height) / 2d);
        return new NativeFatLogoPlacement(x, topY, width, height);
    }

    private static bool IsLegacyBrandingCommand(IoFatReportCommand command)
    {
        if (command is IoFatReportRectCommand rect)
        {
            return Near(rect.X, LegacySyntheticLogoX) &&
                   Near(rect.TopY, LegacySyntheticLogoTop) &&
                   Near(rect.Width, LegacySyntheticLogoSize) &&
                   Near(rect.Height, LegacySyntheticLogoSize);
        }

        if (command is IoFatReportTextCommand text)
        {
            var syntheticA = string.Equals(text.Text, "A", StringComparison.Ordinal) &&
                             Near(text.X, LegacySyntheticLogoX + 5.2d) &&
                             Near(text.BaselineY, LegacySyntheticLogoTop - 15.2d);
            var legacyWordmark = string.Equals(text.Text, "ARSAS", StringComparison.Ordinal) &&
                                 Near(text.X, LegacySyntheticLogoX + 29d) &&
                                 Near(text.BaselineY, LegacySyntheticLogoTop - 15.4d);
            return syntheticA || legacyWordmark;
        }

        return false;
    }

    private static NativeFatReportLogo Decode(Stream stream, string sourceName)
    {
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        BitmapSource source = decoder.Frames[0];

        // Remove transparent canvas padding before sizing. Corporate PNGs often contain a
        // large transparent artboard; fitting the full canvas would make the visible logo look
        // artificially tiny even when the destination slot itself is large.
        source = TrimTransparentPadding(source);

        var largest = Math.Max(source.PixelWidth, source.PixelHeight);
        if (largest > MaxPixelDimension)
        {
            var scale = MaxPixelDimension / (double)largest;
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0d);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var bgraStride = checked(width * 4);
        var bgra = new byte[checked(bgraStride * height)];
        converted.CopyPixels(bgra, bgraStride, 0);

        var rgb = new byte[checked(width * height * 3)];
        var targetOffset = 0;
        for (var sourceOffset = 0; sourceOffset < bgra.Length; sourceOffset += 4, targetOffset += 3)
        {
            var blue = bgra[sourceOffset];
            var green = bgra[sourceOffset + 1];
            var red = bgra[sourceOffset + 2];
            var alpha = bgra[sourceOffset + 3];

            // PDF image XObjects here are RGB-only. Composite transparency onto the white
            // report page so transparent PNG logos remain visually correct in both renderers.
            rgb[targetOffset] = CompositeOnWhite(red, alpha);
            rgb[targetOffset + 1] = CompositeOnWhite(green, alpha);
            rgb[targetOffset + 2] = CompositeOnWhite(blue, alpha);
        }

        return new NativeFatReportLogo(width, height, rgb, sourceName);
    }

    private static BitmapSource TrimTransparentPadding(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0d);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        if (width <= 0 || height <= 0)
            return converted;

        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        converted.CopyPixels(pixels, stride, 0);
        var bounds = FindVisibleBounds(pixels, width, height);
        if (bounds.IsEmpty ||
            (bounds.X == 0 && bounds.Y == 0 && bounds.Width == width && bounds.Height == height))
        {
            return converted;
        }

        return new CroppedBitmap(converted, bounds);
    }

    internal static Int32Rect FindVisibleBounds(byte[] bgra, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (width <= 0 || height <= 0 || bgra.Length < checked(width * height * 4))
            return Int32Rect.Empty;

        var minX = width;
        var minY = height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var alpha = bgra[rowOffset + (x * 4) + 3];
                if (alpha <= VisibleAlphaThreshold)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < minX || maxY < minY
            ? Int32Rect.Empty
            : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static byte CompositeOnWhite(byte channel, byte alpha)
        => (byte)((channel * alpha + 255 * (255 - alpha) + 127) / 255);

    private static bool Near(double left, double right)
        => Math.Abs(left - right) < 0.01d;
}
