using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeDiagnosticQueueTests
{
    [Fact]
    public void DiagnosticQueue_HasExplicitSmallBound()
    {
        Assert.InRange(ComtradeDiagnosticQueue.Capacity, 32, 512);
    }

    [Fact]
    public void DiagnosticQueue_TryEnqueueRemainsNonBlockingWhenProducerBurstsPastCapacity()
    {
        for (var index = 0; index < ComtradeDiagnosticQueue.Capacity * 4; index++)
        {
            Assert.True(ComtradeDiagnosticQueue.TryEnqueue(
                "test",
                "BURST",
                $"diagnostic-{index}"));
        }
    }
}
