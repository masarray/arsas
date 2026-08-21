using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

internal sealed record DynamicReportObservedCommandIntent(
    Iec61850MonitorDevice Device,
    SignalDefinition Signal,
    string RequestedValue,
    string Source,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// Observer-only command-intent bus used by G2.5-A2.1. Publishers never depend on
/// subscribers and every subscriber exception is contained so commissioning
/// observability can never disturb the existing control transaction.
/// </summary>
internal static class DynamicReportCommandIntentObservation
{
    private static readonly object Sync = new();
    private static readonly List<Action<DynamicReportObservedCommandIntent>> Subscribers = new();

    internal static IDisposable Subscribe(Action<DynamicReportObservedCommandIntent> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (Sync)
            Subscribers.Add(subscriber);
        return new Subscription(subscriber);
    }

    internal static void Publish(DynamicReportObservedCommandIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        Action<DynamicReportObservedCommandIntent>[] snapshot;
        lock (Sync)
            snapshot = Subscribers.ToArray();

        foreach (var subscriber in snapshot)
        {
            try
            {
                subscriber(intent);
            }
            catch
            {
                // Fail open for the user's existing control command. A diagnostic
                // observer must never throw into the routed Button.Click/control path.
            }
        }
    }

    private static void Unsubscribe(Action<DynamicReportObservedCommandIntent> subscriber)
    {
        lock (Sync)
            Subscribers.Remove(subscriber);
    }

    private sealed class Subscription(Action<DynamicReportObservedCommandIntent> subscriber) : IDisposable
    {
        private Action<DynamicReportObservedCommandIntent>? _subscriber = subscriber;

        public void Dispose()
        {
            var value = Interlocked.Exchange(ref _subscriber, null);
            if (value is not null)
                Unsubscribe(value);
        }
    }
}
