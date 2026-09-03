namespace ArIED61850Tester.Services;

/// <summary>
/// Bounded recovery policy for long-running IEC 61850 monitoring sessions.
/// Transport recovery is intentionally independent from Report Control Block re-arming:
/// ACSE/MMS must become usable first, then reporting is restored by the normal background
/// acquisition pipeline.
/// </summary>
public static class SmartReconnectPolicy
{
    public static TimeSpan ClientCleanupBudget => TimeSpan.FromMilliseconds(500);
    public static TimeSpan ConnectBudget => TimeSpan.FromSeconds(2);
    public static TimeSpan InitialAssociationRetryDelay => TimeSpan.FromMilliseconds(500);
    public static TimeSpan ReportRearmDelay => TimeSpan.FromSeconds(1);
    public static TimeSpan ReportRearmDeadline => TimeSpan.FromSeconds(3);
    public static TimeSpan RecoveryWarmupDuration => TimeSpan.FromSeconds(3);

    public static TimeSpan GetRetryDelay(int consecutiveFailureCount)
    {
        var attempt = Math.Max(1, consecutiveFailureCount);
        return attempt switch
        {
            1 => TimeSpan.FromMilliseconds(500),
            2 => TimeSpan.FromSeconds(1),
            _ => TimeSpan.FromSeconds(2)
        };
    }

    public static int ApplyRecoveryPollFloor(int intervalMs, bool recoveryWarmup)
    {
        var bounded = Math.Clamp(intervalMs, 50, 600000);
        return recoveryWarmup ? Math.Max(bounded, 500) : bounded;
    }

    public static int GetRecoveryStaggerDelayMs(int zeroBasedIndex)
    {
        var index = Math.Max(0, zeroBasedIndex);
        return Math.Min(500, index * 10);
    }
}
