using System.Reflection;
using ArIED61850Tester;

namespace ARSAS.Tests;

/// <summary>
/// An offline admission contract: none of these tests creates an MMS association,
/// sends an Operate or touches a relay. A null request MUST remain unobserved
/// because each rejection happens before the inner control implementation.
/// </summary>
public sealed class P0CommandStopRaceAdmissionTests
{
    [Theory]
    [InlineData("OperationGate")]
    [InlineData("StopGate")]
    public async Task CommandBusyAtEitherGate_RejectsImmediatelyAndReleasesOwnedLease(
        string busyGateName)
    {
        await using var runtime = new Iec61850MonitorRuntime();
        const string id = "offline-stop-admission";
        var slot = Slot(runtime, id);
        var busyGate = Gate(slot, busyGateName);
        var operationGate = Gate(slot, "OperationGate");
        await busyGate.WaitAsync();
        try
        {
            // WaitAsync bounds the regression even if a future change wrongly
            // restores an invisible FIFO wait on OperationGate or StopGate.
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.ExecuteControlAsync(id, null!, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains("NOT SENT", ex.Message, StringComparison.Ordinal);
            if (busyGateName == "StopGate")
            {
                // Command briefly acquires OperationGate, then must release it
                // even when StopGate has rejected the nonqueued admission.
                Assert.True(await operationGate.WaitAsync(0));
                operationGate.Release();
            }
        }
        finally
        {
            busyGate.Release();
        }
    }

    [Fact]
    public async Task PublishedStopTask_RejectsCommandEvenWhenStopGateIsFree()
    {
        await using var runtime = new Iec61850MonitorRuntime();
        const string id = "offline-stop-published";
        var slot = Slot(runtime, id);
        var stopProperty = slot.GetType().GetProperty(
            "ActiveStopTask", BindingFlags.Instance | BindingFlags.Public)!;
        Assert.NotNull(stopProperty);
        var pendingStop = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        stopProperty.SetValue(slot, pendingStop.Task);
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await runtime.ExecuteControlAsync(id, null!, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains("command NOT SENT", ex.Message, StringComparison.Ordinal);
            var operationGate = Gate(slot, "OperationGate");
            Assert.True(await operationGate.WaitAsync(0));
            operationGate.Release();
        }
        finally
        {
            stopProperty.SetValue(slot, null);
            pendingStop.TrySetResult(true);
        }
    }

    [Fact]
    public void DistinctIeds_OwnIndependentAdmissionAndStopLanes()
    {
        using var ignored = new CancellationTokenSource();
        // Slot isolation is the foundation for an unrelated IED remaining
        // responsive when another IED is discovering or stopping.
        var runtime = new Iec61850MonitorRuntime();
        try
        {
            var first = Slot(runtime, "ied-1");
            var second = Slot(runtime, "ied-2");
            Assert.NotSame(first, second);
            Assert.NotSame(Gate(first, "OperationGate"), Gate(second, "OperationGate"));
            Assert.NotSame(Gate(first, "StopGate"), Gate(second, "StopGate"));
        }
        finally
        {
            runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static object Slot(Iec61850MonitorRuntime runtime, string deviceId)
    {
        var method = typeof(Iec61850MonitorRuntime).GetMethod(
            "GetSlot", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(runtime, [deviceId])!;
    }

    private static SemaphoreSlim Gate(object slot, string name)
    {
        var property = slot.GetType().GetProperty(
            name, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        return Assert.IsType<SemaphoreSlim>(property.GetValue(slot));
    }
}
