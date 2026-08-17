namespace ArIED61850Tester.Services;

/// <summary>
/// Determines how aggressively MMS verifies a point while IEC 61850 reporting is being
/// established. Report assignment or GI/integrity traffic alone is not proof that dchg
/// delivery works. Until an actual report change edge has been verified, MMS keeps the
/// configured polling cadence so live values and SOE fallback cannot silently freeze.
/// </summary>
public static class ReportVerificationPollingPolicy
{
    public static int GetIntervalMs(
        int configuredPollingIntervalMs,
        bool isFastPoint,
        bool reportAssigned,
        bool reportTrafficSeen,
        bool reportChangeVerified)
    {
        var configured = Math.Clamp(configuredPollingIntervalMs <= 0 ? 1000 : configuredPollingIntervalMs, 50, 600000);

        // Fail-safe contract: merely arming an RCB, or seeing its initial GI/integrity
        // image, must never slow the MMS fallback. Only a real dchg/qchg/dupd edge proves
        // that event delivery is trustworthy enough to reduce verification traffic.
        if (!reportAssigned || !reportTrafficSeen || !reportChangeVerified)
            return configured;

        var minimum = isFastPoint ? 10000 : 30000;
        return Math.Clamp(Math.Max(configured * 15, minimum), minimum, 60000);
    }
}
