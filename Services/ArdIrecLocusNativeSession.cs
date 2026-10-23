using System.IO;
using System.Runtime.InteropServices;

namespace ArIED61850Tester.Services;

internal readonly record struct ComtradeDistancePoint(
    bool Valid,
    int Loop,
    ulong ReferenceFrame,
    uint RawTimestamp,
    double TimeSeconds,
    double R,
    double X,
    double Magnitude,
    double AngleDegrees,
    double MeasuringCurrent,
    double MinimumCurrent);

/// <summary>
/// Lazy native distance/locus session. It is instantiated only while Locus is visible and is
/// disposed when the operator leaves Locus, so the compatibility bridge record never doubles
/// COMTRADE memory outside that analysis mode. All electrical equations remain in ArdIrec C++.
/// </summary>
internal sealed class ArdIrecLocusNativeSession : IDisposable
{
    internal const int LoopL1E = 0;
    internal const int LoopL2E = 1;
    internal const int LoopL3E = 2;
    internal const int LoopL1L2 = 3;
    internal const int LoopL2L3 = 4;
    internal const int LoopL3L1 = 5;

    private const string BridgeFileName = "ardirec_bridge.dll";
    private readonly object _sync = new();
    private readonly CloseDelegate _close;
    private readonly GetDistanceCurrentFloorDelegate _getFloor;
    private readonly GetDistanceLoopsDelegate _getLoops;
    private readonly GetDistanceLocusDelegate _getLocus;
    private readonly Dictionary<int, double> _floorByRepresentation = new();
    private IntPtr _library;
    private IntPtr _handle;

    private ArdIrecLocusNativeSession(
        IntPtr library,
        IntPtr handle,
        CloseDelegate close,
        GetDistanceCurrentFloorDelegate getFloor,
        GetDistanceLoopsDelegate getLoops,
        GetDistanceLocusDelegate getLocus,
        ulong frameCount)
    {
        _library = library;
        _handle = handle;
        _close = close;
        _getFloor = getFloor;
        _getLoops = getLoops;
        _getLocus = getLocus;
        FrameCount = frameCount;
    }

    internal ulong FrameCount { get; }

    internal static bool TryOpen(string cfgPath, out ArdIrecLocusNativeSession? session, out string error)
    {
        session = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(cfgPath) || !File.Exists(cfgPath))
        {
            error = "The COMTRADE CFG file does not exist.";
            return false;
        }

        foreach (var candidate in EnumerateBridgeCandidates())
        {
            if (!File.Exists(candidate)) continue;
            IntPtr library = IntPtr.Zero;
            IntPtr handle = IntPtr.Zero;
            try
            {
                library = NativeLibrary.Load(candidate);
                if (!TryExport(library, "ardirec_record_open_utf8", out OpenDelegate? open) ||
                    !TryExport(library, "ardirec_record_close", out CloseDelegate? close) ||
                    !TryExport(library, "ardirec_record_get_info", out GetInfoDelegate? getInfo) ||
                    !TryExport(library, "ardirec_record_get_distance_current_floor", out GetDistanceCurrentFloorDelegate? getFloor) ||
                    !TryExport(library, "ardirec_record_get_distance_loops", out GetDistanceLoopsDelegate? getLoops) ||
                    !TryExport(library, "ardirec_record_get_distance_locus", out GetDistanceLocusDelegate? getLocus))
                {
                    NativeLibrary.Free(library);
                    continue;
                }

                var path = Marshal.StringToCoTaskMemUTF8(Path.GetFullPath(cfgPath));
                var errorBuffer = Marshal.AllocHGlobal(1024);
                try
                {
                    Marshal.Copy(new byte[1024], 0, errorBuffer, 1024);
                    var result = open!(path, out handle, errorBuffer, 1024);
                    if (result != 0 || handle == IntPtr.Zero)
                    {
                        error = Marshal.PtrToStringUTF8(errorBuffer) ?? $"Locus bridge open failed ({result}).";
                        NativeLibrary.Free(library);
                        return false;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(path);
                    Marshal.FreeHGlobal(errorBuffer);
                }

                var nativeInfo = NativeRecordInfo.Create();
                if (getInfo!(handle, ref nativeInfo) != 0)
                {
                    close!(handle);
                    NativeLibrary.Free(library);
                    error = "Locus bridge could not read record metadata.";
                    return false;
                }

                session = new ArdIrecLocusNativeSession(
                    library, handle, close!, getFloor!, getLoops!, getLocus!, nativeInfo.FrameCount);
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or SEHException)
            {
                if (handle != IntPtr.Zero && library != IntPtr.Zero)
                {
                    try
                    {
                        if (TryExport(library, "ardirec_record_close", out CloseDelegate? close)) close!(handle);
                    }
                    catch
                    {
                    }
                }
                if (library != IntPtr.Zero) NativeLibrary.Free(library);
                error = ex.Message;
            }
        }

        if (string.IsNullOrWhiteSpace(error))
            error = "The installed COMTRADE bridge does not expose the P1D.5 distance/locus extension.";
        return false;
    }

    internal bool TryReadLoops(
        ulong referenceFrame,
        int representation,
        double groundingFactorMagnitude,
        double groundingFactorAngleDegrees,
        out ComtradeDistancePoint[] points,
        out string error)
    {
        points = Array.Empty<ComtradeDistancePoint>();
        error = string.Empty;
        lock (_sync)
        {
            if (_handle == IntPtr.Zero)
            {
                error = "Locus session is closed.";
                return false;
            }
            if (!TryGetFloorLocked(representation, out var floor, out error)) return false;

            var native = new NativeDistancePoint[6];
            var result = _getLoops(
                _handle, referenceFrame, representation,
                groundingFactorMagnitude, groundingFactorAngleDegrees,
                floor, native, checked((uint)native.Length));
            if (result != 0)
            {
                error = $"Distance cursor calculation failed ({result}).";
                return false;
            }

            points = new ComtradeDistancePoint[native.Length];
            for (var index = 0; index < native.Length; index++) points[index] = Map(native[index]);
            return true;
        }
    }

    internal bool TryReadLocus(
        int loop,
        ulong startFrame,
        ulong frameCount,
        uint maximumPoints,
        int representation,
        double groundingFactorMagnitude,
        double groundingFactorAngleDegrees,
        out ComtradeDistancePoint[] points,
        out string error)
    {
        points = Array.Empty<ComtradeDistancePoint>();
        error = string.Empty;
        lock (_sync)
        {
            if (_handle == IntPtr.Zero)
            {
                error = "Locus session is closed.";
                return false;
            }
            if (!TryGetFloorLocked(representation, out var floor, out error)) return false;

            uint required = 0;
            var result = _getLocus(
                _handle, loop, startFrame, frameCount, maximumPoints, representation,
                groundingFactorMagnitude, groundingFactorAngleDegrees, floor,
                IntPtr.Zero, 0, ref required);
            if (result != 0)
            {
                error = $"Distance locus size query failed ({result}).";
                return false;
            }
            if (required == 0) return true;

            var nativeSize = Marshal.SizeOf<NativeDistancePoint>();
            var bytes = checked(nativeSize * checked((int)required));
            var buffer = Marshal.AllocHGlobal(bytes);
            try
            {
                var copied = required;
                result = _getLocus(
                    _handle, loop, startFrame, frameCount, maximumPoints, representation,
                    groundingFactorMagnitude, groundingFactorAngleDegrees, floor,
                    buffer, required, ref copied);
                if (result != 0 || copied > required)
                {
                    error = $"Distance locus calculation failed ({result}).";
                    return false;
                }

                points = new ComtradeDistancePoint[checked((int)copied)];
                for (var index = 0; index < points.Length; index++)
                {
                    var ptr = IntPtr.Add(buffer, checked(index * nativeSize));
                    points[index] = Map(Marshal.PtrToStructure<NativeDistancePoint>(ptr));
                }
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private bool TryGetFloorLocked(int representation, out double floor, out string error)
    {
        error = string.Empty;
        if (_floorByRepresentation.TryGetValue(representation, out floor)) return true;
        var result = _getFloor(_handle, representation, out floor);
        if (result != 0 || !double.IsFinite(floor) || floor <= 0)
        {
            error = $"Distance current-floor calculation failed ({result}).";
            floor = 0;
            return false;
        }
        _floorByRepresentation[representation] = floor;
        return true;
    }

    private static ComtradeDistancePoint Map(NativeDistancePoint native)
        => new(native.Valid != 0, native.Loop, native.ReferenceFrame, native.RawTimestamp,
            native.TimeSeconds, native.R, native.X, native.Magnitude, native.AngleDegrees,
            native.MeasuringCurrent, native.MinimumCurrent);

    private static IEnumerable<string> EnumerateBridgeCandidates()
    {
        var explicitPath = Environment.GetEnvironmentVariable("ARSAS_ARDIREC_BRIDGE_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath)) yield return Path.GetFullPath(explicitPath);
        var root = AppContext.BaseDirectory;
        yield return Path.Combine(root, "Tools", "ArdIrec", BridgeFileName);
        yield return Path.Combine(root, "ArdIrec", BridgeFileName);
        yield return Path.Combine(root, BridgeFileName);
    }

    private static bool TryExport<T>(IntPtr library, string name, out T? function) where T : Delegate
    {
        if (NativeLibrary.TryGetExport(library, name, out var address))
        {
            function = Marshal.GetDelegateForFunctionPointer<T>(address);
            return true;
        }
        function = null;
        return false;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_handle != IntPtr.Zero)
            {
                _close(_handle);
                _handle = IntPtr.Zero;
            }
            if (_library != IntPtr.Zero)
            {
                NativeLibrary.Free(_library);
                _library = IntPtr.Zero;
            }
        }
        GC.SuppressFinalize(this);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OpenDelegate(IntPtr cfgPath, out IntPtr handle, IntPtr error, nuint errorCapacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CloseDelegate(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetInfoDelegate(IntPtr handle, ref NativeRecordInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDistanceCurrentFloorDelegate(IntPtr handle, int representation, out double minimumCurrent);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDistanceLoopsDelegate(
        IntPtr handle, ulong referenceFrame, int representation,
        double groundingFactorMagnitude, double groundingFactorAngleDegrees, double minimumCurrent,
        [Out] NativeDistancePoint[] points, uint pointCapacity);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDistanceLocusDelegate(
        IntPtr handle, int loop, ulong startFrame, ulong frameCount, uint maximumPoints, int representation,
        double groundingFactorMagnitude, double groundingFactorAngleDegrees, double minimumCurrent,
        IntPtr points, uint pointCapacity, ref uint outPointCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeDistancePoint
    {
        internal int Valid;
        internal int Loop;
        internal ulong ReferenceFrame;
        internal uint RawTimestamp;
        internal double TimeSeconds;
        internal double R;
        internal double X;
        internal double Magnitude;
        internal double AngleDegrees;
        internal double MeasuringCurrent;
        internal double MinimumCurrent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRecordInfo
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
            StationName = new byte[256], RecorderId = new byte[256],
            StartTime = new byte[128], TriggerTime = new byte[128]
        };
    }
}
