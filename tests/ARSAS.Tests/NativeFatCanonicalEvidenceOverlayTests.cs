using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class NativeFatCanonicalEvidenceOverlayTests
{
    [Fact]
    public void BuildRowKey_UsesIedNameAndIecTelegram_NotDeviceIdOrDisplayName()
    {
        var first = Point("runtime-a", "Trip A", "AA1E1F06R4LD0/GGIO1.Ind1.stVal");
        var recreated = Point("runtime-b", "Renamed display text", "AA1E1F06R4LD0/GGIO1.Ind1.stVal");
        var secondSignal = Point("runtime-a", "Trip A", "AA1E1F06R4LD0/GGIO1.Ind2.stVal");

        Assert.Equal("aa1e1f06r4|ld0/ggio1.ind1.stval", NativeFatCanonicalEvidenceOverlay.BuildRowKey(first));
        Assert.Equal(
            NativeFatCanonicalEvidenceOverlay.BuildRowKey(first),
            NativeFatCanonicalEvidenceOverlay.BuildRowKey(recreated));
        Assert.NotEqual(
            NativeFatCanonicalEvidenceOverlay.BuildRowKey(first),
            NativeFatCanonicalEvidenceOverlay.BuildRowKey(secondSignal));
        Assert.DoesNotContain("runtime-a", NativeFatCanonicalEvidenceOverlay.BuildRowKey(first), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Trip A", NativeFatCanonicalEvidenceOverlay.BuildRowKey(first), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingStableIdentity_FailsClosedWithoutSignalNameFallback()
    {
        var cache = new NativeFatIedSessionCacheState();
        var point = new Iec61850MonitorPoint
        {
            DeviceId = "runtime-a",
            DeviceName = "AA1E1F06R4",
            SignalName = "CSWI.Pos",
            IecReference = string.Empty,
            IecDataType = "BOOLEAN"
        };

        NativeFatCanonicalEvidenceOverlay.Write(cache, point, NativeFatEvidenceField.Value1, "SHOULD-NOT-BIND");

        Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.BuildRowKey(point));
        Assert.Equal(string.Empty, NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Value1));
        Assert.Empty(cache.EvidenceByRow);
    }

    [Fact]
    public void ReadUntouchedEvidence_DoesNotAllocateShadowRow()
    {
        var cache = new NativeFatIedSessionCacheState();
        var point = Point("dev-1", "Breaker", "AA1E1F06R4LD0/XCBR1.Pos.stVal");

        Assert.Equal(string.Empty,
            NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Value1));
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
        Assert.Equal("OPEN", NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Value1));
        Assert.Equal("CLOSE", NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Value2));
        Assert.Equal("PASS", NativeFatCanonicalEvidenceOverlay.ReadRaw(cache, point, NativeFatEvidenceField.Result));

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
