using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ArIED61850Tester.Services;

internal sealed record ComtradeRecordInfo(
    int RevisionYear,
    int DataFormat,
    uint AnalogCount,
    uint StatusCount,
    ulong FrameCount,
    double NominalFrequency,
    double TimeMultiplier,
    string StationName,
    string RecorderId,
    string StartTime,
    string TriggerTime);

internal sealed record ComtradeAnalogChannelInfo(
    int Index,
    string Id,
    string Phase,
    string Circuit,
    string Units,
    double A,
    double B,
    double SkewUs,
    double MinValue,
    double MaxValue,
    double? Primary,
    double? Secondary,
    string PrimarySecondary);

internal sealed record ComtradeStatusChannelInfo(
    int Index,
    string Id,
    string Phase,
    string Circuit,
    int NormalState);

internal sealed record ComtradeAnalogSemantics(
    int Role,
    int PhaseRole,
    int RecordedRepresentation,
    bool HasValidTransformerRatio,
    double ScaleToSecondary,
    double ScaleToPrimary);

internal sealed record ComtradeCursorMeasurement(
    bool Valid,
    ulong ReferenceFrame,
    uint RawTimestamp,
    double TimeSeconds,
    double Instantaneous,
    double Rms,
    ulong WindowStartFrame,
    ulong WindowEndExclusive,
    uint WindowSampleCount);

internal sealed record ComtradeStatusState(
    int RawState,
    int NormalState,
    bool IsActive);

internal sealed record ComtradeStatusEdge(
    bool Valid,
    uint ChannelIndex,
    ulong FrameIndex,
    uint RawTimestamp,
    int BeforeState,
    int AfterState,
    int NormalState,
    bool BecameActive,
    double DistanceSeconds);

internal sealed record ComtradePhasor(
    bool Valid,
    double MagnitudeRms,
    double AngleDegrees,
    double Real,
    double Imag,
    ulong WindowStartFrame,
    ulong WindowEndExclusive);

internal sealed record ComtradeHarmonicBin(
    int Order,
    double MagnitudeRms,
    double PercentOfFundamental,
    double AngleDegrees);

internal sealed record ComtradeHarmonicSpectrum(
    bool Valid,
    double DcComponent,
    double FundamentalRms,
    double ThdPercent,
    int DominantOrder,
    double DominantRms,
    double DominantPercent,
    double EstimatedSampleRateHz,
    int MaximumResolvableOrder,
    ulong WindowStartFrame,
    ulong WindowEndExclusive,
    IReadOnlyList<ComtradeHarmonicBin> Bins);

internal static class ArdIrecNativeBridge
{
    internal const uint ExpectedAbiVersion = 1;
    internal const ulong CapCursorMeasurement = 1UL << 0;
    internal const ulong CapChannelSemantics = 1UL << 1;
    internal const ulong CapValueRepresentation = 1UL << 2;
    internal const ulong CapStatusState = 1UL << 3;
    internal const ulong CapDigitalEdgeSnap = 1UL << 4;
    internal const ulong CapPhasor = 1UL << 5;
    internal const ulong CapHarmonics = 1UL << 6;

    private const string BridgeFileName = "ardirec_bridge.dll";
    private static readonly object Sync = new();
    private static NativeApi? _api;
    private static string? _loadError;

    internal static bool TryOpen(string cfgPath, out ArdIrecNativeRecord? record, out string error)
    {
        record = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(cfgPath) || !File.Exists(cfgPath))
        {
            error = "The COMTRADE CFG file does not exist.";
            return false;
        }

        if (!TryGetApi(out var api, out error) || api is null)
            return false;

        var pathPtr = Marshal.StringToCoTaskMemUTF8(Path.GetFullPath(cfgPath));
        var errorPtr = Marshal.AllocHGlobal(2048);
        try
        {
            Span<byte> clear = new byte[2048];
            Marshal.Copy(clear.ToArray(), 0, errorPtr, clear.Length);

            var result = api.Open(pathPtr, out var handle, errorPtr, 2048);
            if (result != 0 || handle == IntPtr.Zero)
            {
                error = ReadUtf8(errorPtr, 2048);
                if (string.IsNullOrWhiteSpace(error))
                    error = $"ArdIrec native bridge could not open the record (error {result}).";
                return false;
            }

            try
            {
                record = new ArdIrecNativeRecord(api, handle, cfgPath);
                return true;
            }
            catch
            {
                api.Close(handle);
                throw;
            }
        }
        catch (Exception ex) when (ex is SEHException or AccessViolationException or InvalidOperationException)
        {
            error = $"ArdIrec native bridge failed: {ex.Message}";
            return false;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPtr);
            Marshal.FreeHGlobal(errorPtr);
        }
    }

    internal static bool IsAvailable(out string detail)
    {
        var available = TryGetApi(out var api, out var error);
        detail = available && api is not null
            ? $"ABI {api.AbiVersion} • {api.LibraryPath}"
            : error;
        return available;
    }

    private static bool TryGetApi(out NativeApi? api, out string error)
    {
        lock (Sync)
        {
            if (_api is not null)
            {
                api = _api;
                error = string.Empty;
                return true;
            }

            if (_loadError is not null)
            {
                api = null;
                error = _loadError;
                return false;
            }

            var failures = new List<string>();
            foreach (var candidate in EnumerateBridgeCandidates())
            {
                if (!File.Exists(candidate))
                    continue;

                try
                {
                    var loaded = new NativeApi(candidate);
                    if (loaded.AbiVersion != ExpectedAbiVersion)
                    {
                        failures.Add($"{candidate}: ABI {loaded.AbiVersion}, expected {ExpectedAbiVersion}");
                        loaded.Dispose();
                        continue;
                    }

                    _api = loaded;
                    api = loaded;
                    error = string.Empty;
                    return true;
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
                {
                    failures.Add($"{candidate}: {ex.Message}");
                }
            }

            _loadError = failures.Count > 0
                ? "ArdIrec native bridge is present but could not be loaded. " + string.Join(" | ", failures)
                : "ArdIrec native bridge is not installed. P0 Qt viewer remains available as fallback.";
            api = null;
            error = _loadError;
            return false;
        }
    }

    private static IEnumerable<string> EnumerateBridgeCandidates()
    {
        var explicitPath = Environment.GetEnvironmentVariable("ARSAS_ARDIREC_BRIDGE_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
            yield return Path.GetFullPath(explicitPath);

        var baseDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(baseDirectory, "Tools", "ArdIrec", BridgeFileName);
        yield return Path.Combine(baseDirectory, "ArdIrec", BridgeFileName);
        yield return Path.Combine(baseDirectory, BridgeFileName);
    }

    private static string ReadUtf8(IntPtr pointer, int capacity)
    {
        if (pointer == IntPtr.Zero || capacity <= 0)
            return string.Empty;

        var bytes = new byte[capacity];
        Marshal.Copy(pointer, bytes, 0, capacity);
        return DecodeUtf8(bytes);
    }

    internal static string DecodeUtf8(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return string.Empty;
        var length = Array.IndexOf(bytes, (byte)0);
        if (length < 0) length = bytes.Length;
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    internal sealed class NativeApi : IDisposable
    {
        private IntPtr _library;

        internal NativeApi(string libraryPath)
        {
            LibraryPath = Path.GetFullPath(libraryPath);
            _library = NativeLibrary.Load(LibraryPath);
            AbiVersion = Export<BridgeAbiVersionDelegate>("ardirec_bridge_abi_version")();
            Open = Export<RecordOpenDelegate>("ardirec_record_open_utf8");
            Close = Export<RecordCloseDelegate>("ardirec_record_close");
            GetInfo = Export<RecordGetInfoDelegate>("ardirec_record_get_info");
            GetAnalogChannel = Export<RecordGetAnalogChannelDelegate>("ardirec_record_get_analog_channel");
            GetStatusChannel = Export<RecordGetStatusChannelDelegate>("ardirec_record_get_status_channel");
            CopyAnalog = Export<RecordCopyAnalogDelegate>("ardirec_record_copy_analog");
            CopyStatus = Export<RecordCopyStatusDelegate>("ardirec_record_copy_status");
            CopyRawTimestamps = Export<RecordCopyRawTimestampsDelegate>("ardirec_record_copy_raw_timestamps");
            GetPhasor = Export<RecordGetPhasorDelegate>("ardirec_record_get_phasor");
            GetHarmonicSpectrum = Export<RecordGetHarmonicSpectrumDelegate>("ardirec_record_get_harmonic_spectrum");

            var capabilities = TryExport<BridgeCapabilitiesDelegate>("ardirec_bridge_capabilities");
            Capabilities = capabilities?.Invoke() ?? 0UL;
            GetAnalogSemantics = TryExport<RecordGetAnalogSemanticsDelegate>("ardirec_record_get_analog_semantics");
            GetRepresentationScale = TryExport<RecordGetRepresentationScaleDelegate>("ardirec_record_get_representation_scale");
            GetCursorMeasurement = TryExport<RecordGetCursorMeasurementDelegate>("ardirec_record_get_cursor_measurement");
            GetStatusState = TryExport<RecordGetStatusStateDelegate>("ardirec_record_get_status_state");
            FindNearestStatusEdge = TryExport<RecordFindNearestStatusEdgeDelegate>("ardirec_record_find_nearest_status_edge");
        }

        internal string LibraryPath { get; }
        internal uint AbiVersion { get; }
        internal ulong Capabilities { get; }
        internal RecordOpenDelegate Open { get; }
        internal RecordCloseDelegate Close { get; }
        internal RecordGetInfoDelegate GetInfo { get; }
        internal RecordGetAnalogChannelDelegate GetAnalogChannel { get; }
        internal RecordGetStatusChannelDelegate GetStatusChannel { get; }
        internal RecordCopyAnalogDelegate CopyAnalog { get; }
        internal RecordCopyStatusDelegate CopyStatus { get; }
        internal RecordCopyRawTimestampsDelegate CopyRawTimestamps { get; }
        internal RecordGetPhasorDelegate GetPhasor { get; }
        internal RecordGetHarmonicSpectrumDelegate GetHarmonicSpectrum { get; }
        internal RecordGetAnalogSemanticsDelegate? GetAnalogSemantics { get; }
        internal RecordGetRepresentationScaleDelegate? GetRepresentationScale { get; }
        internal RecordGetCursorMeasurementDelegate? GetCursorMeasurement { get; }
        internal RecordGetStatusStateDelegate? GetStatusState { get; }
        internal RecordFindNearestStatusEdgeDelegate? FindNearestStatusEdge { get; }

        private T Export<T>(string name) where T : Delegate
        {
            var address = NativeLibrary.GetExport(_library, name);
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        private T? TryExport<T>(string name) where T : Delegate
            => NativeLibrary.TryGetExport(_library, name, out var address)
                ? Marshal.GetDelegateForFunctionPointer<T>(address)
                : null;

        public void Dispose()
        {
            if (_library == IntPtr.Zero) return;
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate uint BridgeAbiVersionDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate ulong BridgeCapabilitiesDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordOpenDelegate(IntPtr cfgPath, out IntPtr handle, IntPtr error, nuint errorCapacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void RecordCloseDelegate(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordGetInfoDelegate(IntPtr handle, ref NativeRecordInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordGetAnalogChannelDelegate(IntPtr handle, uint index, ref NativeAnalogChannelInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordGetStatusChannelDelegate(IntPtr handle, uint index, ref NativeStatusChannelInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordGetAnalogSemanticsDelegate(IntPtr handle, uint channel, ref NativeAnalogSemanticsInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordGetRepresentationScaleDelegate(IntPtr handle, uint channel, int representation, out double scale);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordGetCursorMeasurementDelegate(IntPtr handle, uint channel, ulong referenceFrame, int representation, ref NativeCursorMeasurementInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordGetStatusStateDelegate(IntPtr handle, uint channel, ulong referenceFrame, ref NativeStatusStateInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordFindNearestStatusEdgeDelegate(IntPtr handle, ulong referenceFrame, double maxDistanceSeconds, ref NativeStatusEdgeInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordCopyAnalogDelegate(IntPtr handle, uint channel, ulong start, ulong count, [Out] double[] destination);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordCopyStatusDelegate(IntPtr handle, uint channel, ulong start, ulong count, [Out] byte[] destination);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordCopyRawTimestampsDelegate(IntPtr handle, ulong start, ulong count, [Out] uint[] destination);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordGetPhasorDelegate(IntPtr handle, uint channel, ulong referenceFrame, ref NativePhasorInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RecordGetHarmonicSpectrumDelegate(IntPtr handle, uint channel, ulong referenceFrame, int maximumOrder, ref NativeHarmonicSpectrumInfo info, IntPtr bins, uint binCapacity);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRecordInfo
    {
        internal uint AbiVersion;
        internal int RevisionYear;
        internal int DataFormat;
        internal uint AnalogCount;
        internal uint StatusCount;
        internal ulong FrameCount;
        internal double NominalFrequency;
        internal double TimeMultiplier;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] internal byte[] StationName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] internal byte[] RecorderId;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] internal byte[] StartTime;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] internal byte[] TriggerTime;

        internal static NativeRecordInfo Create() => new()
        {
            StationName = new byte[256],
            RecorderId = new byte[256],
            StartTime = new byte[128],
            TriggerTime = new byte[128]
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeAnalogChannelInfo
    {
        internal int Index;
        internal double A;
        internal double B;
        internal double SkewUs;
        internal double MinValue;
        internal double MaxValue;
        internal double Primary;
        internal double Secondary;
        internal byte HasPrimary;
        internal byte HasSecondary;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] internal byte[] Id;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] internal byte[] Phase;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] internal byte[] Circuit;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] internal byte[] Units;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] internal byte[] PrimarySecondary;

        internal static NativeAnalogChannelInfo Create() => new()
        {
            Id = new byte[128],
            Phase = new byte[64],
            Circuit = new byte[128],
            Units = new byte[64],
            PrimarySecondary = new byte[64]
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeStatusChannelInfo
    {
        internal int Index;
        internal int NormalState;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] internal byte[] Id;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] internal byte[] Phase;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] internal byte[] Circuit;

        internal static NativeStatusChannelInfo Create() => new()
        {
            Id = new byte[128],
            Phase = new byte[64],
            Circuit = new byte[128]
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeAnalogSemanticsInfo
    {
        internal int Role;
        internal int PhaseRole;
        internal int RecordedRepresentation;
        internal int HasValidTransformerRatio;
        internal double ScaleToSecondary;
        internal double ScaleToPrimary;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeCursorMeasurementInfo
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
    internal struct NativeStatusStateInfo
    {
        internal int RawState;
        internal int NormalState;
        internal int IsActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeStatusEdgeInfo
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePhasorInfo
    {
        internal int Valid;
        internal double MagnitudeRms;
        internal double AngleDegrees;
        internal double Real;
        internal double Imag;
        internal ulong WindowStartFrame;
        internal ulong WindowEndExclusive;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeHarmonicBin
    {
        internal int Order;
        internal double MagnitudeRms;
        internal double PercentOfFundamental;
        internal double AngleDegrees;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeHarmonicSpectrumInfo
    {
        internal int Valid;
        internal double DcComponent;
        internal double FundamentalRms;
        internal double ThdPercent;
        internal int DominantOrder;
        internal double DominantRms;
        internal double DominantPercent;
        internal double EstimatedSampleRateHz;
        internal int MaximumResolvableOrder;
        internal uint BinCount;
        internal ulong WindowStartFrame;
        internal ulong WindowEndExclusive;
    }
}

internal sealed class ArdIrecNativeRecord : IDisposable
{
    private readonly ArdIrecNativeBridge.NativeApi _api;
    private IntPtr _handle;

    internal ArdIrecNativeRecord(ArdIrecNativeBridge.NativeApi api, IntPtr handle, string cfgPath)
    {
        _api = api;
        _handle = handle;
        CfgPath = Path.GetFullPath(cfgPath);

        var nativeInfo = ArdIrecNativeBridge.NativeRecordInfo.Create();
        EnsureSuccess(_api.GetInfo(_handle, ref nativeInfo), "read record metadata");
        Info = new ComtradeRecordInfo(
            nativeInfo.RevisionYear,
            nativeInfo.DataFormat,
            nativeInfo.AnalogCount,
            nativeInfo.StatusCount,
            nativeInfo.FrameCount,
            nativeInfo.NominalFrequency,
            nativeInfo.TimeMultiplier,
            ArdIrecNativeBridge.DecodeUtf8(nativeInfo.StationName),
            ArdIrecNativeBridge.DecodeUtf8(nativeInfo.RecorderId),
            ArdIrecNativeBridge.DecodeUtf8(nativeInfo.StartTime),
            ArdIrecNativeBridge.DecodeUtf8(nativeInfo.TriggerTime));

        AnalogChannels = Enumerable.Range(0, checked((int)Info.AnalogCount))
            .Select(ReadAnalogChannelInfo)
            .ToArray();
        StatusChannels = Enumerable.Range(0, checked((int)Info.StatusCount))
            .Select(ReadStatusChannelInfo)
            .ToArray();
    }

    internal string CfgPath { get; }
    internal ComtradeRecordInfo Info { get; }
    internal IReadOnlyList<ComtradeAnalogChannelInfo> AnalogChannels { get; }
    internal IReadOnlyList<ComtradeStatusChannelInfo> StatusChannels { get; }
    internal ulong BridgeCapabilities => _api.Capabilities;

    internal bool Supports(ulong capability) => (_api.Capabilities & capability) == capability;

    internal double[] ReadAnalog(uint channelIndex, ulong startFrame, int frameCount)
    {
        EnsureOpen();
        if (frameCount < 0) throw new ArgumentOutOfRangeException(nameof(frameCount));
        var values = new double[frameCount];
        EnsureSuccess(_api.CopyAnalog(_handle, channelIndex, startFrame, checked((ulong)frameCount), values), "read analog samples");
        return values;
    }

    internal byte[] ReadStatus(uint channelIndex, ulong startFrame, int frameCount)
    {
        EnsureOpen();
        if (frameCount < 0) throw new ArgumentOutOfRangeException(nameof(frameCount));
        var values = new byte[frameCount];
        EnsureSuccess(_api.CopyStatus(_handle, channelIndex, startFrame, checked((ulong)frameCount), values), "read digital states");
        return values;
    }

    internal uint[] ReadRawTimestamps(ulong startFrame, int frameCount)
    {
        EnsureOpen();
        if (frameCount < 0) throw new ArgumentOutOfRangeException(nameof(frameCount));
        var values = new uint[frameCount];
        EnsureSuccess(_api.CopyRawTimestamps(_handle, startFrame, checked((ulong)frameCount), values), "read timestamps");
        return values;
    }

    internal bool TryReadAnalogSemantics(uint channelIndex, out ComtradeAnalogSemantics? semantics)
    {
        EnsureOpen();
        semantics = null;
        if (!Supports(ArdIrecNativeBridge.CapChannelSemantics) || _api.GetAnalogSemantics is null)
            return false;

        var native = new ArdIrecNativeBridge.NativeAnalogSemanticsInfo();
        if (_api.GetAnalogSemantics(_handle, channelIndex, ref native) != 0)
            return false;
        semantics = new ComtradeAnalogSemantics(
            native.Role,
            native.PhaseRole,
            native.RecordedRepresentation,
            native.HasValidTransformerRatio != 0,
            native.ScaleToSecondary,
            native.ScaleToPrimary);
        return true;
    }

    internal bool TryReadCursorMeasurement(uint channelIndex, ulong referenceFrame, int representation, out ComtradeCursorMeasurement? measurement)
    {
        EnsureOpen();
        measurement = null;
        if (!Supports(ArdIrecNativeBridge.CapCursorMeasurement) || _api.GetCursorMeasurement is null)
            return false;

        var native = new ArdIrecNativeBridge.NativeCursorMeasurementInfo();
        if (_api.GetCursorMeasurement(_handle, channelIndex, referenceFrame, representation, ref native) != 0)
            return false;
        measurement = new ComtradeCursorMeasurement(
            native.Valid != 0,
            native.ReferenceFrame,
            native.RawTimestamp,
            native.TimeSeconds,
            native.Instantaneous,
            native.Rms,
            native.WindowStartFrame,
            native.WindowEndExclusive,
            native.WindowSampleCount);
        return true;
    }

    internal bool TryReadStatusState(uint channelIndex, ulong referenceFrame, out ComtradeStatusState? state)
    {
        EnsureOpen();
        state = null;
        if (!Supports(ArdIrecNativeBridge.CapStatusState) || _api.GetStatusState is null)
            return false;

        var native = new ArdIrecNativeBridge.NativeStatusStateInfo();
        if (_api.GetStatusState(_handle, channelIndex, referenceFrame, ref native) != 0)
            return false;
        state = new ComtradeStatusState(native.RawState, native.NormalState, native.IsActive != 0);
        return true;
    }

    internal bool TryFindNearestStatusEdge(ulong referenceFrame, double maxDistanceSeconds, out ComtradeStatusEdge? edge)
    {
        EnsureOpen();
        edge = null;
        if (!Supports(ArdIrecNativeBridge.CapDigitalEdgeSnap) || _api.FindNearestStatusEdge is null)
            return false;

        var native = new ArdIrecNativeBridge.NativeStatusEdgeInfo();
        if (_api.FindNearestStatusEdge(_handle, referenceFrame, maxDistanceSeconds, ref native) != 0 || native.Valid == 0)
            return false;
        edge = new ComtradeStatusEdge(
            true,
            native.ChannelIndex,
            native.FrameIndex,
            native.RawTimestamp,
            native.BeforeState,
            native.AfterState,
            native.NormalState,
            native.BecameActive != 0,
            native.DistanceSeconds);
        return true;
    }

    internal ComtradePhasor ReadPhasor(uint channelIndex, ulong referenceFrame)
    {
        EnsureOpen();
        var native = new ArdIrecNativeBridge.NativePhasorInfo();
        EnsureSuccess(_api.GetPhasor(_handle, channelIndex, referenceFrame, ref native), "calculate phasor");
        return new ComtradePhasor(
            native.Valid != 0,
            native.MagnitudeRms,
            native.AngleDegrees,
            native.Real,
            native.Imag,
            native.WindowStartFrame,
            native.WindowEndExclusive);
    }

    internal ComtradeHarmonicSpectrum ReadHarmonicSpectrum(uint channelIndex, ulong referenceFrame, int maximumOrder = 15)
    {
        EnsureOpen();
        if (maximumOrder < 1) throw new ArgumentOutOfRangeException(nameof(maximumOrder));

        var native = new ArdIrecNativeBridge.NativeHarmonicSpectrumInfo();
        EnsureSuccess(_api.GetHarmonicSpectrum(_handle, channelIndex, referenceFrame, maximumOrder, ref native, IntPtr.Zero, 0),
            "query harmonic spectrum");
        if (native.Valid == 0 || native.BinCount == 0)
            return MapSpectrum(native, Array.Empty<ComtradeHarmonicBin>());

        var nativeSize = Marshal.SizeOf<ArdIrecNativeBridge.NativeHarmonicBin>();
        var bytes = checked(nativeSize * checked((int)native.BinCount));
        var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            EnsureSuccess(_api.GetHarmonicSpectrum(_handle, channelIndex, referenceFrame, maximumOrder, ref native, buffer, native.BinCount),
                "read harmonic spectrum");
            var bins = new List<ComtradeHarmonicBin>(checked((int)native.BinCount));
            for (var i = 0; i < native.BinCount; i++)
            {
                var pointer = IntPtr.Add(buffer, checked((int)i) * nativeSize);
                var bin = Marshal.PtrToStructure<ArdIrecNativeBridge.NativeHarmonicBin>(pointer);
                bins.Add(new ComtradeHarmonicBin(bin.Order, bin.MagnitudeRms, bin.PercentOfFundamental, bin.AngleDegrees));
            }
            return MapSpectrum(native, bins);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ComtradeHarmonicSpectrum MapSpectrum(
        ArdIrecNativeBridge.NativeHarmonicSpectrumInfo native,
        IReadOnlyList<ComtradeHarmonicBin> bins)
        => new(
            native.Valid != 0,
            native.DcComponent,
            native.FundamentalRms,
            native.ThdPercent,
            native.DominantOrder,
            native.DominantRms,
            native.DominantPercent,
            native.EstimatedSampleRateHz,
            native.MaximumResolvableOrder,
            native.WindowStartFrame,
            native.WindowEndExclusive,
            bins);

    private ComtradeAnalogChannelInfo ReadAnalogChannelInfo(int index)
    {
        var native = ArdIrecNativeBridge.NativeAnalogChannelInfo.Create();
        EnsureSuccess(_api.GetAnalogChannel(_handle, checked((uint)index), ref native), "read analog channel metadata");
        return new ComtradeAnalogChannelInfo(
            native.Index,
            ArdIrecNativeBridge.DecodeUtf8(native.Id),
            ArdIrecNativeBridge.DecodeUtf8(native.Phase),
            ArdIrecNativeBridge.DecodeUtf8(native.Circuit),
            ArdIrecNativeBridge.DecodeUtf8(native.Units),
            native.A,
            native.B,
            native.SkewUs,
            native.MinValue,
            native.MaxValue,
            native.HasPrimary != 0 ? native.Primary : null,
            native.HasSecondary != 0 ? native.Secondary : null,
            ArdIrecNativeBridge.DecodeUtf8(native.PrimarySecondary));
    }

    private ComtradeStatusChannelInfo ReadStatusChannelInfo(int index)
    {
        var native = ArdIrecNativeBridge.NativeStatusChannelInfo.Create();
        EnsureSuccess(_api.GetStatusChannel(_handle, checked((uint)index), ref native), "read status channel metadata");
        return new ComtradeStatusChannelInfo(
            native.Index,
            ArdIrecNativeBridge.DecodeUtf8(native.Id),
            ArdIrecNativeBridge.DecodeUtf8(native.Phase),
            ArdIrecNativeBridge.DecodeUtf8(native.Circuit),
            native.NormalState);
    }

    private void EnsureOpen()
    {
        if (_handle == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(ArdIrecNativeRecord));
    }

    private static void EnsureSuccess(int result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException($"ArdIrec native bridge could not {operation} (error {result}).");
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        _api.Close(_handle);
        _handle = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    ~ArdIrecNativeRecord() => Dispose();
}
