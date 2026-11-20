using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace ArIED61850Tester.Services;

/// <summary>
/// Opt-in EventPipe/ETW diagnostics for SG7-size WPF model navigation.
/// Disabled by default: no file writes, new UI controls, or network operations.
/// Capture provider ARSAS-IEDBrowser-Performance with PerfView/dotnet-trace.
/// </summary>
[EventSource(Name = "ARSAS-IEDBrowser-Performance")]
internal sealed class IedBrowserPerformanceEvents : EventSource
{
    internal static readonly IedBrowserPerformanceEvents Log = new();

    [Event(1, Level = EventLevel.Informational)]
    public void TimedPhase(string phase, string device, double elapsedMs, int itemCount)
    {
        if (IsEnabled())
            WriteEvent(1, phase, device, elapsedMs, itemCount);
    }
}

/// <summary>
/// Measures an existing operation without scheduling, mutation or instrumentation
/// overhead when no external diagnostics listener is attached.
/// </summary>
internal readonly struct IedBrowserPerformanceProbe : IDisposable
{
    private readonly string? _phase;
    private readonly string? _device;
    private readonly int _items;
    private readonly long _started;

    private IedBrowserPerformanceProbe(string phase, string device, int items)
    {
        _phase = phase;
        _device = device;
        _items = items;
        _started = Stopwatch.GetTimestamp();
    }

    internal static IedBrowserPerformanceProbe Measure(string phase, string? device, int items = 0)
        => IedBrowserPerformanceEvents.Log.IsEnabled()
            ? new IedBrowserPerformanceProbe(phase, device ?? "", items)
            : default;

    public void Dispose()
    {
        if (_started == 0) return;
        IedBrowserPerformanceEvents.Log.TimedPhase(
            _phase ?? "", _device ?? "",
            Stopwatch.GetElapsedTime(_started).TotalMilliseconds, _items);
    }
}
