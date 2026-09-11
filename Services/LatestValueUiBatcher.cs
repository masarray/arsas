using System.Collections.Concurrent;

namespace ArIED61850Tester.Services;

/// <summary>
/// Coalesces only the UI projection of high-rate telemetry. Raw report/GOOSE/SOE processing
/// must remain upstream and event-by-event; this class intentionally keeps only the latest
/// visual value per key until the next bounded flush.
/// </summary>
public sealed class LatestValueUiBatcher<TKey, TValue> : IAsyncDisposable
    where TKey : notnull
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(150);

    private sealed record PendingValue(long Version, TValue Value);

    private readonly ConcurrentDictionary<TKey, PendingValue> _pending = new();
    private readonly Func<IReadOnlyList<TValue>, CancellationToken, ValueTask> _flushAsync;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _pump;
    private long _version;
    private long _published;
    private long _flushed;
    private int _disposeStarted;

    public LatestValueUiBatcher(
        Func<IReadOnlyList<TValue>, CancellationToken, ValueTask> flushAsync,
        TimeSpan? interval = null)
    {
        _flushAsync = flushAsync ?? throw new ArgumentNullException(nameof(flushAsync));
        _interval = interval ?? DefaultInterval;
        if (_interval < TimeSpan.FromMilliseconds(50) || _interval > TimeSpan.FromSeconds(2))
            throw new ArgumentOutOfRangeException(nameof(interval), "UI batch interval must stay between 50 ms and 2 s.");

        _pump = Task.Run(PumpAsync);
    }

    public long PublishedCount => Interlocked.Read(ref _published);
    public long FlushedCount => Interlocked.Read(ref _flushed);
    public long CoalescedCount => Math.Max(0, PublishedCount - FlushedCount - _pending.Count);
    public int PendingKeyCount => _pending.Count;

    public bool TryPublish(TKey key, TValue value)
    {
        if (Volatile.Read(ref _disposeStarted) != 0)
            return false;

        var version = Interlocked.Increment(ref _version);
        _pending[key] = new PendingValue(version, value);
        Interlocked.Increment(ref _published);
        return true;
    }

    public async ValueTask FlushNowAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _disposeStarted) != 0 && _pending.IsEmpty)
            return;

        var batch = DrainLatest();
        if (batch.Count == 0)
            return;

        await _flushAsync(batch, cancellationToken).ConfigureAwait(false);
        Interlocked.Add(ref _flushed, batch.Count);
    }

    private async Task PumpAsync()
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                await FlushNowAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch
        {
            // The UI projection is non-authoritative. A presentation failure must not tear
            // down network acquisition; a later explicit FlushNowAsync can still drain data.
        }
    }

    private List<TValue> DrainLatest()
    {
        var batch = new List<TValue>(_pending.Count);
        var collection = (ICollection<KeyValuePair<TKey, PendingValue>>)_pending;

        foreach (var pair in _pending.ToArray())
        {
            // ICollection.Remove(KeyValuePair) is an atomic key+value conditional removal for
            // ConcurrentDictionary. If a newer value arrived after ToArray(), removal fails
            // and the newer value remains pending for the next flush instead of being lost.
            if (collection.Remove(pair))
                batch.Add(pair.Value.Value);
        }

        return batch;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        _stop.Cancel();
        try
        {
            await _pump.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        // Best-effort final visual delivery. Disposal is not a process-evidence boundary;
        // authoritative report/SOE data has already been processed upstream.
        try
        {
            var finalBatch = DrainLatest();
            if (finalBatch.Count > 0)
            {
                await _flushAsync(finalBatch, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Add(ref _flushed, finalBatch.Count);
            }
        }
        catch
        {
        }
        finally
        {
            _pending.Clear();
            _stop.Dispose();
        }
    }
}
