using System.Globalization;
using System.Text;

namespace ArIED61850Tester.Models;

/// <summary>
/// Bounded, Excel-compatible TSV projection. A copy is an explicit operator
/// action, never a per-frame UI task. It preserves row/column order and quotes
/// multi-line values; no data is copied silently when a limit is exceeded.
/// </summary>
public static class EngineeringClipboardText
{
    public const int MaxRows = 3000;
    public const int MaxCells = 36000;
    public const int MaxCharacters = 512000;

    public static bool TryBuild(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows,
        bool includeHeaders,
        out string text,
        out string reason)
    {
        text = string.Empty;
        reason = string.Empty;
        if (headers.Count == 0)
        {
            reason = "No visible columns are available for copying.";
            return false;
        }
        if (headers.Count > MaxCells)
        {
            reason = "The selected table has too many columns.";
            return false;
        }

        var builder = new StringBuilder(Math.Min(4096, MaxCharacters));
        var rowCount = 0;
        var cellCount = 0;
        if (includeHeaders && !AppendRow(builder, headers, headers.Count, prependNewline: false))
        {
            reason = "Clipboard data exceeds the safe size limit.";
            return false;
        }

        foreach (var row in rows)
        {
            if (++rowCount > MaxRows || (long)cellCount + headers.Count > MaxCells)
            {
                reason = "The selection exceeds 3,000 rows or 36,000 cells. Use Export CSV for larger datasets.";
                return false;
            }
            if (row.Count != headers.Count ||
                !AppendRow(builder, row, headers.Count, prependNewline: includeHeaders || rowCount > 1))
            {
                reason = row.Count != headers.Count
                    ? "Clipboard columns changed while preparing the selection."
                    : "Clipboard data exceeds the 512 KB safety limit. Use Export CSV for larger datasets.";
                return false;
            }
            cellCount += headers.Count;
        }

        if (rowCount == 0)
        {
            reason = "Select at least one data row or cell to copy.";
            return false;
        }
        text = builder.ToString();
        return true;
    }

    private static bool AppendRow(StringBuilder builder, IReadOnlyList<string?> fields,
        int count, bool prependNewline)
    {
        // Do not infer a previous row from StringBuilder.Length: a valid all-empty
        // first row has zero characters yet MUST retain its row separator.
        if (prependNewline) builder.Append("\r\n");
        for (var i = 0; i < count; i++)
        {
            if (i > 0) builder.Append('\t');
            var value = fields[i] ?? string.Empty;
            // Excel treats text beginning with formulas as executable cells.
            // Preserve numerical signs, but force formula-like engineering
            // names/labels to paste literally rather than execute.
            if (LooksLikeFormula(value))
                value = "'" + value;
            // Bound work before formatting untrusted or very large diagnostics.
            if (value.Length > MaxCharacters - builder.Length - 4)
                return false;
            var quoted = value.IndexOfAny(['\t', '\r', '\n', '"']) >= 0;
            if (quoted) builder.Append('"');
            builder.Append(quoted ? value.Replace("\"", "\"\"") : value);
            if (quoted) builder.Append('"');
            if (builder.Length > MaxCharacters) return false;
        }
        return builder.Length <= MaxCharacters;
    }

    internal static bool LooksLikeFormula(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value[0] is '=' or '@') return true;
        if (value[0] is not ('+' or '-')) return false;
        return !double.TryParse(value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out _);
    }
}
