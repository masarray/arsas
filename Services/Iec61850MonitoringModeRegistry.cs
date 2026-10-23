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
        public bool StaticDataSetReportOnly { get; set; }
        public bool PreviousDynamicDataSetWrites { get; set; }
        public bool HasPreviousDynamicDataSetWrites { get; set; }
    }

    private static readonly ConditionalWeakTable<Iec61850MonitorDevice, DeviceModeState> States = new();

    public static bool IsStaticDataSetReportOnly(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return States.TryGetValue(device, out var state) && state.StaticDataSetReportOnly;
    }

    public static void UseStaticDataSetReportOnly(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var state = States.GetOrCreateValue(device);
        lock (state)
        {
            if (!state.StaticDataSetReportOnly)
            {
                state.PreviousDynamicDataSetWrites = device.AllowDynamicDataSetWrites;
                state.HasPreviousDynamicDataSetWrites = true;
            }

            state.StaticDataSetReportOnly = true;
            device.AllowDynamicDataSetWrites = false;
        }
    }

    public static void UseHybrid(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var state = States.GetOrCreateValue(device);
        lock (state)
        {
            state.StaticDataSetReportOnly = false;
            if (state.HasPreviousDynamicDataSetWrites)
            {
                device.AllowDynamicDataSetWrites = state.PreviousDynamicDataSetWrites;
                state.HasPreviousDynamicDataSetWrites = false;
            }
        }
    }
}
