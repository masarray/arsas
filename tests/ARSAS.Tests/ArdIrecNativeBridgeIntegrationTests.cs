using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ArdIrecNativeBridgeIntegrationTests
{
    [Fact]
    public void OpensPinnedBinaryFixtureThroughManagedBridge_WhenConfigured()
    {
        var bridgePath = Environment.GetEnvironmentVariable("ARSAS_ARDIREC_BRIDGE_PATH");
        var cfgPath = Environment.GetEnvironmentVariable("ARSAS_NATIVE_COMTRADE_TEST_CFG");

        // The normal unit-test lane does not build the external C++ engine. The dedicated
        // cross-repo/installer lane sets both variables and therefore exercises the real DLL.
        if (string.IsNullOrWhiteSpace(bridgePath) || string.IsNullOrWhiteSpace(cfgPath))
            return;

        Assert.True(File.Exists(bridgePath), $"Native bridge fixture was not found: {bridgePath}");
        Assert.True(File.Exists(cfgPath), $"COMTRADE CFG fixture was not found: {cfgPath}");

        Assert.True(ArdIrecNativeBridge.IsAvailable(out var availability), availability);
        Assert.True(ArdIrecNativeBridge.TryOpen(cfgPath, out var record, out var error), error);
        Assert.NotNull(record);

        using (record!)
        {
            Assert.Equal((uint)3, record.Info.AnalogCount);
            Assert.Equal((uint)2, record.Info.StatusCount);
            Assert.Equal((ulong)2, record.Info.FrameCount);
            Assert.Equal("ARDIREC TEST", record.Info.StationName);
            Assert.Equal("REFERENCE RECORDER", record.Info.RecorderId);

            var analog = record.ReadAnalog(0, 0, 2);
            Assert.Equal(2, analog.Length);
            Assert.Equal(10.0, analog[1], 8);

            var digital = record.ReadStatus(0, 0, 2);
            Assert.Equal(2, digital.Length);
            Assert.Equal((byte)1, digital[1]);

            var timestamps = record.ReadRawTimestamps(0, 2);
            Assert.Equal(2, timestamps.Length);
        }
    }

    [Fact]
    public void DecodesUtf8FixedBuffersWithoutUsingWindowsAnsiCodePage()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("Gardu 日本 üñîçødé\0unused");
        Assert.Equal("Gardu 日本 üñîçødé", ArdIrecNativeBridge.DecodeUtf8(bytes));
    }
}
