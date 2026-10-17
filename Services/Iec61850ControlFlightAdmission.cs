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


/// <summary>
/// One physical MMS association's command lifetime. Retirement atomically
/// prevents admission and never equates WaitAsync cancellation with release
/// of the underlying native operation.
/// </summary>
public sealed class Iec61850ControlSessionLease
{
    private readonly object _sync = new();
    private TaskCompletionSource<bool>? _activeFlightReleased;
    private int _active;
    private int _quarantine;
    private int _retired;

    public bool IsActive => Volatile.Read(ref _active) != 0;
    public bool IsRetired => Volatile.Read(ref _retired) != 0;
    public bool IsUnavailable =>
        IsRetired || Iec61850ControlFlightAdmission.IsUnavailable(ref _active, ref _quarantine);

    public bool TryEnter()
    {
        lock (_sync)
        {
            if (_retired != 0 ||
                !Iec61850ControlFlightAdmission.TryEnter(ref _active, ref _quarantine))
                return false;
            _activeFlightReleased = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
    }

    public void Quarantine()
        => Iec61850ControlFlightAdmission.Quarantine(ref _quarantine);

    public void Release()
    {
        lock (_sync)
        {
            Iec61850ControlFlightAdmission.Release(ref _active);
            _activeFlightReleased?.TrySetResult(true);
        }
    }

    /// <summary>
    /// Prevent future commands and return a drain which finishes only when
    /// the original admitted native operation has actually released its lease.
    /// </summary>
    public Task Retire()
    {
        lock (_sync)
        {
            Volatile.Write(ref _retired, 1);
            Quarantine();
            return _active != 0
                ? _activeFlightReleased!.Task
                : Task.CompletedTask;
        }
    }
}
