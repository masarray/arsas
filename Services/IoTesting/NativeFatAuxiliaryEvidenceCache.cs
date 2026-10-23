using System.Collections.ObjectModel;
using AR.Iec61850.FaultRecords;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record NativeFatComtradeRecordEvidence(
    string RecordName,
    DateTimeOffset? RecordDateUtc,
    long KnownSizeBytes,
    bool HasUnknownSize,
    string Result);

public sealed record NativeFatTimeSyncReportPoint(
    string Role,
    string SignalName,
    string IecReference,
    string Value,
    string Quality,
    string DeviceTimestamp,
    double? DeltaSeconds,
    string Result);

public sealed class NativeFatTimeSyncReportEvidence
{
    private readonly ReadOnlyCollection<NativeFatTimeSyncReportPoint> _supportingPoints;

    internal NativeFatTimeSyncReportEvidence(
        DateTimeOffset verifiedAtUtc,
        string verdict,
        string summary,
        bool ltmsPresent,
        int freshPrimaryTimestampCount,
        IEnumerable<NativeFatTimeSyncReportPoint> supportingPoints)
    {
        VerifiedAtUtc = verifiedAtUtc.ToUniversalTime();
        Verdict = Copy(verdict);
        Summary = Copy(summary);
        LtmsPresent = ltmsPresent;
        FreshPrimaryTimestampCount = freshPrimaryTimestampCount;
        _supportingPoints = Array.AsReadOnly(supportingPoints.ToArray());
    }

    public DateTimeOffset VerifiedAtUtc { get; }
    public string Verdict { get; }
    public string Summary { get; }
    public bool LtmsPresent { get; }
    public int FreshPrimaryTimestampCount { get; }
    public IReadOnlyList<NativeFatTimeSyncReportPoint> SupportingPoints => _supportingPoints;
    public bool IsSynchronized => Verdict.Equals("OK", StringComparison.OrdinalIgnoreCase);

    internal NativeFatTimeSyncReportEvidence Copy()
        => new(
            VerifiedAtUtc,
            Verdict,
            Summary,
            LtmsPresent,
            FreshPrimaryTimestampCount,
            _supportingPoints);

    private static string Copy(string? value)
        => value?.Trim() ?? string.Empty;
}

public sealed class NativeFatAuxiliaryEvidenceSnapshot
{
    private readonly ReadOnlyCollection<NativeFatComtradeRecordEvidence> _comtradeRecords;

    internal NativeFatAuxiliaryEvidenceSnapshot(
        DateTimeOffset? comtradeVerifiedAtUtc,
        IEnumerable<NativeFatComtradeRecordEvidence> comtradeRecords,
        NativeFatTimeSyncReportEvidence? timeSync)
    {
        ComtradeVerifiedAtUtc = comtradeVerifiedAtUtc?.ToUniversalTime();
        _comtradeRecords = Array.AsReadOnly(comtradeRecords.ToArray());
        TimeSync = timeSync?.Copy();
    }

    public static NativeFatAuxiliaryEvidenceSnapshot Empty { get; } =
        new(null, Array.Empty<NativeFatComtradeRecordEvidence>(), null);

    public DateTimeOffset? ComtradeVerifiedAtUtc { get; }
    public IReadOnlyList<NativeFatComtradeRecordEvidence> ComtradeRecords => _comtradeRecords;
    public NativeFatTimeSyncReportEvidence? TimeSync { get; }

    internal NativeFatAuxiliaryEvidenceSnapshot Copy()
        => new(ComtradeVerifiedAtUtc, _comtradeRecords, TimeSync);
}

/// <summary>
/// Thread-safe, in-memory, per-IED cache for evidence already obtained by the native FAT
/// diagnostics. Report capture only copies this state; it never reconnects, discovers files,
/// or starts acquisition. A completed empty/failed evaluation clears that evidence type so
/// report inclusion always fails closed.
/// </summary>
internal sealed class NativeFatAuxiliaryEvidenceCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, EvidenceState> _stateByIed = new(StringComparer.OrdinalIgnoreCase);

    public void RecordComtradeDiscovery(
        Iec61850MonitorDevice device,
        IEnumerable<Iec61850FaultRecordSet>? records,
        DateTimeOffset verifiedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(device);
        var projected = ProjectComtrade(records, CancellationToken.None);
        Update(device, state => state with
        {
            ComtradeVerifiedAtUtc = verifiedAtUtc.ToUniversalTime(),
            ComtradeRecords = projected
        });
    }

    public async Task RecordComtradeDiscoveryAsync(
        Iec61850MonitorDevice device,
        IEnumerable<Iec61850FaultRecordSet>? records,
        DateTimeOffset verifiedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        var key = ResolveStableIedKey(device);
        var projected = await Task.Run(
            () => ProjectComtrade(records, cancellationToken),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Update(key, state => state with
        {
            ComtradeVerifiedAtUtc = verifiedAtUtc.ToUniversalTime(),
            ComtradeRecords = projected
        });
    }

    public void ClearComtrade(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        Update(device, state => state with
        {
            ComtradeVerifiedAtUtc = null,
            ComtradeRecords = Array.Empty<NativeFatComtradeRecordEvidence>()
        });
    }

    public void RecordTimeSyncEvaluation(
        Iec61850MonitorDevice device,
        NativeFatTimeSyncDiagnosticResult diagnostic,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(diagnostic);

        var evidence = diagnostic.IsSynchronized
            ? ProjectTimeSync(diagnostic, evaluatedAtUtc)
            : null;
        Update(device, state => state with { TimeSync = evidence });
    }

    public void ClearTimeSync(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        Update(device, state => state with { TimeSync = null });
    }

    public NativeFatAuxiliaryEvidenceSnapshot Capture(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var key = ResolveStableIedKey(device);
        lock (_gate)
        {
            if (!_stateByIed.TryGetValue(key, out var state))
                return NativeFatAuxiliaryEvidenceSnapshot.Empty;

            return new NativeFatAuxiliaryEvidenceSnapshot(
                state.ComtradeVerifiedAtUtc,
                state.ComtradeRecords,
                state.TimeSync);
        }
    }

    private void Update(Iec61850MonitorDevice device, Func<EvidenceState, EvidenceState> update)
        => Update(ResolveStableIedKey(device), update);

    private void Update(string key, Func<EvidenceState, EvidenceState> update)
    {
        lock (_gate)
        {
            _stateByIed.TryGetValue(key, out var current);
            _stateByIed[key] = update(current ?? EvidenceState.Empty);
        }
    }

    private static IReadOnlyList<NativeFatComtradeRecordEvidence> ProjectComtrade(
        IEnumerable<Iec61850FaultRecordSet>? records,
        CancellationToken cancellationToken)
    {
        if (records == null)
            return Array.Empty<NativeFatComtradeRecordEvidence>();

        var projected = new Dictionary<string, NativeFatComtradeRecordEvidence>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record?.Files == null)
                continue;

            var validFiles = record.Files
                .Where(file => file != null &&
                    (!string.IsNullOrWhiteSpace(file.RemotePath) || !string.IsNullOrWhiteSpace(file.Name)))
                .GroupBy(
                    file => !string.IsNullOrWhiteSpace(file.RemotePath) ? file.RemotePath.Trim() : file.Name.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            if (validFiles.Length == 0)
                continue;

            var name = FirstNonEmpty(
                record.BaseName,
                record.RecordId,
                validFiles[0].BaseName,
                validFiles[0].Name);
            if (name.Length == 0)
                continue;

            var knownSize = record.KnownSizeBytes > 0
                ? record.KnownSizeBytes
                : SumKnownSize(validFiles);
            var date = record.LastModifiedUtc ?? validFiles
                .Where(file => file.LastModifiedUtc.HasValue)
                .Select(file => file.LastModifiedUtc)
                .Max();
            var evidence = new NativeFatComtradeRecordEvidence(
                name,
                date?.ToUniversalTime(),
                knownSize,
                record.HasUnknownSize || validFiles.Any(file => !file.SizeBytes.HasValue),
                "OK");

            var recordKey = FirstNonEmpty(record.RecordId, $"{record.RemoteDirectory}/{name}");
            projected[recordKey] = evidence;
        }

        return projected.Values
            .OrderByDescending(record => record.RecordDateUtc)
            .ThenBy(record => record.RecordName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static NativeFatTimeSyncReportEvidence ProjectTimeSync(
        NativeFatTimeSyncDiagnosticResult diagnostic,
        DateTimeOffset evaluatedAtUtc)
    {
        var supporting = new List<NativeFatTimeSyncPointEvidence>(2);
        if (diagnostic.LtmsPresent)
        {
            NativeFatTimeSyncPointEvidence? ltms = null;
            NativeFatTimeSyncPointEvidence? timestamp = null;
            foreach (var point in diagnostic.PrimaryEvidence)
            {
                if (!point.Trusted)
                    continue;
                if (point.Role.Equals("LTMS", StringComparison.OrdinalIgnoreCase))
                    ltms ??= point;
                else
                    timestamp ??= point;
                if (ltms != null && timestamp != null)
                    break;
            }
            if (ltms != null) supporting.Add(ltms);
            if (timestamp != null) supporting.Add(timestamp);
        }
        else
        {
            foreach (var point in diagnostic.PrimaryEvidence)
            {
                if (point.Trusted)
                    supporting.Add(point);
                if (supporting.Count == 2)
                    break;
            }
        }

        return new NativeFatTimeSyncReportEvidence(
            evaluatedAtUtc,
            diagnostic.Verdict,
            diagnostic.Summary,
            diagnostic.LtmsPresent,
            diagnostic.FreshPrimaryTimestampCount,
            supporting.Select(point => new NativeFatTimeSyncReportPoint(
                Copy(point.Role),
                Copy(point.SignalName),
                Copy(point.IecReference),
                Copy(point.Value),
                Copy(point.Quality),
                Copy(point.DeviceTimestamp),
                point.DeltaSeconds,
                "OK")));
    }

    private static long SumKnownSize(IEnumerable<Iec61850FaultRecordFile> files)
    {
        long total = 0;
        foreach (var file in files)
        {
            var size = (long)(file.SizeBytes ?? 0u);
            total = long.MaxValue - total < size ? long.MaxValue : total + size;
        }
        return total;
    }

    private static string ResolveStableIedKey(Iec61850MonitorDevice device)
    {
        var namedIdentity = new[] { device.SclIedName, device.Name }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !IsPlaceholderIdentity(value.Trim()))
            ?.Trim() ?? string.Empty;
        if (namedIdentity.Length > 0)
            return $"ied:{namedIdentity}";

        if (!string.IsNullOrWhiteSpace(device.IpAddress))
            return $"endpoint:{device.IpAddress.Trim()}:{device.Port}";

        return $"device:{device.DeviceId.Trim()}";
    }

    private static bool IsPlaceholderIdentity(string value)
        => value.Equals("IED", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("New IED", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("Device", StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string Copy(string? value)
        => value?.Trim() ?? string.Empty;

    private sealed record EvidenceState(
        DateTimeOffset? ComtradeVerifiedAtUtc,
        IReadOnlyList<NativeFatComtradeRecordEvidence> ComtradeRecords,
        NativeFatTimeSyncReportEvidence? TimeSync)
    {
        public static EvidenceState Empty { get; } =
            new(null, Array.Empty<NativeFatComtradeRecordEvidence>(), null);
    }
}
