namespace ArIED61850Tester.Models;

/// <summary>
/// One O(N) index per explicit read sweep. Each MMS leaf then projects in O(1)
/// to existing Explorer/Live Monitor models instead of scanning thousands of
/// signal and point objects on every callback. No SOE event is synthesized.
/// </summary>
public sealed class IedBrowserReadBindingIndex
{
    private readonly Dictionary<string, SignalDefinition[]> _signals;
    private readonly Dictionary<string, Iec61850MonitorPoint[]> _points;

    public IedBrowserReadBindingIndex(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _signals = device.Signals.GroupBy(s => Key(s.ObjectReference, s.FunctionalConstraint),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        _points = device.Points.GroupBy(p => Key(p.IecReference, p.FunctionalConstraint),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private static string Key(string reference, string fc)
        => IedBrowserLiveValues.CanonicalReference(reference) + "|" + fc.Trim().ToUpperInvariant();

    public void Apply(IedBrowserReadResult read)
    {
        if (!string.IsNullOrWhiteSpace(read.Error) ||
            string.IsNullOrWhiteSpace(read.Value)) return;
        var key = Key(read.Target.Reference, read.Target.FunctionalConstraint);
        if (_signals.TryGetValue(key, out var signals))
            foreach (var signal in signals)
            {
                signal.Value = read.Value;
                if (!string.IsNullOrWhiteSpace(read.Quality))
                    signal.Quality = read.Quality;
                if (!string.IsNullOrWhiteSpace(read.DeviceTimestamp))
                    signal.DeviceTimestamp = read.DeviceTimestamp;
            }
        if (_points.TryGetValue(key, out var points))
            foreach (var point in points)
            {
                point.Value = read.Value;
                if (!string.IsNullOrWhiteSpace(read.Quality))
                    point.Quality = read.Quality;
                if (!string.IsNullOrWhiteSpace(read.DeviceTimestamp))
                    point.DeviceTimestamp = read.DeviceTimestamp;
                point.IsOffline = false;
                point.SourceMode = "MMS Read";
                point.Status = "Read-only snapshot";
            }
    }
}
