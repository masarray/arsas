namespace ArIED61850Tester.Protocol.Osi;

public static class CotpConnectRequest
{
    // Minimal RFC1006/COTP connection request placeholder for the next native phase.
    // Kept separate from TPKT so the OSI layers remain testable and independent.
    public static byte[] BuildDefault()
    {
        return new byte[]
        {
            0x11, // COTP length
            0xE0, // CR TPDU
            0x00, 0x00, // destination reference
            0x00, 0x01, // source reference
            0x00, // class 0
            0xC0, 0x01, 0x0A, // TPDU size parameter
            0xC1, 0x02, 0x00, 0x01, // source TSAP placeholder
            0xC2, 0x02, 0x00, 0x01  // destination TSAP placeholder
        };
    }
}
