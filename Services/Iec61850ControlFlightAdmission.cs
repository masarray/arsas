using System.Threading;

namespace ArIED61850Tester.Services;

/// <summary>
/// Atomic association-wide admission for one IEC 61850 control flight.
/// Quarantine is sticky for this session after an uncertain wire outcome.
/// This is ownership only: no value ledger, MMS protocol or second transport.
/// </summary>
public static class Iec61850ControlFlightAdmission
{
    public static bool IsUnavailable(ref int active, ref int quarantine)
        => Volatile.Read(ref active) != 0 || Volatile.Read(ref quarantine) != 0;

    public static bool TryEnter(ref int active, ref int quarantine)
    {
        if (Volatile.Read(ref quarantine) != 0 ||
            Interlocked.CompareExchange(ref active, 1, 0) != 0)
            return false;
        if (Volatile.Read(ref quarantine) == 0) return true;
        Interlocked.Exchange(ref active, 0);
        return false;
    }

    public static void Quarantine(ref int quarantine)
        => Interlocked.Exchange(ref quarantine, 1);

    public static void Release(ref int active)
        => Interlocked.Exchange(ref active, 0);
}
