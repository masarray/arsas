using System.Collections.ObjectModel;
using System.Globalization;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record NativeFatPrintPreviewRow(
    string Signal,
    string IecTelegram,
    string Quality,
    string LiveValue,
    string Value1,
    string Value1Timestamp,
    string Value2,
    string Value2Timestamp,
    string Result);

/// <summary>
/// Immutable selected-IED-only report input for native Engineering FAT.
/// Capture copies the exact native FAT row order plus sparse evidence overlay. No live
/// row/evidence object is retained after Capture returns and acquisition is never restarted.
/// </summary>
public sealed class NativeFatPrintPreviewSnapshot
{
    private readonly ReadOnlyCollection<NativeFatPrintPreviewRow> _rows;

    private NativeFatPrintPreviewSnapshot(
        DateTimeOffset capturedAt,
        string deviceId,
        string iedName,
        string ipAddress,
        int port,
        IReadOnlyCollection<NativeFatPrintPreviewRow> rows)
    {
        CapturedAt = capturedAt;
        DeviceId = deviceId;
        IedName = iedName;
        IpAddress = ipAddress;
        Port = port;
        _rows = Array.AsReadOnly(rows.ToArray());
    }

    public DateTimeOffset CapturedAt { get; }
    public string DeviceId { get; }
    public string IedName { get; }
    public string IpAddress { get; }
    public int Port { get; }
    public IReadOnlyList<NativeFatPrintPreviewRow> Rows => _rows;
    public int CompleteCount => _rows.Count(row => HasEvidence(row.Value1) && HasEvidence(row.Value2));
    public string ProgressText => $"{CompleteCount}/{_rows.Count} complete";

    public static NativeFatPrintPreviewSnapshot Capture(
        Iec61850MonitorDevice device,
        NativeFatIedSessionCacheState cache)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(cache);

        var rows = device.Points.Select(point =>
        {
            var value1 = NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Value1);
            var value2 = NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Value2);
            var capture1 = NativeFatCanonicalEvidenceOverlay.ReadCapture(cache, point, NativeFatEvidenceField.Value1);
            var capture2 = NativeFatCanonicalEvidenceOverlay.ReadCapture(cache, point, NativeFatEvidenceField.Value2);
            var result = NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Result);

            return new NativeFatPrintPreviewRow(
                point.SignalName ?? string.Empty,
                Copy(point.IecTelegram),
                Copy(point.Quality),
                Display(point.DisplayValue),
                Display(value1),
                DisplayTimestamp(capture1),
                Display(value2),
                DisplayTimestamp(capture2),
                Display(result));
        }).ToArray();

        return new NativeFatPrintPreviewSnapshot(
            DateTimeOffset.Now,
            Copy(device.DeviceId),
            Copy(device.Name),
            Copy(device.IpAddress),
            device.Port,
            rows);
    }

    private static string DisplayTimestamp(FatValueEvidence? evidence)
    {
        if (evidence is null)
            return "—";
        var timestamp = evidence.IedTimestamp ?? evidence.CapturedAt;
        return timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    private static bool HasEvidence(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim() != "—";

    private static string Copy(string? value)
        => value?.Trim() ?? string.Empty;

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
