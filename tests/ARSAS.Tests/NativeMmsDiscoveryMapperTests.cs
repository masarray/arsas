using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class NativeMmsDiscoveryMapperTests
{
    [Fact]
    public void F13OperationalMeasurementLeaves_AreNeverDropped()
    {
        var snapshot = new NativeMmsDiscoverySnapshot
        {
            DomainVariables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["AA1C1F13R4VI3p1_OperationalValues"] = new[]
                {
                    "RPRE_MMXU1$MX$A$phsA$cVal$mag$f",
                    "RPRE_MMXU1$MX$PhV$phsA$cVal$mag$f",
                    "PPRE_MMXU1$MX$W$phsA$cVal$mag$f"
                }
            }
        };

        var signals = NativeMmsDiscoveryMapper.BuildSignals(snapshot);

        var references = string.Join("\n", signals.Select(signal => signal.ObjectReference));
        Assert.True(signals.Any(signal => signal.ObjectReference.EndsWith("RPRE_MMXU1.A.phsA.cVal.mag.f", StringComparison.OrdinalIgnoreCase)), references);
        Assert.True(signals.Any(signal => signal.ObjectReference.EndsWith("RPRE_MMXU1.PhV.phsA.cVal.mag.f", StringComparison.OrdinalIgnoreCase)), references);
        Assert.True(signals.Any(signal => signal.ObjectReference.EndsWith("PPRE_MMXU1.W.phsA.cVal.mag.f", StringComparison.OrdinalIgnoreCase)), references);
        Assert.All(signals.Where(signal => signal.ObjectReference.EndsWith(".mag.f", StringComparison.OrdinalIgnoreCase)), signal =>
        {
            Assert.Equal("MX", signal.FunctionalConstraint);
            Assert.Equal("Measurement", signal.Category);
        });
    }
}
