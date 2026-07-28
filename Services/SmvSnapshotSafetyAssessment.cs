namespace ArIED61850Tester.Services;

/// <summary>
/// Safety-critical presentation assessment for bounded SV evidence. A publisher restart is a
/// discontinuity and must never be presented as a clean counter proof.
/// </summary>
public static class SmvSnapshotSafetyAssessment
{
    public static bool HasCounterAnomaly(SmvSnapshotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.GapTransitions > 0 ||
               result.DuplicateTransitions > 0 ||
               result.OutOfOrderTransitions > 0 ||
               result.RestartTransitions > 0;
    }

    public static bool IsCleanProof(SmvSnapshotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsComplete && !HasCounterAnomaly(result);
    }

    public static string BuildContinuityEvidence(SmvSnapshotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return $"gaps {result.GapTransitions} / missing {result.MissingSamples} / " +
               $"duplicate {result.DuplicateTransitions} / out-of-order {result.OutOfOrderTransitions} / " +
               $"restart {result.RestartTransitions}";
    }
}
