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
            utcTimeEvidence: null, qualityEvidence: null);

    /// <summary>
    /// Ordered, association-bound publication. A callback from a cancelled or
    /// replaced MMS session cannot publish into a new association's evidence.
    /// </summary>
    public bool PublishForAssociation(long association, string reference, string fc,
        string? value, string? quality, string? timestamp, string source,
        Iec61850UtcTimeEvidence? utcTimeEvidence = null,
        MmsQualityEvidence? qualityEvidence = null)
        => TryPublish(reference, fc, value, quality, timestamp, source, association,
            utcTimeEvidence, qualityEvidence);

    private bool TryPublish(string reference, string fc, string? value, string? quality,
        string? timestamp, string source, long? expectedAssociation,
        Iec61850UtcTimeEvidence? utcTimeEvidence,
        MmsQualityEvidence? qualityEvidence, bool onlyIfNoReport = false)
    {
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(fc) ||
            string.IsNullOrWhiteSpace(value) ||
            (string.Equals(source, "MMS Read", StringComparison.Ordinal) &&
             !IedBrowserReadValuePolicy.TryAcceptDisplay(value, out _, out _)) ||
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
            // The WPF coalesced point snapshot is a compatibility mirror.
            // It must never replace a real ingress Report observation,
            // especially typed q/t evidence from the original MMS bytes.
            if (onlyIfNoReport && channels.Report is not null)
                return false;
            var observed = new IedBrowserObservedValue(value.Trim(), quality ?? "",
                timestamp ?? "", source, DateTime.UtcNow, _version + 1, _association,
                QualityBits: IsQualityReference(key)
                    ? qualityEvidence ?? (MmsQualityDecoder.TryDecodeRendered(value,
                        out var decodedQuality) ? decodedQuality : null)
                    : null,
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
        if (fcIndex <= 2) return false;
        var reference = key.AsSpan(0, fcIndex);
        if (reference.EndsWith(".q".AsSpan(), StringComparison.Ordinal))
            return true;
        // IEC substitution quality has a distinct SV.subQ leaf. Its raw
        // 13-bit payload must remain available to the SAME typed quality
        // presenter used by ST.q; SV.subVal is only two-bit Dbpos and must
        // never be decoded as quality. Wrong FC or width stays unverified.
        return key.AsSpan(fcIndex + 1).SequenceEqual("SV".AsSpan()) &&
               reference.EndsWith(".subQ".AsSpan(), StringComparison.Ordinal);
    }

    private static bool IsTimeReference(string key)
    {
        var fcIndex = key.LastIndexOf('|');
        return fcIndex > 2 && key.AsSpan(0, fcIndex).EndsWith(
            ".t".AsSpan(), StringComparison.Ordinal) ||
            fcIndex > 2 && key.AsSpan(0, fcIndex).EndsWith(
                ".LActTm".AsSpan(), StringComparison.Ordinal);
    }

    public void PublishReport(Iec61850MonitorPoint point, bool observedReportIngress = false)
    {
        // A previously routed RCB is historical coverage, NOT proof that
        // this point snapshot is Report ingress. Never promote an MMS Read,
        // polling result, or status-only refresh into canonical Report channel
        // merely because this signal once appeared in an RCB.
        if (point.IsOffline || point.Value == "-" ||
            !observedReportIngress &&
            !point.SourceMode.Contains("Report", StringComparison.OrdinalIgnoreCase))
            return;
        // Primary Report ingress is published on the runtime receive lane.
        // This UI snapshot only fills gaps (legacy sessions or points), never
        // downgrades a typed Report q/t to the normalized "good"/date text.
        MirrorReportIfMissing(point.IecReference, point.FunctionalConstraint, point.Value,
            point.Quality, point.DeviceTimestamp, "Report");
        if (point.Quality is not ("Unknown" or "-"))
            MirrorReportIfMissing(point.QualityReference, point.FunctionalConstraint,
                point.Quality, "", point.DeviceTimestamp, "Report quality");
        if (point.DeviceTimestamp is not ("Unknown" or "-"))
            MirrorReportIfMissing(point.TimestampReference, point.FunctionalConstraint,
                point.DeviceTimestamp, "", point.DeviceTimestamp, "Report timestamp");
    }

    private void MirrorReportIfMissing(string reference, string fc,
        string? value, string? quality, string? timestamp, string source)
        => TryPublish(reference, fc, value, quality, timestamp, source,
            expectedAssociation: null, utcTimeEvidence: null, qualityEvidence: null,
            onlyIfNoReport: true);

    /// <summary>
    /// Return the authoritative reported snapshot during monitoring. When the
    /// monitor is stopped, the newest verified observation wins by revision.
    /// No chosen source synthesizes SOE or process events.
    /// </summary>
    public bool TryGet(string reference, string fc,
        out IedBrowserObservedValue evidence, bool preferReport = true,
        IedBrowserSchemaIndex? schema = null, string requiredEvidence = "")
    {
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(fc))
        {
            evidence = null!;
            return false;
        }
        lock (_gate)
        {
            if (TryGetExact(Key(reference, fc), preferReport, requiredEvidence, out evidence))
                return true;
            // Only the exact engineering schema can authorize an alias from
            // DO to its unique scalar DA. Do not guess from IEC suffixes and do
            // not reverse-map DO scalars into unsupported child q/t/DA values.
            if (schema != null &&
                schema.TryGetPrimaryLeaf(reference, fc, out var exactLeaf) &&
                TryGetExact(Key(exactLeaf, fc), preferReport, requiredEvidence, out evidence))
                return true;
            evidence = null!;
            return false;
        }
    }

    private bool TryGetExact(string key, bool preferReport, string requiredEvidence,
        out IedBrowserObservedValue evidence)
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
        // Process values retain normal Report authority. Only expanded q/t
        // subfields may use a verified direct MMS Read when the selected
        // Report provides a textual summary but no underlying bit evidence.
        // Never manufacture a bit from "good" or a rendered timestamp.
        if (ReferenceEquals(selected, channels.Report) &&
            channels.Read is { } explicitRead &&
            explicitRead.Revision > selected.Revision &&
            (requiredEvidence == "Quality" &&
                 selected.QualityBits is null && explicitRead.QualityBits is not null ||
             requiredEvidence == "Timestamp" &&
                 selected.TimeBits is null &&
                 explicitRead.TimeBits is { IsDecoded: true, HasWireProvenance: true }))
        {
            // A newer explicit read supplies the missing bit-level proof.
            // An older read can never decorate a newer Report with stale q/t,
            // and an old Report cannot override a newer untyped Read.
            selected = explicitRead;
        }
        evidence = _offline ? selected with { IsStale = true } : selected;
        return true;
    }
}

public sealed record IedBrowserReadTarget(string Reference, string FunctionalConstraint, string DataType);
public sealed record IedBrowserReadResult(
    IedBrowserReadTarget Target, string Value, string Quality,
    string DeviceTimestamp, string Error,
    Iec61850UtcTimeEvidence? UtcTimeEvidence = null,
    MmsQualityEvidence? QualityEvidence = null);
public sealed record IedBrowserReadProgress(int Completed, int Total);
