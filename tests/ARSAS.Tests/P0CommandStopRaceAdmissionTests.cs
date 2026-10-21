using System.Reflection;
using UiRuntime = ArIED61850Tester.Iec61850MonitorRuntime;

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
        await using var runtime = new UiRuntime();
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
        await using var runtime = new UiRuntime();
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
    public async Task DistinctIeds_OwnIndependentAdmissionAndStopLanes()
    {
        // Slot isolation is the foundation for an unrelated IED remaining
        // responsive when another IED is discovering or stopping.
        await using var runtime = new UiRuntime();
        var first = Slot(runtime, "ied-1");
        var second = Slot(runtime, "ied-2");
        Assert.NotSame(first, second);
        Assert.NotSame(Gate(first, "OperationGate"), Gate(second, "OperationGate"));
        Assert.NotSame(Gate(first, "StopGate"), Gate(second, "StopGate"));
    }

    [Fact]
    public void QuickCommandDock_ClassifiesLocalBusyBeforeGenericFailure()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        string? source = null;
        while (current is not null)
        {
            var filename = Path.Combine(current.FullName, "MainWindow.xaml.cs");
            if (File.Exists(filename))
            {
                source = File.ReadAllText(filename);
                break;
            }
            current = current.Parent;
        }
        Assert.NotNull(source);

        var specific = source.IndexOf(
            "ex.Message.Contains(\"command NOT SENT\"", StringComparison.Ordinal);
        var generic = source.IndexOf(
            "signal.ControlLastResult = $\"Command failed:", StringComparison.Ordinal);
        Assert.True(specific >= 0 && generic > specific,
            "A facade-local NOT SENT must be classified before the generic failure popup.");
        Assert.Contains("CONTROL_UI_NOT_SENT:", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850ControlShout.FromLocalRejection(", source, StringComparison.Ordinal);
    }

    private static object Slot(UiRuntime runtime, string deviceId)
    {
        var method = typeof(UiRuntime).GetMethod(
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
