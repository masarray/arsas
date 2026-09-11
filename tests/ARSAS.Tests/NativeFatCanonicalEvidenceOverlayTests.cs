using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatCanonicalEvidenceOverlayTests
{
    [Fact]
    public void BuildRowKey_ReusesEngineeringPointKeyInsteadOfDisplayName()
    {
        var first = Point("dev-1", "Trip", "AA1E1F06R4LD0/GGIO1.Ind1.stVal");
        var second = Point("dev-1", "Trip", "AA1E1F06R4LD0/GGIO1.Ind2.stVal");

        Assert.Equal(first.PointKey, NativeFatCanonicalEvidenceOverlay.BuildRowKey(first));
        Assert.Equal(second.PointKey, NativeFatCanonicalEvidenceOverlay.BuildRowKey(second));
        Assert.NotEqual(
            NativeFatCanonicalEvidenceOverlay.BuildRowKey(first),
            NativeFatCanonicalEvidenceOverlay.BuildRowKey(second));
    }

    [Fact]
    public void ReadUntouchedEvidence_DoesNotAllocateShadowRow()
    {
        var cache = new NativeFatIedSessionCacheState();
        var point = Point("dev-1", "Breaker", "AA1E1F06R4LD0/XCBR1.Pos.stVal");

        Assert.Equal(string.Empty,
            NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value1));
        Assert.Empty(cache.EvidenceByRow);
    }

    [Fact]
    public void WriteEvidence_UsesOneSparseSlotAndClearingLastValueRemovesIt()
    {
        var cache = new NativeFatIedSessionCacheState();
        var point = Point("dev-1", "Breaker", "AA1E1F06R4LD0/XCBR1.Pos.stVal");

        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Value1, "OPEN");
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Value2, "CLOSE");
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Result, "PASS");

        Assert.Single(cache.EvidenceByRow);
        Assert.Equal("OPEN", NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value1));
        Assert.Equal("CLOSE", NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Value2));
        Assert.Equal("PASS", NativeFatCanonicalEvidenceOverlay.Read(cache, point, NativeFatEvidenceField.Result));

        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Value1, "");
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Value2, "");
        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Result, "");

        Assert.Empty(cache.EvidenceByRow);
    }

    private static Iec61850MonitorPoint Point(string deviceId, string signalName, string reference)
        => new()
        {
            DeviceId = deviceId,
            DeviceName = "AA1E1F06R4",
            SignalName = signalName,
            IecReference = reference,
            IecDataType = "BOOLEAN"
        };
}
