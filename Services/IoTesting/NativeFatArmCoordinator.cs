using System.ComponentModel;
using System.Diagnostics;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record NativeFatArmResult(
    bool Succeeded,
    bool AlreadyArmed,
    int ArmedRows,
    int SeededValue1Rows,
    long ElapsedMilliseconds,
    string Message);

public sealed class NativeFatEvidenceChangedEventArgs : EventArgs
{
    public NativeFatEvidenceChangedEventArgs(
        string deviceId,
        Iec61850MonitorPoint point,
        NativeFatEvidenceField field)
    {
        DeviceId = deviceId;
        Point = point;
        Field = field;
    }

    public string DeviceId { get; }
    public Iec61850MonitorPoint Point { get; }
    public NativeFatEvidenceField Field { get; }
}

/// <summary>
/// P1D evidence ARM coordinator for the native Engineering FAT surface.
/// It never connects, discovers, imports SCL, changes reporting, changes polling cadence,
/// or creates a second point collection. It only subscribes to the canonical Engineering
/// Iec61850MonitorPoint instances that are already live and records sparse evidence.
/// </summary>
public sealed class NativeFatArmCoordinator : IDisposable
{
    private readonly Dictionary<string, ArmedDevice> _armedDevices =
        new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<NativeFatEvidenceChangedEventArgs>? EvidenceChanged;

    public NativeFatArmResult Arm(
        Iec61850MonitorDevice device,
        NativeFatIedSessionCacheState cache)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(cache);

        var stopwatch = Stopwatch.StartNew();

        if (!device.IsConnected || !device.IsMonitoring)
        {
            stopwatch.Stop();
            return new NativeFatArmResult(
                false,
                false,
                0,
                0,
                stopwatch.ElapsedMilliseconds,
                $"{device.Name} must already be connected and monitoring in Engineering before FAT can be armed.");
        }

        if (device.Points.Count == 0)
        {
            stopwatch.Stop();
            return new NativeFatArmResult(
                false,
                false,
                0,
                0,
                stopwatch.ElapsedMilliseconds,
                $"{device.Name} has no canonical Engineering live rows to arm.");
        }

        if (_armedDevices.TryGetValue(device.DeviceId, out var existing))
        {
            stopwatch.Stop();
            cache.IsArmed = true;
            cache.ArmedAt ??= existing.ArmedAt;
            cache.LastArmElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            return new NativeFatArmResult(
                true,
                true,
                existing.Subscriptions.Count,
                0,
                stopwatch.ElapsedMilliseconds,
                $"{device.Name} FAT is already armed on the shared Engineering acquisition stream.");
        }

        var armed = new ArmedDevice(device.DeviceId, cache, DateTimeOffset.Now);
        var seeded = 0;

        // P4A: only one-to-one IEDName + IEC Telegram identities may own evidence.
        // Missing or duplicate identities are skipped instead of being guessed by order,
        // SignalName, SelectedIndex or runtime DeviceId.
        var identityCounts = device.Points
            .Select(point => NativeFatCanonicalEvidenceOverlay.TryBuildRowKey(point, out var key)
                ? key
                : string.Empty)
            .Where(key => key.Length > 0)
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var point in device.Points)
        {
            if (!NativeFatCanonicalEvidenceOverlay.TryBuildRowKey(point, out var rowKey) ||
                !identityCounts.TryGetValue(rowKey, out var identityCount) ||
                identityCount != 1)
            {
                continue;
            }

            PropertyChangedEventHandler handler = (_, args) =>
            {
                if (args.PropertyName is nameof(Iec61850MonitorPoint.Value) or nameof(Iec61850MonitorPoint.DisplayValue))
                    ObserveCanonicalValue(armed, point);
            };

            point.PropertyChanged += handler;
            armed.Subscriptions.Add(new PointSubscription(point, handler));
            if (ObserveCanonicalValue(armed, point))
                seeded++;
        }

        if (armed.Subscriptions.Count == 0)
        {
            stopwatch.Stop();
            return new NativeFatArmResult(
                false,
                false,
                0,
                0,
                stopwatch.ElapsedMilliseconds,
                $"{device.Name} has no uniquely addressable IEDName + IEC Telegram FAT rows; evidence was not armed.");
        }

        _armedDevices[device.DeviceId] = armed;
        cache.IsArmed = true;
        cache.ArmedAt = armed.ArmedAt;
        stopwatch.Stop();
        cache.LastArmElapsedMilliseconds = stopwatch.ElapsedMilliseconds;

        return new NativeFatArmResult(
            true,
            false,
            armed.Subscriptions.Count,
            seeded,
            stopwatch.ElapsedMilliseconds,
            $"{device.Name} FAT armed on {armed.Subscriptions.Count} canonical Engineering row(s) in {stopwatch.ElapsedMilliseconds} ms; acquisition was not restarted.");
    }

    public bool IsArmed(string? deviceId)
        => !string.IsNullOrWhiteSpace(deviceId) && _armedDevices.ContainsKey(deviceId);

    public void Dispose()
    {
        foreach (var armed in _armedDevices.Values)
        {
            foreach (var subscription in armed.Subscriptions)
                subscription.Point.PropertyChanged -= subscription.Handler;
            armed.Cache.IsArmed = false;
        }

        _armedDevices.Clear();
    }

    private bool ObserveCanonicalValue(ArmedDevice armed, Iec61850MonitorPoint point)
    {
        var value = point.DisplayValue?.Trim() ?? string.Empty;
        if (!IsEvidenceCandidate(point, value))
            return false;

        if (!NativeFatCanonicalEvidenceOverlay.TryBuildRowKey(point, out var rowKey))
            return false;

        lock (armed.Gate)
        {
            if (!armed.Cache.EvidenceByRow.TryGetValue(rowKey, out var slot))
            {
                NativeFatCanonicalEvidenceOverlay.Write(
                    armed.Cache,
                    point,
                    NativeFatEvidenceField.Value1,
                    value);
                RaiseEvidenceChanged(armed.DeviceId, point, NativeFatEvidenceField.Value1);
                return true;
            }

            if (string.IsNullOrWhiteSpace(slot.Value1))
            {
                NativeFatCanonicalEvidenceOverlay.Write(
                    armed.Cache,
                    point,
                    NativeFatEvidenceField.Value1,
                    value);
                RaiseEvidenceChanged(armed.DeviceId, point, NativeFatEvidenceField.Value1);
                return true;
            }

            if (string.IsNullOrWhiteSpace(slot.Value2))
            {
                if (Iec61850MonitorPoint.AreSemanticallyEquivalent(slot.Value1, value))
                    return false;

                NativeFatCanonicalEvidenceOverlay.Write(
                    armed.Cache,
                    point,
                    NativeFatEvidenceField.Value2,
                    value);
                RaiseEvidenceChanged(armed.DeviceId, point, NativeFatEvidenceField.Value2);
                return true;
            }

            // Once a pair exists, only the newest Value 2 is the duplicate guard. A return
            // to the prior Value 1 is itself a real transition and must advance the pair.
            if (Iec61850MonitorPoint.AreSemanticallyEquivalent(slot.Value2, value))
                return false;

            // Keep the current pair aligned to the latest meaningful transition without
            // touching Result, which remains an operator/report assessment field.
            NativeFatCanonicalEvidenceOverlay.Write(
                armed.Cache,
                point,
                NativeFatEvidenceField.Value1,
                slot.Value2);
            RaiseEvidenceChanged(armed.DeviceId, point, NativeFatEvidenceField.Value1);

            NativeFatCanonicalEvidenceOverlay.Write(
                armed.Cache,
                point,
                NativeFatEvidenceField.Value2,
                value);
            RaiseEvidenceChanged(armed.DeviceId, point, NativeFatEvidenceField.Value2);
            return true;
        }
    }

    private static bool IsEvidenceCandidate(Iec61850MonitorPoint point, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "-" or "—")
            return false;

        if (value.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var quality = point.Quality ?? string.Empty;
        return !quality.Contains("bad", StringComparison.OrdinalIgnoreCase) &&
               !quality.Contains("invalid", StringComparison.OrdinalIgnoreCase) &&
               !quality.Contains("questionable", StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseEvidenceChanged(
        string deviceId,
        Iec61850MonitorPoint point,
        NativeFatEvidenceField field)
        => EvidenceChanged?.Invoke(
            this,
            new NativeFatEvidenceChangedEventArgs(deviceId, point, field));

    private sealed class ArmedDevice
    {
        public ArmedDevice(
            string deviceId,
            NativeFatIedSessionCacheState cache,
            DateTimeOffset armedAt)
        {
            DeviceId = deviceId;
            Cache = cache;
            ArmedAt = armedAt;
        }

        public string DeviceId { get; }
        public NativeFatIedSessionCacheState Cache { get; }
        public DateTimeOffset ArmedAt { get; }
        public object Gate { get; } = new();
        public List<PointSubscription> Subscriptions { get; } = new();
    }

    private sealed record PointSubscription(
        Iec61850MonitorPoint Point,
        PropertyChangedEventHandler Handler);
}
