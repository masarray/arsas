using System.IO.Compression;
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
        var rowCount = Math.Max(0, csv.Split('\n').Length -
            (csv.EndsWith('\n') ? 2 : 1));
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
}
