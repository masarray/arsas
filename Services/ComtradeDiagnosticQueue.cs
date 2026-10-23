using System.Diagnostics;
using System.Threading.Channels;

namespace ArIED61850Tester.Services;

/// <summary>
/// Non-blocking diagnostic boundary for COMTRADE workstation failures. Interactive code only
/// performs TryWrite into a small bounded channel; formatting/output is handled by one background
/// reader. DropOldest prevents a repeated failure from creating unbounded memory pressure.
/// </summary>
internal static class ComtradeDiagnosticQueue
{
    internal const int Capacity = 128;

    private static readonly Channel<ComtradeDiagnosticEvent> Queue = Channel.CreateBounded<ComtradeDiagnosticEvent>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private static readonly Task DrainTask = Task.Run(DrainAsync);

    internal static bool TryEnqueue(
        string source,
        string code,
        string message,
        Exception? exception = null)
    {
        var entry = new ComtradeDiagnosticEvent(
            DateTimeOffset.Now,
            string.IsNullOrWhiteSpace(source) ? "COMTRADE" : source,
            string.IsNullOrWhiteSpace(code) ? "UNSPECIFIED" : code,
            message ?? string.Empty,
            exception);
        return Queue.Writer.TryWrite(entry);
    }

    private static async Task DrainAsync()
    {
        await foreach (var entry in Queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                Trace.WriteLine(
                    $"[{entry.Timestamp:O}] [{entry.Source}] [{entry.Code}] {entry.Message}" +
                    (entry.Exception is null ? string.Empty : Environment.NewLine + entry.Exception));
            }
            catch (Exception)
            {
                // Diagnostics must never become a second application failure. The bounded channel
                // continues draining even if an installed TraceListener rejects one entry.
            }
        }
    }

    private readonly record struct ComtradeDiagnosticEvent(
        DateTimeOffset Timestamp,
        string Source,
        string Code,
        string Message,
        Exception? Exception);
}
