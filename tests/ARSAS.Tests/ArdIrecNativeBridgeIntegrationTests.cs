using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ArdIrecNativeBridgeIntegrationTests
{
    [Fact]
    public void OpensConfiguredFixtureThroughManagedBridge_WhenConfigured()
    {
        var bridgePath = Environment.GetEnvironmentVariable("ARSAS_ARDIREC_BRIDGE_PATH");
        var cfgPath = Environment.GetEnvironmentVariable("ARSAS_NATIVE_COMTRADE_TEST_CFG");

        // The normal unit-test lane does not build the external C++ engine. Integration and
        // installer/release lanes set both variables and therefore exercise the real DLL.
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
            Assert.True(record.Info.FrameCount >= 2);
            Assert.False(string.IsNullOrWhiteSpace(record.Info.StationName));
            Assert.False(string.IsNullOrWhiteSpace(record.Info.RecorderId));

            var readCount = checked((int)Math.Min(record.Info.FrameCount, 4UL));
            var analog = record.ReadAnalog(0, 0, readCount);
            Assert.Equal(readCount, analog.Length);
            Assert.Equal(10.0, analog[1], 8);

            var digital = record.ReadStatus(0, 0, readCount);
            Assert.Equal(readCount, digital.Length);
            Assert.Contains((byte)1, digital);

            var timestamps = record.ReadRawTimestamps(0, readCount);
            Assert.Equal(readCount, timestamps.Length);
            Assert.True(timestamps[^1] >= timestamps[0]);
        }
    }

    [Fact]
    public void DecodesUtf8FixedBuffersWithoutUsingWindowsAnsiCodePage()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("Gardu 日本 üñîçødé\0unused");
        Assert.Equal("Gardu 日本 üñîçødé", ArdIrecNativeBridge.DecodeUtf8(bytes));
    }
}
