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

    /// <summary>
    /// Bounds only the caller's wait, never the actual native lifetime.
    /// An unresolved native operation remains owned by its session until its
    /// own Task finishes; the caller must retain admission until then.
    /// </summary>
    public static async Task<T> WaitForNativeAsync<T>(
        Task<T> nativeFlight, TimeSpan deadline, CancellationToken waitCancellation,
        Action quarantine, Action cancelNative)
    {
        ArgumentNullException.ThrowIfNull(nativeFlight);
        ArgumentNullException.ThrowIfNull(quarantine);
        ArgumentNullException.ThrowIfNull(cancelNative);
        try
        {
            return await nativeFlight.WaitAsync(deadline, waitCancellation).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            quarantine();
            cancelNative();
            throw;
        }
        catch (OperationCanceledException) when (waitCancellation.IsCancellationRequested &&
                                                  !nativeFlight.IsCompleted)
        {
            quarantine();
            cancelNative();
            throw;
        }
    }

    public static void Quarantine(ref int quarantine)
        => Interlocked.Exchange(ref quarantine, 1);

    public static void Release(ref int active)
        => Interlocked.Exchange(ref active, 0);
}
