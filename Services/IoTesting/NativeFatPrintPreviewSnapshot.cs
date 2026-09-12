using System.Collections.ObjectModel;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record NativeFatPrintPreviewRow(
    string Signal,
    string IecReference,
    string Type,
    string LiveValue,
    string Value1,
    string Value2,
    string Status,
    string Result);

/// <summary>
/// P3 immutable, selected-IED-only report input for native Engineering FAT.
/// Capture copies primitive display values from the canonical Engineering rows and
/// sparse evidence overlay. No live row/evidence object is retained after Capture returns.
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
    public int CompleteCount => _rows.Count(row =>
        row.Status.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase));
    public string ProgressText => $"{CompleteCount}/{_rows.Count} complete";

    public static NativeFatPrintPreviewSnapshot Capture(
        Iec61850MonitorDevice device,
        NativeFatIedSessionCacheState cache)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(cache);

        // Materialize in the current canonical Engineering row order. Every value below
        // is copied now; the preview never binds back to device.Points or EvidenceByRow.
        var rows = device.Points.Select(point =>
        {
            var value1 = NativeFatCanonicalEvidenceOverlay.ReadRaw(
                cache,
                point,
                NativeFatEvidenceField.Value1).Trim();
            var value2 = NativeFatCanonicalEvidenceOverlay.ReadRaw(
                cache,
                point,
                NativeFatEvidenceField.Value2).Trim();
            var result = NativeFatCanonicalEvidenceOverlay.ReadRaw(
                cache,
                point,
                NativeFatEvidenceField.Result).Trim();

            var status = !string.IsNullOrWhiteSpace(value1) && !string.IsNullOrWhiteSpace(value2)
                ? "COMPLETE"
                : !string.IsNullOrWhiteSpace(value1)
                    ? "WAITING V2"
                    : "WAITING V1";

            return new NativeFatPrintPreviewRow(
                Copy(point.SignalName),
                Copy(point.IecReference),
                Copy(point.IecDataType),
                Display(point.DisplayValue),
                Display(value1),
                Display(value2),
                status,
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

    private static string Copy(string? value)
        => value?.Trim() ?? string.Empty;

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
