namespace ArIED61850Tester.Services;

/// <summary>
/// Bounded recovery policy for long-running IEC 61850 monitoring sessions.
/// Transport recovery is intentionally independent from Report Control Block re-arming:
/// ACSE/MMS must become usable first, then reporting is restored by the normal background
/// acquisition pipeline.
/// </summary>
public static class SmartReconnectPolicy
{
    public static TimeSpan ClientCleanupBudget => TimeSpan.FromMilliseconds(750);
    public static TimeSpan ConnectBudget => TimeSpan.FromSeconds(10);
    public static TimeSpan InitialAssociationRetryDelay => TimeSpan.FromMilliseconds(750);
    public static TimeSpan ReportRearmDelay => TimeSpan.FromSeconds(3);
    public static TimeSpan ReportRearmDeadline => TimeSpan.FromSeconds(5);
    public static TimeSpan RecoveryWarmupDuration => TimeSpan.FromSeconds(10);

    public static TimeSpan GetRetryDelay(int consecutiveFailureCount)
    {
        var attempt = Math.Max(1, consecutiveFailureCount);
        var seconds = attempt switch
        {
            1 => 1,
            2 => 2,
            3 => 4,
            4 => 8,
            5 => 15,
            _ => 30
        };
        return TimeSpan.FromSeconds(seconds);
    }

    public static int ApplyRecoveryPollFloor(int intervalMs, bool recoveryWarmup)
    {
        var bounded = Math.Clamp(intervalMs, 50, 600000);
        return recoveryWarmup ? Math.Max(bounded, 2000) : bounded;
    }

    public static int GetRecoveryStaggerDelayMs(int zeroBasedIndex)
    {
        var index = Math.Max(0, zeroBasedIndex);
        return Math.Min(2000, index * 20);
    }
}
