using System.Globalization;

namespace ArIED61850Tester.Services;

/// <summary>
/// Normalized IEC 61850 telemetry boundary used between network decoding and application logic.
/// Missing/ambiguous data is never promoted to a valid process value: the source timestamp
/// stays unknown and quality is preserved conservatively while ReceivedAtUtc records local receipt.
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
    public bool HasProcessValue => IsProcessValuePresent(Value, DisplayValue);

    /// <summary>
    /// True only when both the process value and IEC quality are explicitly Good.
    /// Questionable data may still be presented to an engineer, but it is not promoted
    /// to valid evidence by this boundary.
    /// </summary>
    public bool IsValid => HasProcessValue && QualityState == Iec61850TelemetryQualityState.Good;

    public bool IsUsable => HasProcessValue && QualityState != Iec61850TelemetryQualityState.Invalid;

    public static Iec61850TelemetryEnvelope FromReadValue(
        Iec61850ReadValue? read,
        DateTimeOffset? receivedAtUtc = null)
    {
        // Preserve the timestamp captured when the read projection was created. Normalization
        // can happen later after queuing/batching and must not move local receipt evidence
        // forward to the conversion time. UtcNow is only a last resort for a null read.
        var received = receivedAtUtc ?? read?.ReceivedAtUtc ?? DateTimeOffset.UtcNow;
        if (read is null)
        {
            return Invalid(
                received,
                sourceReference: string.Empty,
                diagnostic: "IEC 61850 read returned no value object.");
        }

        var display = read.DisplayValue?.Trim() ?? string.Empty;
        var hasValue = IsProcessValuePresent(read.Value, display);
        var qualityText = NormalizeQualityText(read.Quality);
        var qualityState = ClassifyQuality(qualityText, hasValue);
        var sourceTimestamp = TryParseSourceTimestampUtc(read.DeviceTimestamp);
        var sourceReference = FirstNonEmpty(read.SourceReference, read.ReadReference);

        var diagnostic = qualityState == Iec61850TelemetryQualityState.Invalid
            ? hasValue
                ? $"Telemetry quality is invalid ({qualityText})."
                : "Telemetry contains no process value."
            : qualityState == Iec61850TelemetryQualityState.Questionable
                ? $"Telemetry quality is not proven Good ({qualityText})."
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

        // ARIEC61850's decoded IEC UtcTime display can be zone-less even though IEC UtcTime
        // is semantically UTC. AssumeUniversal therefore preserves engine semantics here;
        // parsing failure still stays null and is never replaced by ReceivedAtUtc/PC time.
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

    private static bool IsProcessValuePresent(object? value, string? displayValue)
    {
        if (value is not null)
            return true;

        var display = displayValue?.Trim() ?? string.Empty;
        return display.Length > 0 && display != "-";
    }

    private static Iec61850TelemetryQualityState ClassifyQuality(string quality, bool hasValue)
    {
        if (!hasValue)
            return Iec61850TelemetryQualityState.Invalid;

        // Never infer Good from an unknown/vendor token. IEC validity is only considered
        // Good when the decoder explicitly said Good. This prevents missing or future
        // quality representations from being silently promoted to trustworthy evidence.
        if (quality.Equals("Good", StringComparison.OrdinalIgnoreCase))
            return Iec61850TelemetryQualityState.Good;

        if (quality.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("bad", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("reserved", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("outofrange", StringComparison.OrdinalIgnoreCase) ||
            quality.Contains("out-of-range", StringComparison.OrdinalIgnoreCase))
        {
            return Iec61850TelemetryQualityState.Invalid;
        }

        return Iec61850TelemetryQualityState.Questionable;
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
