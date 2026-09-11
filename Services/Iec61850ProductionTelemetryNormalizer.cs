namespace ArIED61850Tester.Services;

/// <summary>
/// Single production boundary between decoded IEC 61850 network data and UI/runtime state.
/// It preserves a valid source timestamp, never fabricates PC time as relay evidence, and
/// never promotes missing/unknown quality to Good.
/// </summary>
public static class Iec61850ProductionTelemetryNormalizer
{
    public static Iec61850TelemetryEnvelope FromReadObject(
        object? value,
        string dataType,
        string unit,
        DateTimeOffset? receivedAtUtc = null,
        string? sourceReference = null,
        string? readReference = null)
    {
        if (value is Iec61850ReadValue rich)
            return Iec61850TelemetryEnvelope.FromReadValue(rich, receivedAtUtc);

        var display = Iec61850ValueFormatter.Format(value, dataType, unit);
        return FromComponents(
            value,
            display,
            quality: null,
            deviceTimestamp: null,
            receivedAtUtc ?? DateTimeOffset.UtcNow,
            sourceReference,
            readReference);
    }

    public static Iec61850TelemetryEnvelope FromComponents(
        object? value,
        string? displayValue,
        string? quality,
        string? deviceTimestamp,
        DateTimeOffset receivedAtUtc,
        string? sourceReference = null,
        string? readReference = null)
        => Iec61850TelemetryEnvelope.FromReadValue(new Iec61850ReadValue
        {
            Value = value,
            DisplayValue = displayValue?.Trim() ?? string.Empty,
            Quality = quality?.Trim() ?? string.Empty,
            DeviceTimestamp = deviceTimestamp?.Trim() ?? string.Empty,
            SourceReference = sourceReference?.Trim() ?? string.Empty,
            ReadReference = readReference?.Trim() ?? string.Empty,
            ReceivedAtUtc = receivedAtUtc
        }, receivedAtUtc);

    public static string SourceTimestampTextOrUnknown(
        Iec61850TelemetryEnvelope envelope,
        string? originalTimestamp)
        => envelope.SourceTimestampUtc.HasValue
            ? originalTimestamp?.Trim() ?? "-"
            : "-";
}
