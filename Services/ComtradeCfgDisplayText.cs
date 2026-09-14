using System.IO;
using System.Text;

namespace ArIED61850Tester.Services;

/// <summary>
/// Recovers COMTRADE CFG display labels from the original file bytes. IEC COMTRADE files found in
/// legacy relay fleets are not uniformly UTF-8; many recorder exports contain ISO-8859-1/ANSI
/// accented text. The native bridge keeps its UTF-8 ABI, while this display helper performs strict
/// UTF-8 detection and a byte-preserving Latin-1 fallback for channel labels.
/// </summary>
internal static class ComtradeCfgDisplayText
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static IReadOnlyDictionary<uint, string> TryReadStatusChannelIds(
        string cfgPath,
        uint analogCount,
        uint statusCount)
    {
        if (string.IsNullOrWhiteSpace(cfgPath) || statusCount == 0)
            return new Dictionary<uint, string>();

        try
        {
            var bytes = File.ReadAllBytes(cfgPath);
            var text = DecodeCfg(bytes);
            var lines = SplitLines(text);
            var analog = checked((int)analogCount);
            var status = checked((int)statusCount);
            var firstStatusLine = 2 + analog;
            if (firstStatusLine < 0 || firstStatusLine >= lines.Length)
                return new Dictionary<uint, string>();

            var result = new Dictionary<uint, string>();
            for (var offset = 0; offset < status && firstStatusLine + offset < lines.Length; offset++)
            {
                var fields = ParseCsvLine(lines[firstStatusLine + offset]);
                if (fields.Count < 2) continue;

                var fallbackIndex = checked((uint)offset);
                var index = fallbackIndex;
                if (int.TryParse(fields[0].Trim(), out var oneBased) && oneBased > 0 && oneBased <= status)
                    index = checked((uint)(oneBased - 1));

                var label = Normalize(fields[1]);
                if (label.Length > 0)
                    result[index] = label;
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException or OverflowException)
        {
            return new Dictionary<uint, string>();
        }
    }

    internal static string DecodeCfg(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return string.Empty;

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Latin-1 is deterministic and built into .NET. The accented letters used by common
            // French/German relay labels occupy the same byte positions as Windows-1252.
            return Encoding.Latin1.GetString(bytes);
        }
    }

    internal static IReadOnlyList<string> ParseCsvLine(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return Array.Empty<string>();

        var fields = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
                continue;
            }

            if (ch == ',' && !quoted)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(ch);
        }
        fields.Add(current.ToString());
        return fields;
    }

    private static string[] SplitLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static string Normalize(string value)
        => (value ?? string.Empty).Trim().Trim('\u0000').Normalize(NormalizationForm.FormC);
}
