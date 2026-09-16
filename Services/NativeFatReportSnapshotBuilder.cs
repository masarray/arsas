using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Converts the persisted native FAT state into an immutable report snapshot. This is a
/// presentation/evidence boundary only; it never starts acquisition, changes command
/// state, or mutates the persisted FAT result.
/// </summary>
public static class NativeFatReportSnapshotBuilder
{
    public static NativeFatReportSnapshot Build(
        Iec61850MonitorDevice device,
        NativeFatDeviceState state,
        DateTimeOffset? generatedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(state);

        var rows = (state.Signals ?? new List<NativeFatSignalState>())
            .Select(BuildRow)
            .OrderBy(row => row.IsHistorical)
            .ThenBy(row => row.SignalName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.IecReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new NativeFatReportSnapshot(
            device.DeviceId,
            device.Name,
            device.IpAddress,
            device.Port,
            generatedUtc ?? DateTimeOffset.UtcNow,
            rows);
    }

    private static NativeFatReportRow BuildRow(NativeFatSignalState state)
    {
        var value1 = state.Value1;
        var value2 = state.Value2;
        var result = string.IsNullOrWhiteSpace(state.Result)
            ? NativeFatResult.Untested
            : state.Result.Trim().ToUpperInvariant();

        return new NativeFatReportRow(
            SignalName: state.SignalName,
            IecReference: state.IecReference,
            FunctionalConstraint: state.FunctionalConstraint,
            DataType: state.DataType,
            IsHistorical: state.IsHistorical,
            Result: result,
            HistoryCount: state.History?.Count ?? 0,
            Value1Text: value1?.Value ?? "—",
            Value1Quality: value1?.Quality ?? "—",
            Value1DeviceTimestamp: value1?.DeviceTimestamp ?? "—",
            Value1SourceMode: value1?.SourceMode ?? "—",
            Value1CapturedUtc: value1?.CapturedUtc,
            Value2Text: value2?.Value ?? "—",
            Value2Quality: value2?.Quality ?? "—",
            Value2DeviceTimestamp: value2?.DeviceTimestamp ?? "—",
            Value2SourceMode: value2?.SourceMode ?? "—",
            Value2CapturedUtc: value2?.CapturedUtc);
    }
}
