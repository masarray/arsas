using AR.Iec61850.Mms;
using ArIED61850Tester.Protocol.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Immutable evidence captured by one successful, explicit observation.
/// A quality or UTC evidence object is present only when the raw field was
/// actually decoded; a normalized legacy label never proves individual bits.
/// </summary>
public sealed record IedBrowserObservedValue(
    string Value, string Quality, string DeviceTimestamp, string Source, DateTime ObservedUtc,
    long Revision = 0, long Association = 0, bool IsStale = false,
    MmsQualityEvidence? QualityBits = null,
    Iec61850UtcTimeEvidence? TimeBits = null);

internal sealed record IedBrowserObservationChannels(
    IedBrowserObservedValue? Report = null,
    IedBrowserObservedValue? Read = null);

/// <summary>
/// One bounded-to-observed-references registry per IED. Report and manual Read
/// remain separate, so a late Read can never overwrite report evidence during
/// monitoring. One short lock makes publication, version and association
/// transitions atomic, without UI callbacks, model walks, or network work.
/// </summary>
public sealed class IedBrowserLiveValues
{
    private readonly object _gate = new();
    // IEC object names are case-sensitive. FC labels are separately normalized.
    private readonly Dictionary<string, IedBrowserObservationChannels> _values =
        new(StringComparer.Ordinal);
    private long _version;
    private long _association;
    private bool _offline;
    public long Version { get { lock (_gate) return _version; } }
    public long Association { get { lock (_gate) return _association; } }
    public bool IsOffline { get { lock (_gate) return _offline; } }

    public static string CanonicalReference(string? reference)
    {
        var input = (reference ?? "").Trim();
        var dollar = input.IndexOf('$');
        if (dollar < 0) return input;
        var domainLn = input[..dollar];
        var segments = input[(dollar + 1)..].Split('$', StringSplitOptions.RemoveEmptyEntries);
        // MMS LN$FC$DO$DA: remove FC exactly, not LD/LN or the object identity.
        return segments.Length >= 2
            ? domainLn + "." + string.Join(".", segments.Skip(1))
            : input.Replace('$', '.');
    }

    private static string Key(string reference, string fc)
        => CanonicalReference(reference) + "|" + (fc ?? "").Trim().ToUpperInvariant();

    /// <summary>New association invalidates old observations, including queued reads.</summary>
    public long BeginAssociation()
    {
        lock (_gate)
        {
            _association++;
            _offline = false;
            _values.Clear();
            _version++;
            return _association;
        }
    }

    /// <summary>Keep last-seen evidence only as explicitly offline/stale history.</summary>
    public void MarkOffline()
    {
        lock (_gate)
        {
            _offline = true;
            _association++; // invalidate in-flight callbacks even before reconnect
            _version++;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _association++;
            _offline = true;
            _values.Clear();
            _version++;
        }
    }

    public void Publish(string reference, string fc, string? value, string? quality,
        string? timestamp, string source)
        => TryPublish(reference, fc, value, quality, timestamp, source, expectedAssociation: null,
            utcTimeEvidence: null);

    /// <summary>
    /// Ordered, association-bound publication. A callback from a cancelled or
    /// replaced MMS session cannot publish into a new association's evidence.
    /// </summary>
    public bool PublishForAssociation(long association, string reference, string fc,
        string? value, string? quality, string? timestamp, string source,
        Iec61850UtcTimeEvidence? utcTimeEvidence = null)
        => TryPublish(reference, fc, value, quality, timestamp, source, association,
            utcTimeEvidence);

    private bool TryPublish(string reference, string fc, string? value, string? quality,
        string? timestamp, string source, long? expectedAssociation,
        Iec61850UtcTimeEvidence? utcTimeEvidence)
    {
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(fc) ||
            string.IsNullOrWhiteSpace(value) ||
            value.Trim() is "-" or "Not read" or "Unknown")
            return false;

        var key = Key(reference, fc);
        var isReport = source.StartsWith("Report", StringComparison.Ordinal);
        lock (_gate)
        {
            if (_offline || (expectedAssociation.HasValue &&
                             expectedAssociation.Value != _association))
                return false;

            _values.TryGetValue(key, out var channels);
            channels ??= new IedBrowserObservationChannels();
            var observed = new IedBrowserObservedValue(value.Trim(), quality ?? "",
                timestamp ?? "", source, DateTime.UtcNow, _version + 1, _association,
                QualityBits: IsQualityReference(key) &&
                    MmsQualityDecoder.TryDecodeRendered(value, out var decodedQuality)
                        ? decodedQuality : null,
                TimeBits: IsTimeReference(key) &&
                    utcTimeEvidence is { IsDecoded: true, HasWireProvenance: true }
                        ? utcTimeEvidence : null);
            _values[key] = isReport
                ? channels with { Report = observed }
                : channels with { Read = observed };
            _version++;
            return true;
        }
    }

    private static bool IsQualityReference(string key)
    {
        var fcIndex = key.LastIndexOf('|');
        return fcIndex > 2 && key.AsSpan(0, fcIndex).EndsWith(
            ".q".AsSpan(), StringComparison.Ordinal);
    }

    private static bool IsTimeReference(string key)
    {
        var fcIndex = key.LastIndexOf('|');
        return fcIndex > 2 && key.AsSpan(0, fcIndex).EndsWith(
            ".t".AsSpan(), StringComparison.Ordinal);
    }

    public void PublishReport(Iec61850MonitorPoint point, bool observedReportIngress = false)
    {
        if (point.IsOffline || point.Value == "-" ||
            !observedReportIngress &&
            !point.SourceMode.Contains("Report", StringComparison.OrdinalIgnoreCase) &&
            point.RoutedReportControlReferences.Count == 0)
            return;
        Publish(point.IecReference, point.FunctionalConstraint, point.Value,
            point.Quality, point.DeviceTimestamp, "Report");
        if (point.Quality is not ("Unknown" or "-"))
            Publish(point.QualityReference, point.FunctionalConstraint, point.Quality,
                "", point.DeviceTimestamp, "Report quality");
        if (point.DeviceTimestamp is not ("Unknown" or "-"))
            Publish(point.TimestampReference, point.FunctionalConstraint, point.DeviceTimestamp,
                "", point.DeviceTimestamp, "Report timestamp");
    }

    /// <summary>
    /// Return the authoritative reported snapshot during monitoring. When the
    /// monitor is stopped, the newest verified observation wins by revision.
    /// No chosen source synthesizes SOE or process events.
    /// </summary>
    public bool TryGet(string reference, string fc,
        out IedBrowserObservedValue evidence, bool preferReport = true,
        IedBrowserSchemaIndex? schema = null)
    {
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(fc))
        {
            evidence = null!;
            return false;
        }
        lock (_gate)
        {
            if (TryGetExact(Key(reference, fc), preferReport, out evidence))
                return true;
            // Only the exact engineering schema can authorize an alias from
            // DO to its unique scalar DA. Do not guess from IEC suffixes and do
            // not reverse-map DO scalars into unsupported child q/t/DA values.
            if (schema != null &&
                schema.TryGetPrimaryLeaf(reference, fc, out var exactLeaf) &&
                TryGetExact(Key(exactLeaf, fc), preferReport, out evidence))
                return true;
            evidence = null!;
            return false;
        }
    }

    private bool TryGetExact(string key, bool preferReport, out IedBrowserObservedValue evidence)
    {
        if (!_values.TryGetValue(key, out var channels))
        {
            evidence = null!;
            return false;
        }
        var selected = preferReport
            ? channels.Report ?? channels.Read
            : channels.Report?.Revision > channels.Read?.Revision
                ? channels.Report : channels.Read ?? channels.Report;
        if (selected is null)
        {
            evidence = null!;
            return false;
        }
        evidence = _offline ? selected with { IsStale = true } : selected;
        return true;
    }
}

public sealed record IedBrowserReadTarget(string Reference, string FunctionalConstraint, string DataType);
public sealed record IedBrowserReadResult(
    IedBrowserReadTarget Target, string Value, string Quality,
    string DeviceTimestamp, string Error);
public sealed record IedBrowserReadProgress(int Completed, int Total);
