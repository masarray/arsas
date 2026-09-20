using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly object _dataSetCapabilityBuildSync = new();
    private readonly Dictionary<string, CancellationTokenSource> _dataSetCapabilityBuildTokens =
        new(StringComparer.OrdinalIgnoreCase);

    private void QueueDataSetCapabilityRefresh(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var model = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel;
        if (model is null)
            return;

        var generation = device.ModelGeneration;
        var source = device.SclWorkspace?.DesignModel is not null
            ? "OpenScl"
            : "SmartDiscovery";

        CancellationTokenSource cancellation;
        lock (_dataSetCapabilityBuildSync)
        {
            if (_dataSetCapabilityBuildTokens.Remove(device.DeviceId, out var previous))
            {
                // The superseded worker owns disposal in its own finally block. Cancelling
                // here is sufficient and avoids disposing a token source while that worker
                // is still observing the token.
                previous.Cancel();
            }

            cancellation = CancellationTokenSource.CreateLinkedTokenSource(_applicationCancellation.Token);
            _dataSetCapabilityBuildTokens[device.DeviceId] = cancellation;
        }

        _ = BuildDataSetCapabilityIndexAsync(
            device,
            model,
            generation,
            source,
            cancellation);
    }

    private async Task BuildDataSetCapabilityIndexAsync(
        Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument model,
        long generation,
        string source,
        CancellationTokenSource cancellation)
    {
        try
        {
            var index = await Iec61850DataSetCapabilityIndexBuilder.BuildAsync(
                model,
                generation,
                source,
                cancellation.Token);

            cancellation.Token.ThrowIfCancellationRequested();

            // Generation is the final authority. A late worker from an older SCL/discovery
            // model is silently discarded instead of repainting capability state with stale
            // DataSet/RCB information.
            device.TryApplyDataSetCapabilityIndex(index);
        }
        catch (OperationCanceledException)
        {
            // Superseded model generations are expected during reconnect/reopen.
        }
        finally
        {
            lock (_dataSetCapabilityBuildSync)
            {
                if (_dataSetCapabilityBuildTokens.TryGetValue(device.DeviceId, out var current) &&
                    ReferenceEquals(current, cancellation))
                {
                    _dataSetCapabilityBuildTokens.Remove(device.DeviceId);
                }
            }

            cancellation.Dispose();
        }
    }
}
