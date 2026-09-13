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

/// <summary>
/// Native FAT report logo authority. The default mark is the real packaged ARSAS app icon.
/// Custom logos are decoded once and stored in the immutable report command stream, so Preview
/// and Save PDF never depend on the source file after selection.
/// </summary>
internal static class NativeFatReportLogoService
{
    private const int MaxPixelDimension = 512;
    private const double NativeLogoX = 710d;
    private const double LegacySyntheticLogoTop = 582d;
    private const double NativeLogoTop = 576d;
    private const double NativeLogoSize = 22d;

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
                    // The base layout still carries the legacy synthetic icon/wordmark so
                    // older non-image report paths remain structurally compatible. Native
                    // Preview/PDF replaces the complete legacy mark with the real app icon.
                    if (IsLegacyBrandingCommand(command))
                        continue;
                    commands.Add(command);
                }

                if (logo != null)
                {
                    commands.Add(new IoFatReportImageCommand(
                        NativeLogoX,
                        NativeLogoTop,
                        NativeLogoSize,
                        NativeLogoSize,
                        logo.PixelWidth,
                        logo.PixelHeight,
                        logo.RgbPixels));
                }

                return new IoFatReportPagePlan(page.PageNumber, page.Width, page.Height, commands.ToArray());
            })
            .ToArray();

        return new IoFatReportLayoutPlan(layout.ProjectId, layout.CreatedAt, layout.Draft, pages);
    }

    private static bool IsLegacyBrandingCommand(IoFatReportCommand command)
    {
        if (command is IoFatReportRectCommand rect)
        {
            return Near(rect.X, NativeLogoX) &&
                   Near(rect.TopY, LegacySyntheticLogoTop) &&
                   Near(rect.Width, NativeLogoSize) &&
                   Near(rect.Height, NativeLogoSize);
        }

        if (command is IoFatReportTextCommand text)
        {
            var syntheticA = string.Equals(text.Text, "A", StringComparison.Ordinal) &&
                             Near(text.X, NativeLogoX + 5.2d) &&
                             Near(text.BaselineY, LegacySyntheticLogoTop - 15.2d);
            var legacyWordmark = string.Equals(text.Text, "ARSAS", StringComparison.Ordinal) &&
                                 Near(text.X, NativeLogoX + 29d) &&
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

    private static byte CompositeOnWhite(byte channel, byte alpha)
        => (byte)((channel * alpha + 255 * (255 - alpha) + 127) / 255);

    private static bool Near(double left, double right)
        => Math.Abs(left - right) < 0.01d;
}
