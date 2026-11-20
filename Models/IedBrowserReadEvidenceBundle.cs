using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArIED61850Tester.Models;

/// <summary>
/// Operator-exported, bounded diagnostic packet from an existing read-evidence
/// CSV. No new MMS IO, packet capture, device control, memory cache or
/// unsolicited upload. The manifest explicitly labels missing on-wire proof.
/// </summary>
public static class IedBrowserReadEvidenceBundle
{
    public static byte[] Build(
        string csv, string appVersion, string engineVersion,
        long association, DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(csv);
        // CSV source is bounded by IedBrowserReadEvidenceCsv to 4096 targets.
        if (Encoding.UTF8.GetByteCount(csv) > 4_194_304)
            throw new ArgumentOutOfRangeException(nameof(csv),
                "Selected-page evidence exceeded the 4 MiB diagnostic boundary.");
        var rowCount = CountSelectedTargetRows(csv);
        // Hash the exact UTF-8 bytes written to the CSV ZIP entry, not a normalized copy.
        var csvBytes = new UTF8Encoding(false).GetBytes(csv);
        var csvSha256 = Convert.ToHexString(SHA256.HashData(csvBytes)).ToLowerInvariant();
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            static void Write(ZipArchive archive, string name, string body)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(body);
            }
            Write(archive, "read-evidence.csv", csv);
            Write(archive, "manifest.json", JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                CapturedAtUtc = capturedAtUtc,
                AppVersion = appVersion,
                EngineVersion = engineVersion,
                Association = association,
                SelectedTargetRows = rowCount,
                ReadEvidenceCsvSha256 = csvSha256,
                WireGvaStatus = "NOT_CAPTURED",
                WireInvokeStatus = "NOT_CAPTURED",
                UiProjectionStatus = "NOT_MEASURED",
                Scope = "selected-page-ledger-and-failure-evidence",
                PhysicalAcceptance = "UNVERIFIED",
                Note = "Only actual ledger observations authorize process values. " +
                       "Deferred means no proven MMS attempt; an unavailable peer path " +
                       "does not erase already routed reports."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Count logical CSV records, not physical lines: quoted diagnostic values
    /// may legally contain newlines. Refuse malformed quoting and more than
    /// 4096 selected targets rather than publishing a misleading manifest.
    /// The first CSV record is the header, not an observation.
    /// </summary>
    private static int CountSelectedTargetRows(string csv)
    {
        if (csv.Length == 0) return 0;
        var records = 0;
        var inQuotes = false;
        var afterQuote = false;
        var atFieldStart = true;
        var recordHasContent = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                        i++; // Escaped CSV quote belongs to the same field.
                    else
                    {
                        inQuotes = false;
                        afterQuote = true;
                    }
                }
                continue;
            }

            if (c is '\r' or '\n')
            {
                if (!recordHasContent)
                    throw new FormatException("Evidence CSV contains an empty record.");
                records++;
                if (records > 4097)
                    throw new ArgumentOutOfRangeException(nameof(csv),
                        "Selected-page evidence exceeds 4096 CSV target records.");
                if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                    i++;
                atFieldStart = true;
                afterQuote = false;
                recordHasContent = false;
                continue;
            }

            if (afterQuote)
            {
                if (c != ',')
                    throw new FormatException("Evidence CSV has unexpected data after a quoted field.");
                atFieldStart = true;
                afterQuote = false;
                recordHasContent = true;
                continue;
            }

            if (c == '"')
            {
                if (!atFieldStart)
                    throw new FormatException("Evidence CSV has an unexpected quote in an unquoted field.");
                inQuotes = true;
                atFieldStart = false;
            }
            else
                atFieldStart = c == ',';
            recordHasContent = true;
        }

        if (inQuotes)
            throw new FormatException("Evidence CSV has an unterminated quoted field.");
        if (recordHasContent) records++;
        if (records > 4097)
            throw new ArgumentOutOfRangeException(nameof(csv),
                "Selected-page evidence exceeds 4096 CSV target records.");
        return Math.Max(0, records - 1);
    }
}
