namespace ArIED61850Tester.Models.IoTesting;

/// <summary>
/// Generic live IEC 61850 value image used by FAT v2. Unlike the legacy digital
/// observation model, the raw value is intentionally not normalized to bool.
/// </summary>
public sealed record FatLiveValueObservation(
    string RawValue,
    DateTimeOffset CapturedAt,
    DateTimeOffset? IedTimestamp,
    string Quality,
    string AcquisitionSource,
    long Sequence,
    long ConnectionGeneration);
