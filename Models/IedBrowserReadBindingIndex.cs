using ArIED61850Tester.Services;

namespace ArIED61850Tester.Models;

/// <summary>
/// O(model + signals + points) immutable reference/FC bindings per explicit Read
/// sweep or monitoring lifetime. Presentation models are projections, never
/// observational authorities. Only canonical evidence may update them.
/// </summary>
public sealed class IedBrowserReadBindingIndex
{
    private readonly Iec61850MonitorDevice _device;
    private readonly Dictionary<string, SignalDefinition[]> _signals;
    private readonly Dictionary<string, Iec61850MonitorPoint[]> _points;

    public IedBrowserReadBindingIndex(Iec61850MonitorDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        var model = device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel;
        var schema = model is null ? null : new IedBrowserSchemaIndex(model);
        // Optional, strictly model-proven DO -> process leaf lookup. No
        // first-child heuristics and never borrow another FC's value.
        var signalsByKey = new Dictionary<string, List<SignalDefinition>>(StringComparer.Ordinal);
        foreach (var signal in device.Signals)
        {
            if (string.IsNullOrWhiteSpace(signal.ObjectReference) ||
                string.IsNullOrWhiteSpace(signal.FunctionalConstraint))
                continue;
            var directKey = Key(signal.ObjectReference, signal.FunctionalConstraint);
            Add(signalsByKey, directKey, signal);
            if (schema is not null &&
                schema.TryGetPrimaryLeaf(signal.ObjectReference,
                    signal.FunctionalConstraint, out var primaryLeaf))
            {
                var leafKey = Key(primaryLeaf, signal.FunctionalConstraint);
                if (leafKey != directKey) Add(signalsByKey, leafKey, signal);
            }
        }
        _signals = signalsByKey.ToDictionary(entry => entry.Key,
            entry => entry.Value.ToArray(), StringComparer.Ordinal);
        _points = device.Points
            .Where(point => !string.IsNullOrWhiteSpace(point.IecReference) &&
                            !string.IsNullOrWhiteSpace(point.FunctionalConstraint))
            .GroupBy(point => Key(point.IecReference, point.FunctionalConstraint),
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(),
                StringComparer.Ordinal);
    }

    private static void Add(Dictionary<string, List<SignalDefinition>> index,
        string key, SignalDefinition signal)
    {
        if (!index.TryGetValue(key, out var targets))
            index[key] = targets = new List<SignalDefinition>(1);
        if (!targets.Contains(signal)) targets.Add(signal);
    }

    private static string Key(string reference, string fc)
        => IedBrowserLiveValues.CanonicalReference(reference) + "|" +
           fc.Trim().ToUpperInvariant();

    /// <summary>
    /// Projects an explicit Read only after its value has been admitted into
    /// the current association's canonical store. An obsolete or competing
    /// Report cannot be materialized from the callback payload.
    /// </summary>
    public int Apply(IedBrowserReadResult read, IedBrowserLiveValues live,
        long association)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(live);
        if (_device.IsMonitoring || !_device.IsConnected ||
            !string.IsNullOrWhiteSpace(read.Error) ||
            string.IsNullOrWhiteSpace(read.Value) ||
            live.Association != association ||
            !live.TryGet(read.Target.Reference, read.Target.FunctionalConstraint,
                out var observed, preferReport: false) ||
            observed.IsStale || observed.Association != association ||
            !string.Equals(observed.Source, "MMS Read", StringComparison.Ordinal))
            return 0;

        var key = Key(read.Target.Reference, read.Target.FunctionalConstraint);
        var updated = 0;
        if (_signals.TryGetValue(key, out var signals))
            foreach (var signal in signals)
            {
                signal.Value = observed.Value;
                if (!string.IsNullOrWhiteSpace(observed.Quality))
                    signal.Quality = observed.Quality;
                if (!string.IsNullOrWhiteSpace(observed.DeviceTimestamp))
                    signal.DeviceTimestamp = observed.DeviceTimestamp;
                updated++;
            }
        if (_points.TryGetValue(key, out var points))
            foreach (var point in points)
            {
                point.Value = observed.Value;
                if (!string.IsNullOrWhiteSpace(observed.Quality))
                    point.Quality = observed.Quality;
                if (!string.IsNullOrWhiteSpace(observed.DeviceTimestamp))
                    point.DeviceTimestamp = observed.DeviceTimestamp;
                point.IsOffline = false;
                point.SourceMode = "MMS Read";
                point.Status = "Read-only snapshot";
                updated++;
            }
        return updated;
    }

    /// <summary>
    /// Projects a verified Report to the existing SignalDefinition UI rows.
    /// Explorer/Live Monitor point values are handled by the same canonical
    /// store in CanonicalPointSnapshotAuthority, not duplicated here.
    /// </summary>
    public int ProjectReportedSignals(IedBrowserLiveValues live, long association,
        string reference, string fc)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (!_device.IsMonitoring || !_device.IsConnected ||
            association != live.Association || live.IsOffline ||
            string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(fc) ||
            !_signals.TryGetValue(Key(reference, fc), out var signals) ||
            !live.TryGet(reference, fc, out var observed, preferReport: true) ||
            observed.IsStale || observed.Association != association ||
            !observed.Source.StartsWith("Report", StringComparison.Ordinal))
            return 0;
        foreach (var signal in signals)
        {
            signal.Value = Iec61850ValueFormatter.FormatReportProcessValue(
                observed.Value, signal.DataType, signal.Unit,
                signal.Category, signal.ObjectReference);
            if (!string.IsNullOrWhiteSpace(observed.Quality))
                signal.Quality = observed.Quality;
            if (!string.IsNullOrWhiteSpace(observed.DeviceTimestamp))
                signal.DeviceTimestamp = observed.DeviceTimestamp;
        }
        return signals.Length;
    }
}
