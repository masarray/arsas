using System.Globalization;

namespace ArIED61850Tester.Services;

/// <summary>
/// Normalized IEC 61850 telemetry boundary used between network decoding and application logic.
/// Missing/ambiguous data is never promoted to a valid process value: the source timestamp
/// stays unknown and the quality is forced Invalid while ReceivedAtUtc records local receipt.
/// </summary>
public enum Iec61850TelemetryQualityState
{
    Good,
    Questionable,
    Invalid
}

public readonly record struct Iec61850TelemetryEnvelope(
    object? Value,
    string DisplayValue,
    Iec61850TelemetryQualityState QualityState,
    string QualityText,
    DateTimeOffset? SourceTimestampUtc,
    DateTimeOffset ReceivedAtUtc,
    string SourceReference,
    string Diagnostic)
{
    public bool HasProcessValue => Value is not null ||
                                   (!string.IsNullOrWhiteSpace(DisplayValue) && DisplayValue != "-");
    public bool IsValid => HasProcessValue && QualityState != Iec61850TelemetryQualityState.Invalid;

    public static Iec61850TelemetryEnvelope FromReadValue(
        Iec61850ReadValue? read,
        DateTimeOffset? receivedAtUtc = null)
    {
        var received = receivedAtUtc ?? DateTimeOffset.UtcNow;
        if (read is null)
        {
            return Invalid(
                received,
                sourceReference: string.Empty,
                diagnostic: "IEC 61850 read returned no value object.");
        }

        var display = read.DisplayValue?.Trim() ?? string.Empty;
        var hasValue = read.Value is not null || (display.Length > 0 && display != "-");
        var qualityText = NormalizeQualityText(read.Quality);
        var qualityState = ClassifyQuality(qualityText, hasValue);
        var sourceTimestamp = TryParseSourceTimestampUtc(read.DeviceTimestamp);
        var sourceReference = FirstNonEmpty(read.SourceReference, read.ReadReference);

        var diagnostic = qualityState == Iec61850TelemetryQualityState.Invalid
            ? hasValue
                ? $"Telemetry quality is invalid ({qualityText})."
                : "Telemetry contains no process value."
            : sourceTimestamp is null && read.HasDeviceTimestamp
                ? "Device timestamp was present but could not be parsed safely; source timestamp remains unknown."
                : string.Empty;

        return new Iec61850TelemetryEnvelope(
            read.Value,
            display,
            qualityState,
            qualityText,
            sourceTimestamp,
            received,
            sourceReference,
            diagnostic);
    }

    public static Iec61850TelemetryEnvelope Invalid(
        DateTimeOffset receivedAtUtc,
        string sourceReference,
        string diagnostic,
        object? safePlaceholder = null)
        => new(
            safePlaceholder,
            safePlaceholder?.ToString() ?? "-",
            Iec61850TelemetryQualityState.Invalid,
            "Invalid",
            null,
            receivedAtUtc,
            sourceReference?.Trim() ?? string.Empty,
            diagnostic?.Trim() ?? string.Empty);

    internal static DateTimeOffset? TryParseSourceTimestampUtc(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0 || text == "-")
            return null;

        // IEC 61850 timestamps are UTC-based. Only publish a parsed source timestamp when
        // parsing succeeds; never substitute DateTime.Now/ReceivedAtUtc for missing data.
        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return null;
        }

        return parsed.ToUniversalTime();
    }

    private static Iec61850TelemetryQualityState ClassifyQuality(string quality, bool hasValue)
    {
        if (!hasValue)
            return Iec61850TelemetryQualityState.Invalid;

        if (quality.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("bad", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("outofrange", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("out-of-range", StringComparison.OrdinalIgnoreCase))
        {
            return Iec61850TelemetryQualityState.Invalid;
        }

        if (quality.Length == 0 || quality == "-" ||
            quality.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("questionable", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("olddata", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("old-data", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("substituted", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return Iec61850TelemetryQualityState.Questionable;
        }

        return Iec61850TelemetryQualityState.Good;
    }

    private static string NormalizeQualityText(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length == 0 ? "Unknown" : text;
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.Select(value => value?.Trim() ?? string.Empty)
            .FirstOrDefault(value => value.Length > 0) ?? string.Empty;
}
