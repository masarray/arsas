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
    Iec61850UtcTimeEvidence? TimeBits = null,
    bool? TypedBoolean = null,
    string ReadWireShapeEvidence = "");

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
    // One bounded cursor-based change journal for every workspace. SSOT holds
    // the actual immutable observations; the journal holds only dirty keys.
    private readonly CanonicalValueChangeJournal _changeJournal = new();
    public CanonicalValueChangeBatch ReadChanges(long afterRevision, int maxChanges = 256)
    {
        lock (_gate) return _changeJournal.Read(afterRevision, maxChanges);
    }

    private void RecordChange(string reference, string fc, CanonicalValueChangeKind kind)
    {
        // Called only while holding _gate, after _version advances exactly once.
        _changeJournal.Append(new CanonicalValueChange(
            _version, _association, CanonicalReference(reference),
            (fc ?? "").Trim().ToUpperInvariant(), kind));
    }
    // Only CF changes invalidate engineering-unit projection; ordinary ST/MX
    // report traffic must not rebuild its signal/unit directory.
    private long _configurationVersion;
    private long _association;
    private bool _offline;
    public long Version { get { lock (_gate) return _version; } }
    public long ConfigurationVersion { get { lock (_gate) return _configurationVersion; } }
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
            _configurationVersion++;
            _version++;
            RecordChange("", "", CanonicalValueChangeKind.AssociationInvalidated);
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
            _configurationVersion++;
            _version++;
            RecordChange("", "", CanonicalValueChangeKind.AssociationInvalidated);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _association++;
            _offline = true;
            _values.Clear();
            _configurationVersion++;
            _version++;
            RecordChange("", "", CanonicalValueChangeKind.AssociationInvalidated);
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
        MmsQualityEvidence? qualityEvidence = null,
        bool? typedBoolean = null,
        AR.Iec61850.Mms.MmsDataKind? verifiedStringWireKind = null,
        string? declaredDataType = null,
        string? readWireShapeEvidence = null)
        => TryPublish(reference, fc, value, quality, timestamp, source, association,
            utcTimeEvidence, qualityEvidence, typedBoolean: typedBoolean,
            verifiedStringWireKind: verifiedStringWireKind,
            declaredDataType: declaredDataType,
            readWireShapeEvidence: readWireShapeEvidence);

    /// <summary>
    /// Atomically attach a verified, exact-model-owned q or t Report companion
    /// to the SAME existing canonical process Report observation. This is a
    /// metadata-only revision: never generates a process observation, changes
    /// the raw value, upgrades a Read to Report, or borrows another FC/IED.
    /// The raw q/t leaf remains independently in this ledger for audit.
    /// Ownership is proven by the caller's exact schema/association route.
    /// </summary>
    public bool MergeVerifiedReportCompanionForAssociation(
        long association, string processReference, string fc,
        MmsQualityEvidence? qualityEvidence = null,
        Iec61850UtcTimeEvidence? timeEvidence = null)
    {
        if (string.IsNullOrWhiteSpace(processReference) ||
            string.IsNullOrWhiteSpace(fc) ||
            (qualityEvidence is null) == (timeEvidence is null) ||
            (timeEvidence is not null &&
             (!timeEvidence.IsDecoded || !timeEvidence.HasWireProvenance)))
            return false;

        var key = Key(processReference, fc);
        lock (_gate)
        {
            if (_offline || association == 0 || association != _association ||
                !_values.TryGetValue(key, out var channels) ||
                channels.Report is not { } prior ||
                prior.Association != association ||
                !prior.Source.StartsWith("Report", StringComparison.Ordinal))
                return false;

            var updated = prior with
            {
                Quality = qualityEvidence is null ? prior.Quality : qualityEvidence.Summary,
                DeviceTimestamp = timeEvidence is null
                    ? prior.DeviceTimestamp : timeEvidence.EngineeringUtc,
                QualityBits = qualityEvidence ?? prior.QualityBits,
                TimeBits = timeEvidence ?? prior.TimeBits,
                ObservedUtc = DateTime.UtcNow,
                Revision = _version + 1
            };
            _values[key] = channels with { Report = updated };
            _version++;
            RecordChange(processReference, fc, CanonicalValueChangeKind.ReportCompanion);
            return true;
        }
    }

    private bool TryPublish(string reference, string fc, string? value, string? quality,
        string? timestamp, string source, long? expectedAssociation,
        Iec61850UtcTimeEvidence? utcTimeEvidence,
        MmsQualityEvidence? qualityEvidence, bool onlyIfNoReport = false,
        bool? typedBoolean = null,
        AR.Iec61850.Mms.MmsDataKind? verifiedStringWireKind = null,
        string? declaredDataType = null,
        string? readWireShapeEvidence = null)
    {
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(fc) ||
            string.IsNullOrWhiteSpace(value) ||
            (string.Equals(source, "MMS Read", StringComparison.Ordinal) &&
             !IedBrowserReadValuePolicy.TryAcceptVerifiedMmsString(
                 value, declaredDataType, verifiedStringWireKind, out _, out _)) ||
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
            // Report process members often omit unchanged q/t. A previous
            // typed companion in this SAME association remains the latest
            // real evidence until replaced; a new value-only Report must not
            // revert the canonical process row to Unknown / no timestamp.
            // There is no cross-association carry: BeginAssociation clears all.
            var previousReport = isReport && source == "Report"
                ? channels.Report : null;
            var suppliedQuality = !string.IsNullOrWhiteSpace(quality) &&
                quality is not ("Unknown" or "Pending / q not supplied" or
                    "Unavailable / q not supplied");
            var suppliedTimestamp = !string.IsNullOrWhiteSpace(timestamp) &&
                timestamp != "-";
            var observed = new IedBrowserObservedValue(value.Trim(),
                suppliedQuality ? quality! : previousReport?.Quality ?? quality ?? "",
                suppliedTimestamp ? timestamp! :
                    previousReport?.DeviceTimestamp ?? timestamp ?? "",
                source, DateTime.UtcNow, _version + 1, _association,
                QualityBits: IsQualityReference(key)
                    ? qualityEvidence ?? (MmsQualityDecoder.TryDecodeRendered(value,
                        out var decodedQuality) ? decodedQuality : null)
                    : !suppliedQuality ||
                      string.Equals(quality, previousReport?.Quality,
                          StringComparison.OrdinalIgnoreCase)
                        ? previousReport?.QualityBits
                        : null,
                TimeBits: IsTimeReference(key) &&
                    utcTimeEvidence is { IsDecoded: true, HasWireProvenance: true }
                        ? utcTimeEvidence
                        : !suppliedTimestamp ||
                          string.Equals(timestamp, previousReport?.DeviceTimestamp,
                              StringComparison.Ordinal)
                            ? previousReport?.TimeBits : null,
                TypedBoolean: typedBoolean,
                ReadWireShapeEvidence: source == "MMS Read" &&
                    !string.IsNullOrWhiteSpace(readWireShapeEvidence)
                        ? readWireShapeEvidence[..Math.Min(192, readWireShapeEvidence.Length)]
                        : "");
            _values[key] = isReport
                ? channels with { Report = observed }
                : channels with { Read = observed };
            if (string.Equals(fc, "CF", StringComparison.OrdinalIgnoreCase))
                _configurationVersion++;
            _version++;
            RecordChange(reference, fc, isReport
                ? CanonicalValueChangeKind.Report
                : CanonicalValueChangeKind.Read);
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

    /// <summary>
    /// Last-resort canonical bridge for a real routed InformationReport where
    /// the typed primary route did not publish. Runtime lane only; no WPF and
    /// no fabricated q/t. Exact association check rejects reconnect races.
    /// Typed primary evidence always wins via onlyIfNoReport.
    /// </summary>
    public bool MirrorRoutedReportIfMissingForAssociation(long association,
        string reference, string fc, string? value, string? quality,
        string? timestamp)
        => association > 0 && TryPublish(reference, fc, value, quality,
            timestamp, "Report compatibility", expectedAssociation: association,
            utcTimeEvidence: null, qualityEvidence: null,
            onlyIfNoReport: true);

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
    MmsQualityEvidence? QualityEvidence = null,
    bool? TypedBoolean = null,
    AR.Iec61850.Mms.MmsDataKind? VerifiedStringWireKind = null,
    string WireShapeEvidence = "");
public sealed record IedBrowserReadProgress(int Completed, int Total);
