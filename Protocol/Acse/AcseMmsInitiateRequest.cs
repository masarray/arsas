using System.Collections.Generic;
using ArIED61850Tester.Protocol.Diagnostics;

namespace ArIED61850Tester.Protocol.Acse;

public sealed record AcseAssociationProfile(string Name, string Description, byte[] Payload);

public static class AcseMmsInitiateRequest
{
    /// <summary>
    /// Native native association profiles.
    ///
    /// IEC 61850 IEDs are not fully uniform in how strict they are about ACSE AP-title /
    /// AE-qualifier and ISO presentation negotiation. Keep the profiles explicit, small, and
    /// auditable; do not silently copy any third-party stack internals.
    /// </summary>
    public static IReadOnlyList<AcseAssociationProfile> BuildAssociationProfiles()
    {
        return new[]
        {
            BuildBalancedApTitleProfile(),
            BuildLegacyMinimalProfile()
        };
    }

    /// <summary>
    /// Profile with symmetric AP-title / AE-qualifier and MMS service capability value observed
    /// in many IEC 61850 client traces. This is tried first because some IEDs abort the older
    /// minimal profile when called AP-title is encoded as an empty OID.
    /// </summary>
    private static AcseAssociationProfile BuildBalancedApTitleProfile()
    {
        var payload = HexDump.Parse(
            "0D B6 05 06 13 01 00 16 01 02 14 02 00 02 33 02 00 01 34 02 00 01 C1 A0 " +
            "31 81 9D A0 03 80 01 01 A2 81 95 81 04 00 00 00 01 82 04 00 00 00 01 " +
            "A4 23 30 0F 02 01 01 06 04 52 01 00 01 30 04 06 02 51 01 30 10 02 01 03 " +
            "06 05 28 CA 22 02 01 30 04 06 02 51 01 61 62 30 60 02 01 01 A0 5B 60 59 " +
            "A1 07 06 05 28 CA 22 02 03 A2 07 06 05 29 01 87 67 01 A3 03 02 01 0C " +
            "A6 06 06 04 29 01 87 67 A7 03 02 01 0C BE 33 28 31 06 02 51 01 02 01 03 " +
            "A0 28 A8 26 80 03 00 FD E8 81 01 0A 82 01 0A 83 01 05 A4 16 80 01 01 " +
            "81 03 05 F1 00 82 0C 03 EE 1C 00 00 04 08 00 00 79 EF 18");

        return new AcseAssociationProfile(
            "BalancedApTitle",
            "IEC61850 MMS AARQ with called/calling AP-title and AE-qualifier populated.",
            payload);
    }

    /// <summary>
    /// Conservative profile retained as fallback for devices that accept the traditional compact
    /// association request. Some devices reply with Session Abort (0x19) to this profile; that is
    /// now treated as a real association failure, not as MMS-ready.
    /// </summary>
    private static AcseAssociationProfile BuildLegacyMinimalProfile()
    {
        var payload = HexDump.Parse(
            "0D B2 05 06 13 01 00 16 01 02 14 02 00 02 33 02 00 01 34 02 00 01 C1 9C " +
            "31 81 99 A0 03 80 01 01 A2 81 91 81 04 00 00 00 01 82 04 00 00 00 01 " +
            "A4 23 30 0F 02 01 01 06 04 52 01 00 01 30 04 06 02 51 01 30 10 02 01 03 " +
            "06 05 28 CA 22 02 01 30 04 06 02 51 01 61 5E 30 5C 02 01 01 A0 57 60 55 " +
            "A1 07 06 05 28 CA 22 02 03 A2 02 06 00 A3 03 02 01 0C A6 07 06 05 29 87 67 01 01 " +
            "A7 03 02 01 0C BE 33 28 31 06 02 51 01 02 01 03 A0 28 A8 26 80 03 00 FD E8 " +
            "81 01 0A 82 01 0A 83 01 06 A4 16 80 01 01 81 03 05 F1 00 82 0C 03 EE 08 00 00 04 00 00 00 01 E7 18");

        return new AcseAssociationProfile(
            "LegacyMinimal",
            "Compact IEC61850 MMS AARQ fallback profile.",
            payload);
    }

    /// <summary>
    /// Backward-compatible helper for older call sites.
    /// </summary>
    public static byte[] BuildDefaultAssociationPayload() => BuildAssociationProfiles()[0].Payload;
}
