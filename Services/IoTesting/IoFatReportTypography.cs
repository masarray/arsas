using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// One typography policy for every FAT evidence surface. Inter is preferred everywhere;
/// Segoe UI is the only fallback so Preview and PDF never drift into unrelated visual fonts.
/// </summary>
internal static class IoFatReportTypography
{
    public const string PreferredFamilyName = "Inter";
    public const string FallbackFamilyName = "Segoe UI";
    public static readonly FontFamily PreviewFontFamily = new($"{PreferredFamilyName}, {FallbackFamilyName}");

    public static IoFatPdfFontSet ResolvePdfFonts()
    {
        foreach (var familyName in new[] { PreferredFamilyName, FallbackFamilyName })
        {
            if (!TryResolveFace(familyName, FontWeights.Normal, out var regular) ||
                !TryResolveFace(familyName, FontWeights.Bold, out var bold))
            {
                continue;
            }

            return new IoFatPdfFontSet(familyName, regular, bold);
        }

        throw new InvalidOperationException(
            "ARSAS FAT PDF requires Inter or Segoe UI. Install Inter for the preferred report typography; Segoe UI is the supported fallback.");
    }

    private static bool TryResolveFace(string familyName, FontWeight weight, out IoFatPdfFontFace face)
    {
        face = null!;
        try
        {
            var typeface = new Typeface(
                new FontFamily(familyName),
                FontStyles.Normal,
                weight,
                FontStretches.Normal);
            if (!typeface.TryGetGlyphTypeface(out var glyphTypeface) || glyphTypeface == null)
                return false;
            if (!CanEmbedOutlines(glyphTypeface.EmbeddingRights))
                return false;
            if (!glyphTypeface.FontUri.IsFile)
                return false;

            var path = glyphTypeface.FontUri.LocalPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            var fontBytes = File.ReadAllBytes(path);
            if (fontBytes.Length == 0)
                return false;

            var family = ResolveLocalizedName(glyphTypeface.FamilyNames, familyName);
            var faceName = ResolveLocalizedName(
                glyphTypeface.FaceNames,
                weight >= FontWeights.Bold ? "Bold" : "Regular");
            var pdfName = SanitizePdfName($"{family}-{faceName}");
            var widths = new int[95];
            for (var character = 32; character <= 126; character++)
            {
                widths[character - 32] = 500;
                if (!glyphTypeface.CharacterToGlyphMap.TryGetValue(character, out var glyphIndex))
                    continue;
                if (!glyphTypeface.AdvanceWidths.TryGetValue(glyphIndex, out var advance))
                    continue;
                widths[character - 32] = Math.Max(1, (int)Math.Round(advance * 1000d, MidpointRounding.AwayFromZero));
            }

            var ascent = Math.Max(1, (int)Math.Round(glyphTypeface.Baseline * 1000d, MidpointRounding.AwayFromZero));
            var descent = -Math.Max(1, (int)Math.Round((glyphTypeface.Height - glyphTypeface.Baseline) * 1000d, MidpointRounding.AwayFromZero));
            var capHeight = Math.Max(1, (int)Math.Round(glyphTypeface.CapsHeight * 1000d, MidpointRounding.AwayFromZero));

            face = new IoFatPdfFontFace(
                familyName,
                pdfName,
                fontBytes,
                widths,
                ascent,
                descent,
                capHeight,
                weight >= FontWeights.Bold ? 120 : 80);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool CanEmbedOutlines(FontEmbeddingRight right)
        => right is not FontEmbeddingRight.RestrictedLicense
            and not FontEmbeddingRight.InstallableButWithBitmapsOnly
            and not FontEmbeddingRight.InstallableButNoSubsettingAndWithBitmapsOnly
            and not FontEmbeddingRight.PreviewAndPrintButWithBitmapsOnly
            and not FontEmbeddingRight.PreviewAndPrintButNoSubsettingAndWithBitmapsOnly
            and not FontEmbeddingRight.EditableButWithBitmapsOnly
            and not FontEmbeddingRight.EditableButNoSubsettingAndWithBitmapsOnly;

    private static string ResolveLocalizedName(IDictionary<CultureInfo, string> names, string fallback)
    {
        if (names.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out var english) && !string.IsNullOrWhiteSpace(english))
            return english;
        return names.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? fallback;
    }

    private static string SanitizePdfName(string value)
    {
        var characters = value
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .ToArray();
        return characters.Length == 0 ? "ARSASReportFont" : new string(characters);
    }
}

internal sealed record IoFatPdfFontSet(
    string FamilyName,
    IoFatPdfFontFace Regular,
    IoFatPdfFontFace Bold);

internal sealed record IoFatPdfFontFace(
    string FamilyName,
    string PdfName,
    byte[] FontBytes,
    IReadOnlyList<int> Widths,
    int Ascent,
    int Descent,
    int CapHeight,
    int StemV);
