using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Captures the value currently read from the IED when the FAT operator explicitly
/// confirms Value 1 or Value 2. No injected/reference value or tolerance exists here;
/// this first public milestone proves trustworthy reading capture only.
/// </summary>
public static class FatOperatorSnapshotCaptureService
{
    public static FatValueEvidence CreateEvidence(
        FatVerificationSignal signal,
        FatValueSlot slot,
        FatLiveValueObservation observation)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(observation);

        if (!signal.IsIncludedInFat)
        {
            throw new InvalidOperationException(
                $"FAT signal '{signal.SignalName}' is removed from the active FAT scope and cannot capture evidence until it is restored.");
        }

        if (signal.CaptureMode != FatCaptureMode.OperatorSnapshot)
        {
            throw new InvalidOperationException(
                $"FAT signal '{signal.SignalName}' uses {signal.CaptureMode} capture and cannot be manually snapshotted.");
        }

        if (string.IsNullOrWhiteSpace(observation.RawValue) ||
            observation.RawValue.Trim() is "-" or "—")
        {
            throw new InvalidOperationException(
                $"FAT signal '{signal.SignalName}' does not have a readable live value to capture.");
        }

        return new FatValueEvidence(
            Guid.NewGuid(),
            slot,
            FatEvidenceCaptureKind.OperatorSnapshot,
            observation.RawValue.Trim(),
            observation.CapturedAt,
            observation.IedTimestamp,
            string.IsNullOrWhiteSpace(observation.Quality) ? "Unknown" : observation.Quality.Trim(),
            string.IsNullOrWhiteSpace(observation.AcquisitionSource) ? "Unknown" : observation.AcquisitionSource.Trim(),
            observation.Sequence,
            observation.ConnectionGeneration);
    }

    public static FatValueEvidence Capture(
        FatVerificationSignal signal,
        FatValueSlot slot,
        FatLiveValueObservation observation)
    {
        var evidence = CreateEvidence(signal, slot, observation);
        signal.SetCurrentEvidence(evidence);
        return evidence;
    }
}
