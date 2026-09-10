using System.Runtime.InteropServices;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ArdIrecNativeBridgeIntegrationTests
{
    private const ulong CapCursorMeasurement = 1UL << 0;
    private const ulong CapChannelSemantics = 1UL << 1;
    private const ulong CapValueRepresentation = 1UL << 2;
    private const ulong CapStatusState = 1UL << 3;
    private const ulong CapDigitalEdgeSnap = 1UL << 4;

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

            // This small binary fixture may not contain a complete power-frequency cycle, but
            // invoking both P1C exports here proves the managed delegate/struct boundary against
            // the exact DLL that will be packaged. Numeric analysis accuracy is covered by the
            // ArdIrec bridge smoke using its 1 kHz / 50 Hz distance_p1 fixture.
            var referenceFrame = record.Info.FrameCount - 1;
            var phasor = record.ReadPhasor(0, referenceFrame);
            Assert.True(phasor.WindowStartFrame <= phasor.WindowEndExclusive);
            Assert.True(phasor.WindowEndExclusive <= record.Info.FrameCount);

            var harmonics = record.ReadHarmonicSpectrum(0, referenceFrame, 15);
            Assert.True(harmonics.WindowStartFrame <= harmonics.WindowEndExclusive);
            Assert.True(harmonics.WindowEndExclusive <= record.Info.FrameCount);
            if (harmonics.Valid)
            {
                Assert.NotEmpty(harmonics.Bins);
                Assert.Equal(1, harmonics.Bins[0].Order);
                Assert.InRange(harmonics.Bins[0].PercentOfFundamental, 0.0, 100.000001);
            }
        }
    }

    [Fact]
    public void P1D2AInvestigationExports_MatchManagedAbi_WhenConfigured()
    {
        var bridgePath = Environment.GetEnvironmentVariable("ARSAS_ARDIREC_BRIDGE_PATH");
        var cfgPath = Environment.GetEnvironmentVariable("ARSAS_NATIVE_COMTRADE_TEST_CFG");
        if (string.IsNullOrWhiteSpace(bridgePath) || string.IsNullOrWhiteSpace(cfgPath))
            return;

        var library = NativeLibrary.Load(bridgePath);
        try
        {
            var capabilities = Export<BridgeCapabilitiesDelegate>(library, "ardirec_bridge_capabilities")();
            var required = CapCursorMeasurement | CapChannelSemantics | CapValueRepresentation | CapStatusState | CapDigitalEdgeSnap;
            Assert.Equal(required, capabilities & required);

            var open = Export<RecordOpenDelegate>(library, "ardirec_record_open_utf8");
            var close = Export<RecordCloseDelegate>(library, "ardirec_record_close");
            var getSemantics = Export<RecordGetAnalogSemanticsDelegate>(library, "ardirec_record_get_analog_semantics");
            var getMeasurement = Export<RecordGetCursorMeasurementDelegate>(library, "ardirec_record_get_cursor_measurement");
            var getStatusState = Export<RecordGetStatusStateDelegate>(library, "ardirec_record_get_status_state");
            var findEdge = Export<RecordFindNearestStatusEdgeDelegate>(library, "ardirec_record_find_nearest_status_edge");

            var path = Marshal.StringToCoTaskMemUTF8(Path.GetFullPath(cfgPath));
            var error = Marshal.AllocHGlobal(512);
            try
            {
                for (var i = 0; i < 512; ++i) Marshal.WriteByte(error, i, 0);
                var result = open(path, out var handle, error, 512);
                Assert.True(result == 0 && handle != IntPtr.Zero,
                    $"P1D.2A bridge open failed ({result}): {Marshal.PtrToStringUTF8(error)}");

                try
                {
                    var semantics = new NativeAnalogSemanticsInfo();
                    Assert.Equal(0, getSemantics(handle, 0, ref semantics));
                    Assert.Equal(2, semantics.Role); // Current
                    Assert.Equal(1, semantics.PhaseRole); // L1
                    Assert.Equal(2, semantics.RecordedRepresentation); // Primary
                    Assert.Equal(1, semantics.HasValidTransformerRatio);
                    Assert.Equal(1.0, semantics.ScaleToPrimary, 12);
                    Assert.Equal(0.0005, semantics.ScaleToSecondary, 12);

                    var measurement = new NativeCursorMeasurementInfo();
                    Assert.Equal(0, getMeasurement(handle, 0, 1, 1, ref measurement)); // Secondary
                    Assert.Equal(1, measurement.Valid);
                    Assert.Equal((ulong)1, measurement.ReferenceFrame);
                    Assert.True(double.IsFinite(measurement.Instantaneous));
                    Assert.True(double.IsFinite(measurement.Rms));
                    Assert.True(measurement.WindowEndExclusive > measurement.WindowStartFrame);

                    var state = new NativeStatusStateInfo();
                    Assert.Equal(0, getStatusState(handle, 0, 1, ref state));
                    Assert.Equal(0, state.NormalState);
                    Assert.Equal(1, state.RawState);
                    Assert.Equal(1, state.IsActive);

                    var edge = new NativeStatusEdgeInfo();
                    Assert.Equal(0, findEdge(handle, 0, 0.002, ref edge));
                    Assert.Equal(1, edge.Valid);
                    Assert.Equal((ulong)1, edge.FrameIndex);
                    Assert.Equal((uint)0, edge.ChannelIndex);
                    Assert.Equal(0, edge.BeforeState);
                    Assert.Equal(1, edge.AfterState);
                    Assert.Equal(1, edge.BecameActive);
                }
                finally
                {
                    close(handle);
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(path);
                Marshal.FreeHGlobal(error);
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [Fact]
    public void P1D2ANativeInvestigationStructLayout_MatchesCAbiOnWindowsX64()
    {
        Assert.Equal(32, Marshal.SizeOf<NativeAnalogSemanticsInfo>());
        Assert.Equal(72, Marshal.SizeOf<NativeCursorMeasurementInfo>());
        Assert.Equal(12, Marshal.SizeOf<NativeStatusStateInfo>());
        Assert.Equal(48, Marshal.SizeOf<NativeStatusEdgeInfo>());
    }

    [Fact]
    public void P1CNativeAnalysisStructLayout_MatchesCAbiOnWindowsX64()
    {
        Assert.Equal(56, Marshal.SizeOf<ArdIrecNativeBridge.NativePhasorInfo>());
        Assert.Equal(32, Marshal.SizeOf<ArdIrecNativeBridge.NativeHarmonicBin>());
        Assert.Equal(88, Marshal.SizeOf<ArdIrecNativeBridge.NativeHarmonicSpectrumInfo>());
    }

    [Fact]
    public void DecodesUtf8FixedBuffersWithoutUsingWindowsAnsiCodePage()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("Gardu 日本 üñîçødé\0unused");
        Assert.Equal("Gardu 日本 üñîçødé", ArdIrecNativeBridge.DecodeUtf8(bytes));
    }

    private static T Export<T>(IntPtr library, string name) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate ulong BridgeCapabilitiesDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RecordOpenDelegate(IntPtr cfgPath, out IntPtr handle, IntPtr error, nuint errorCapacity);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RecordCloseDelegate(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RecordGetAnalogSemanticsDelegate(IntPtr handle, uint channel, ref NativeAnalogSemanticsInfo info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RecordGetCursorMeasurementDelegate(IntPtr handle, uint channel, ulong referenceFrame, int representation, ref NativeCursorMeasurementInfo info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RecordGetStatusStateDelegate(IntPtr handle, uint channel, ulong referenceFrame, ref NativeStatusStateInfo info);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RecordFindNearestStatusEdgeDelegate(IntPtr handle, ulong referenceFrame, double maxDistanceSeconds, ref NativeStatusEdgeInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeAnalogSemanticsInfo
    {
        internal int Role;
        internal int PhaseRole;
        internal int RecordedRepresentation;
        internal int HasValidTransformerRatio;
        internal double ScaleToSecondary;
        internal double ScaleToPrimary;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCursorMeasurementInfo
    {
        internal int Valid;
        internal ulong ReferenceFrame;
        internal uint RawTimestamp;
        internal double TimeSeconds;
        internal double Instantaneous;
        internal double Rms;
        internal ulong WindowStartFrame;
        internal ulong WindowEndExclusive;
        internal uint WindowSampleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStatusStateInfo
    {
        internal int RawState;
        internal int NormalState;
        internal int IsActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStatusEdgeInfo
    {
        internal int Valid;
        internal uint ChannelIndex;
        internal ulong FrameIndex;
        internal uint RawTimestamp;
        internal int BeforeState;
        internal int AfterState;
        internal int NormalState;
        internal int BecameActive;
        internal double DistanceSeconds;
    }
}
