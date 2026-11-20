using System.Collections.Concurrent;

namespace ArIED61850Tester.Models;

/// <summary>
/// Lossless bounded-by-request Read result handoff. Report() enqueues inline on
/// the producer thread (unlike Progress<T>, which posts to WPF asynchronously).
/// The UI owns the consumer and drains once per dispatcher batch plus once at
/// operation completion. No MMS operations or second result-value cache.
/// </summary>
public sealed class IedBrowserReadResultInbox : IProgress<IedBrowserReadResult>
{
    private readonly ConcurrentQueue<IedBrowserReadResult> _pending = new();
    private readonly Action _scheduleDrain;
    private int _scheduled;

    public IedBrowserReadResultInbox(Action scheduleDrain)
        => _scheduleDrain = scheduleDrain ?? throw new ArgumentNullException(nameof(scheduleDrain));

    public void Report(IedBrowserReadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _pending.Enqueue(result);
        ScheduleIfNeeded();
    }

    /// <summary>
    /// Consume every result currently available; in-flight producers may enqueue
    /// further items and will trigger another notification without a lost wakeup.
    /// Called only from the owner UI thread, never on MMS worker threads.
    /// </summary>
    public int Drain(Action<IedBrowserReadResult> consume)
    {
        ArgumentNullException.ThrowIfNull(consume);
        var count = 0;
        try
        {
            while (_pending.TryDequeue(out var result))
            {
                consume(result);
                count++;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _scheduled, 0);
            if (!_pending.IsEmpty)
                ScheduleIfNeeded();
        }
        return count;
    }

    private void ScheduleIfNeeded()
    {
        if (Interlocked.CompareExchange(ref _scheduled, 1, 0) == 0)
            _scheduleDrain();
    }
}
