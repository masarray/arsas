using System.Runtime.CompilerServices;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Per-device operator acquisition intent. The state follows the device object lifetime
/// and is deliberately not inferred from signal names, RCB availability, or polling rate.
/// </summary>
public static class Iec61850MonitoringModeRegistry
{
    private sealed class DeviceModeState
    {
        public bool StaticDataSetMode { get; set; }
        public bool StrictReportOnly { get; set; }
        public bool PreviousDynamicDataSetWrites { get; set; }
        public bool HasPreviousDynamicDataSetWrites { get; set; }
    }

    private static readonly ConditionalWeakTable<Iec61850MonitorDevice, DeviceModeState> States = new();

    public static bool IsStaticDataSetMode(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return States.TryGetValue(device, out var state) && state.StaticDataSetMode;
    }

    public static bool IsStaticDataSetReportOnly(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return States.TryGetValue(device, out var state) &&
               state.StaticDataSetMode &&
               state.StrictReportOnly;
    }

    /// <summary>
    /// Selects the Static DataSet workflow while retaining bounded MMS reads for the
    /// initial live image and for DataSet members that cannot be safely supplied by a
    /// configured report. Dynamic DataSet writes remain disabled, so the IED's existing
    /// engineering configuration is never modified merely to obtain readable values.
    /// </summary>
    public static void UseStaticDataSetWithMmsFallback(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var state = States.GetOrCreateValue(device);
        lock (state)
        {
            CapturePreviousDynamicWriteSetting(device, state);
            state.StaticDataSetMode = true;
            state.StrictReportOnly = false;
            device.AllowDynamicDataSetWrites = false;
        }
    }

    /// <summary>
    /// Strict report-only variant retained for explicit engineering/diagnostic use.
    /// Process values that cannot be delivered through configured static reporting are
    /// intentionally left unavailable in this mode.
    /// </summary>
    public static void UseStaticDataSetReportOnly(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var state = States.GetOrCreateValue(device);
        lock (state)
        {
            CapturePreviousDynamicWriteSetting(device, state);
            state.StaticDataSetMode = true;
            state.StrictReportOnly = true;
            device.AllowDynamicDataSetWrites = false;
        }
    }

    public static void UseHybrid(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var state = States.GetOrCreateValue(device);
        lock (state)
        {
            state.StaticDataSetMode = false;
            state.StrictReportOnly = false;
            if (state.HasPreviousDynamicDataSetWrites)
            {
                device.AllowDynamicDataSetWrites = state.PreviousDynamicDataSetWrites;
                state.HasPreviousDynamicDataSetWrites = false;
            }
        }
    }

    private static void CapturePreviousDynamicWriteSetting(
        Iec61850MonitorDevice device,
        DeviceModeState state)
    {
        if (state.StaticDataSetMode || state.HasPreviousDynamicDataSetWrites)
            return;

        state.PreviousDynamicDataSetWrites = device.AllowDynamicDataSetWrites;
        state.HasPreviousDynamicDataSetWrites = true;
    }
}
