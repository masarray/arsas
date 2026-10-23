using System.Runtime.CompilerServices;
using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// One declaration-only IEC 61850 schema index for a concrete immutable model.
/// Browser, Read projection and future workspace adapters must share the SAME
/// index instance. Observations, sessions and process values are never stored.
/// Weak model identity allows replaced discovery/SCL models to be collected.
/// </summary>
public static class SharedIedSchemaIndex
{
    private static readonly ConditionalWeakTable<LiveIedModelDiscoveryDocument,
        Lazy<Task<IedBrowserSchemaIndex>>> Models = new();

    /// <summary>
    /// Thread-safe one-flight index creation off the WPF dispatcher. Calling
    /// this API only schedules/prepares; it never synchronously walks the model.
    /// </summary>
    public static Task<IedBrowserSchemaIndex> PrepareAsync(LiveIedModelDiscoveryDocument model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Models.GetValue(model, static source =>
            new Lazy<Task<IedBrowserSchemaIndex>>(
                () => Task.Run(() => new IedBrowserSchemaIndex(source)),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>
    /// Synchronous compatibility path for existing explicit read binding.
    /// Reuses the same prepared index; callers on the Dispatcher should prefer
    /// PrepareAsync and only inspect a successfully completed task.
    /// </summary>
    public static IedBrowserSchemaIndex GetForBinding(LiveIedModelDiscoveryDocument model)
        => PrepareAsync(model).GetAwaiter().GetResult();

    /// <summary>Explicit operator retry only; no background fault-retry storm.</summary>
    public static void ForgetFaulted(LiveIedModelDiscoveryDocument model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Models.TryGetValue(model, out var lazy) &&
            lazy.IsValueCreated && (lazy.Value.IsFaulted || lazy.Value.IsCanceled))
            Models.Remove(model);
    }

    public static bool TryGetPrepared(LiveIedModelDiscoveryDocument model,
        out IedBrowserSchemaIndex index)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Models.TryGetValue(model, out var lazy) &&
            lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
        {
            index = lazy.Value.Result;
            return true;
        }
        index = null!;
        return false;
    }
}
