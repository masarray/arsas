using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

/// <summary>
/// Creates immutable FAT Value 1 / Value 2 evidence from a trustworthy live observation.
/// Pointer promotion is intentionally owned by the session controller so journal writes
/// happen first and batch recapture can remain transactional.
/// </summary>
public static class FatOperatorSnapshotCaptureService
{
    public static FatValueEvidence Capture(
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

        var evidence = CreateEvidence(slot, observation, FatEvidenceCaptureKind.OperatorSnapshot);
        signal.SetCurrentEvidence(evidence);
        return evidence;
    }

    public static FatValueEvidence CreateEvidence(
        FatValueSlot slot,
        FatLiveValueObservation observation)
        => CreateEvidence(slot, observation, FatEvidenceCaptureKind.OperatorSnapshot);

    /// <summary>
    /// Creates an immutable capture record without mutating a current evidence pointer.
    /// OperatorRecapture is valid for every FAT signal type; AutomaticStableSnapshot is
    /// reserved for conservative analog/other settling capture.
    /// </summary>
    public static FatValueEvidence CreateEvidence(
        FatValueSlot slot,
        FatLiveValueObservation observation,
        FatEvidenceCaptureKind captureKind)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (string.IsNullOrWhiteSpace(observation.RawValue) ||
            observation.RawValue.Trim() is "-" or "—")
        {
            throw new InvalidOperationException("The FAT signal does not have a readable live value to capture.");
        }

        if (captureKind == FatEvidenceCaptureKind.AutomaticTransition)
            throw new ArgumentOutOfRangeException(nameof(captureKind), "Transition evidence is created by the digital transition evaluator.");

        return new FatValueEvidence(
            Guid.NewGuid(),
            slot,
            captureKind,
            observation.RawValue.Trim(),
            observation.CapturedAt,
            observation.IedTimestamp,
            string.IsNullOrWhiteSpace(observation.Quality) ? "Unknown" : observation.Quality.Trim(),
            string.IsNullOrWhiteSpace(observation.AcquisitionSource) ? "Unknown" : observation.AcquisitionSource.Trim(),
            observation.Sequence,
            observation.ConnectionGeneration);
    }
}
