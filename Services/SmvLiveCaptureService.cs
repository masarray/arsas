using System.Buffers.Binary;
using System.Diagnostics;
using AR.Iec61850.SampledValues;
using AR.Iec61850.SampledValues.Measurements;
using AR.Iec61850.Transports;
using AR.Iec61850.Transports.Npcap;

namespace ArIED61850Tester.Services;

public sealed record SmvLiveExpectedStream(
    ushort? AppId, string SvId, string DestinationMac,
    ushort? SampleRate, ushort? SampleMode);

public sealed record SmvLiveCaptureOptions
{
    public required string AdapterSelector { get; init; }
    public double NominalFrequencyHz { get; init; } = 50;
    public int Cycles { get; init; } = 2;
    // Display-only assumption, NEVER a verified source sampling rate.
    public int? DisplaySamplesPerCycleOverride { get; init; }
    public int MaximumChannels { get; init; } = 8;
    public int MaximumStreams { get; init; } = 32;
    public int RefreshMilliseconds { get; init; } = 200;
    public IReadOnlyList<SmvLiveExpectedStream> ExpectedStreams { get; init; } = [];
}

public sealed record SmvLiveStreamSummary(
    string Key, string SvId, ushort AppId, string SourceMac,
    string DestinationMac, string DataSetReference, string Vlan,
    DateTimeOffset LastSeen, long Frames, long Asdus, long Gaps,
    long Missing, long Duplicates, long OutOfOrder, long Restarts,
    bool HasCoherentWindow, string Diagnostic);

public sealed record SmvLiveUpdate(
    IReadOnlyList<SmvLiveStreamSummary> Streams, string SelectedKey,
    SmvSnapshotResult? SelectedSnapshot, long TotalFrames, DateTimeOffset EmittedAt);

/// <summary>
/// One passive reader for all publisher identities. The callback is bounded
/// to a presentation cadence; no packet produces a UI task of its own.
/// </summary>
public sealed class SmvLiveCaptureService
{
    public async Task RunAsync(SmvLiveCaptureOptions options,
        Func<string?> selectedKey, Action<SmvLiveUpdate> publish,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(selectedKey);
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AdapterSelector);
        if (options.NominalFrequencyHz is not (50 or 60) ||
            options.Cycles is < 1 or > 10 ||
            options.MaximumChannels is < 1 or > 16 ||
            options.MaximumStreams is < 1 or > 64 ||
            options.RefreshMilliseconds is < 100 or > 2000 ||
            options.DisplaySamplesPerCycleOverride is not (null or 80 or 256))
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid bounded SV capture configuration.");

        var catalog = new SmvLiveStreamCatalog(options);
        using var source = new NpcapProcessBusFrameSource(options.AdapterSelector);
        var stopwatch = Stopwatch.StartNew();
        long lastPublished = -options.RefreshMilliseconds;
        var captureOptions = new ProcessBusCaptureOptions
        {
            Filter = SmvSnapshotCaptureService.DefaultCaptureFilter,
            ReadTimeoutMilliseconds = 250,
            BufferCapacity = 4096
        };

        await foreach (var captured in source.CaptureAsync(captureOptions, cancellationToken)
                           .ConfigureAwait(false))
        {
            if (SampledValuesFrameParser.TryParseEthernetFrame(captured.Frame, out var frame))
                catalog.Receive(frame, captured.Timestamp);

            if (stopwatch.ElapsedMilliseconds - lastPublished < options.RefreshMilliseconds)
                continue;
            lastPublished = stopwatch.ElapsedMilliseconds;
            publish(catalog.Publish(selectedKey(), DateTimeOffset.UtcNow));
        }
    }
}

/// <summary>
/// Single-reader keyed publisher catalog. Capacity is explicitly bounded and
/// the least recently observed publisher is evicted when a new one arrives.
/// </summary>
public sealed class SmvLiveStreamCatalog
{
    private readonly SmvLiveCaptureOptions _options;
    private readonly Dictionary<string, SmvLiveStreamBuffer> _streams = new(StringComparer.Ordinal);
    private long _totalFrames;

    public SmvLiveStreamCatalog(SmvLiveCaptureOptions options) => _options = options;
    public int StreamCount => _streams.Count;

    public void Receive(SampledValuesFrame frame, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _totalFrames++;
        foreach (var asdu in frame.Pdu.Asdus)
        {
            if (asdu is null)
                continue;
            var key = SmvLiveStreamBuffer.ComposeKey(frame, asdu);
            if (!_streams.TryGetValue(key, out var stream))
            {
                if (_streams.Count >= _options.MaximumStreams)
                {
                    var oldest = _streams.Values.MinBy(item => item.LastSeen);
                    if (oldest is not null)
                        _streams.Remove(oldest.Key);
                }
                stream = new SmvLiveStreamBuffer(key, _options, FindExpected(frame, asdu));
                _streams.Add(key, stream);
            }
            stream.Receive(frame, asdu, timestamp, _totalFrames);
        }
    }

    public SmvLiveUpdate Publish(string? selectedKey, DateTimeOffset now)
    {
        var summaries = _streams.Values.OrderBy(s => s.SvId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.SourceMac, StringComparer.Ordinal)
            .Select(s => s.Summarize()).ToArray();
        var key = selectedKey is not null && _streams.ContainsKey(selectedKey)
            ? selectedKey : string.Empty;
        if (key.Length == 0 && string.IsNullOrEmpty(selectedKey) && summaries.Length > 0)
            key = summaries[0].Key;
        return new SmvLiveUpdate(summaries, key,
            key.Length > 0 ? _streams[key].CreateSnapshot() : null, _totalFrames, now);
    }

    private SmvLiveExpectedStream? FindExpected(SampledValuesFrame frame, SampledValueAsdu asdu)
    {
        var matches = _options.ExpectedStreams.Where(s =>
            (!s.AppId.HasValue || s.AppId.Value == frame.AppId) &&
            (string.IsNullOrWhiteSpace(s.SvId) ||
             s.SvId.Equals(asdu.SvId, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(s.DestinationMac) || s.DestinationMac == "-" ||
             MacKey(s.DestinationMac) == MacKey(frame.Destination.ToString())))
            .Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string MacKey(string mac)
        => new(mac.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());
}

/// <summary>
/// Continuity-qualified, fixed-size two-cycle ring. Direct big-endian INT32
/// reads avoid allocating per-ASDU generic inspection objects on the hot path.
/// Raw words are never relabeled as volts/amps without trusted mapping.
/// </summary>
public sealed class SmvLiveStreamBuffer
{
    private readonly SmvLiveCaptureOptions _options;
    private readonly SmvLiveExpectedStream? _expected;
    private readonly SvSampleCounterTracker _counter = new();
    private readonly SmvObservedTimebaseDetector _observedTimebase;
    private readonly SmvDeclaredRateCounterVerifier _declaredRateVerifier;
    private SvTimebaseResolution? _timebase;
    private double[][] _samples = [];
    private ushort[] _counters = [];
    private DateTimeOffset[] _times = [];
    private int[] _wordIndexes = [];
    private int _length, _next, _filled, _payloadLength;
    private uint _confRev;
    private byte _smpSynch;
    private ushort? _declaredRate, _declaredMode;
    private long _lastFrameOrdinal = -1;
    private DateTimeOffset _lastCaptureTimestamp;
    private long _gaps, _missing, _duplicates, _outOfOrder, _restarts;
    private string _diagnostic = "Waiting for decoded SV samples.";

    public SmvLiveStreamBuffer(string key, SmvLiveCaptureOptions options,
        SmvLiveExpectedStream? expected = null)
    {
        Key = key;
        _options = options;
        _expected = expected;
        _observedTimebase = new SmvObservedTimebaseDetector(options.NominalFrequencyHz);
        _declaredRateVerifier = new SmvDeclaredRateCounterVerifier(options.NominalFrequencyHz, options.Cycles);
    }

    public string Key { get; }
    public string SvId { get; private set; } = "";
    public ushort AppId { get; private set; }
    public string SourceMac { get; private set; } = "";
    public string DestinationMac { get; private set; } = "";
    public string Vlan { get; private set; } = "-";
    public string DataSetReference { get; private set; } = "";
    public DateTimeOffset LastSeen { get; private set; }
    public long Frames { get; private set; }
    public long Asdus { get; private set; }

    public static string ComposeKey(SampledValuesFrame frame, SampledValueAsdu asdu)
        => $"{frame.Source}|{frame.Destination}|{frame.AppId:X4}|{asdu.SvId}";

    public void Receive(SampledValuesFrame frame, SampledValueAsdu asdu,
        DateTimeOffset timestamp, long frameOrdinal)
    {
        if (_lastFrameOrdinal != frameOrdinal)
        {
            Frames++;
            _lastFrameOrdinal = frameOrdinal;
        }
        Asdus++;
        LastSeen = DateTimeOffset.UtcNow;
        // A capture pause or a discontinuous capture clock cannot silently
        // stitch old and new samples into one phasor window, even if smpCnt
        // happens to appear continuous after a full counter wrap.
        if (_lastCaptureTimestamp != default &&
            Math.Abs((timestamp - _lastCaptureTimestamp).TotalSeconds) > 1.0)
        {
            _restarts++;
            ResetWindow();
            _counter.Reset();
            _diagnostic = "Capture timing discontinuity; rebuilding coherent SV window.";
        }
        _lastCaptureTimestamp = timestamp;
        AppId = frame.AppId;
        SvId = asdu.SvId;
        SourceMac = frame.Source.ToString();
        DestinationMac = frame.Destination.ToString();
        Vlan = frame.Vlan?.VlanId.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";

        var payload = asdu.SamplePayload.AsSpan();
        if (payload.Length is 0 or > 65536 || payload.Length % 4 != 0)
        {
            ResetWindow();
            _counter.Reset();
            _diagnostic = "Invalid seqOfData alignment/length; coherent evidence withheld.";
            return;
        }

        if (_wordIndexes.Length == 0 || _payloadLength != payload.Length ||
            _confRev != asdu.ConfigurationRevision ||
            !DataSetReference.Equals(asdu.DataSetReference, StringComparison.Ordinal))
        {
            if (_wordIndexes.Length > 0) _restarts++;
            Configure(asdu, payload.Length);
        }
        // smpSynch is ASDU synchronization evidence, not a stream schema change.
        // Preserve coherent buffer and inferred timebase across 0 -> 2 transitions.
        _smpSynch = asdu.SampleSynchronization;

        if (_timebase?.IsResolved != true || _length == 0)
        {
            // smpRate/smpMod are optional in actual field SV streams.
            // Derive a bounded capture timebase from counter continuity AND
            // independently observed NIC timestamps. Do not fabricate samples.
            // Some real MUs advertise smpRate=4000/4800 while smpMod=0
            // (samples/period), which would request an impossible 8000+ point
            // two-cycle scope. That is contradictory metadata, not trustworthy
            // evidence of an 80-sample cycle. With a 64-byte fixed 4I+4V
            // payload, permit independent counter + NIC-clock confirmation.
            // Never override a plausible declared timebase or infer solely
            // from payload shape, chosen nominal Hz, or a single smpCnt.
            // Some physical publishers encode smpRate as samples/second
            // while smpMod is absent or interpreted as samples/period.
            // A declared two-cycle window larger than the bounded capture
            // capacity is unusable, regardless of seqOfData payload layout.
            // Independently corroborated NIC timing + contiguous smpCnt may
            // resolve the display window; never infer from payload shape alone.
            var invalidDeclaredTimebase = _timebase?.IsResolved == true &&
                (long)_timebase.SamplesPerCycle.GetValueOrDefault() * _options.Cycles > 4096;
            var mayObserve = _timebase?.IsResolved != true ||
                             invalidDeclaredTimebase ||
                             _declaredRate is null || _declaredMode is null;
            // On real NICs multiple ASDUs can have identical/batched capture
            // timestamps. An independently declared sample rate plus a whole
            // counter-contiguous window is sufficient for a conservative
            // display-only timebase, without fabricating samples or using the
            // nominal 50/60Hz setting alone as evidence.
            var declaredCounterProof = mayObserve
                ? _declaredRateVerifier.Observe(asdu.SampleCount, timestamp, _declaredRate, _declaredMode)
                : null;
            var observed = mayObserve
                ? _observedTimebase.Observe(asdu.SampleCount, timestamp)
                : null;
            var resolved = observed ?? declaredCounterProof;
            if (resolved is null)
            {
                _diagnostic = _declaredRateVerifier.ProgressText;
                return;
            }

            _timebase = invalidDeclaredTimebase
                ? resolved with
                {
                    Reason = "Conflicting on-wire smpRate/smpMod; display timebase corroborated " +
                             "by contiguous smpCnt and NIC-clock evidence; source channel semantics remain unverified. " +
                             resolved.Reason
                }
                : resolved;
            AllocateResolvedWindow();
            _counter.Reset();
        }

        // An observed 4000/4800/... Hz publisher may roll smpCnt at its
        // sampling frequency rather than at UInt16.MaxValue. Adopt a counter
        // modulus ONLY on the actually observed supported wrap edge.
        if (_timebase.SampleCounterWrap is null &&
            asdu.SampleCount == 0 && _counter.Last is ushort previous &&
            SmvObservedTimebaseDetector.IsSupportedWrap(previous,
                _timebase.SamplesPerCycle!.Value, _timebase.NominalFrequencyHz!.Value))
        {
            _timebase = _timebase with { SampleCounterWrap = (ushort)(previous + 1) };
            // The tracker cached its next expectation under unknown UInt16
            // modulus (4000, not 0). Re-prime using the JUST-OBSERVED
            // preceding sample so this proven normal rollover is not a gap.
            // Do not reset or splice the actual coherent sample ring.
            _counter.Reset();
            _counter.Observe(previous, _timebase.SampleCounterWrap);
        }

        var transition = _counter.Observe(asdu.SampleCount, _timebase.SampleCounterWrap);
        switch (transition.Kind)
        {
            case SvSampleCounterTransitionKind.Gap: _gaps++; _missing += transition.MissingSamples; break;
            case SvSampleCounterTransitionKind.Duplicate: _duplicates++; break;
            case SvSampleCounterTransitionKind.OutOfOrder: _outOfOrder++; break;
            case SvSampleCounterTransitionKind.Restart: _restarts++; break;
        }
        if (transition.Kind is SvSampleCounterTransitionKind.Gap or
            SvSampleCounterTransitionKind.Duplicate or
            SvSampleCounterTransitionKind.OutOfOrder or
            SvSampleCounterTransitionKind.Restart)
        {
            ResetWindow();
            _counter.Reset();
            _counter.Observe(asdu.SampleCount, _timebase.SampleCounterWrap);
            _diagnostic = $"Counter {transition.Kind}; collecting a new coherent window.";
        }

        for (var i = 0; i < _wordIndexes.Length; i++)
            _samples[i][_next] = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(_wordIndexes[i] * 4, 4));
        _counters[_next] = asdu.SampleCount;
        _times[_next] = timestamp;
        _next = (_next + 1) % _length;
        _filled = Math.Min(_length, _filled + 1);
        if (_filled == _length)
            _diagnostic = "Continuous SV samples · raw INT32, unscaled.";
    }

    public SmvLiveStreamSummary Summarize()
        => new(Key, SvId, AppId, SourceMac, DestinationMac,
            DataSetReference, Vlan, LastSeen, Frames, Asdus,
            _gaps, _missing, _duplicates, _outOfOrder, _restarts,
            _length > 0 && _filled == _length, _diagnostic);

    public SmvSnapshotResult? CreateSnapshot()
    {
        if (_length == 0 || _filled != _length || _timebase?.IsResolved != true)
            return null;
        var channels = new SmvSnapshotChannel[_samples.Length];
        for (var lane = 0; lane < channels.Length; lane++)
        {
            var ordered = new double[_length];
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            for (var i = 0; i < _length; i++)
            {
                var value = _samples[lane][(_next + i) % _length];
                ordered[i] = value;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
            var word = _wordIndexes[lane];
            channels[lane] = new SmvSnapshotChannel
            {
                ChannelIndex = lane + 1, PayloadWordIndex = word,
                Label = _payloadLength % 8 == 0
                    ? $"Raw group {lane + 1} · word {word + 1}" : $"Raw word {word + 1}",
                Interpretation = "Signed INT32; no trusted channel/scale mapping",
                Samples = ordered, Minimum = min, Maximum = max
            };
        }
        return new SmvSnapshotResult
        {
            StartedAt = _times[_next],
            CompletedAt = _times[(_next + _length - 1) % _length],
            AppId = AppId, SourceMac = SourceMac, DestinationMac = DestinationMac,
            VlanText = Vlan, StreamId = SvId, DataSetReference = DataSetReference,
            ConfigurationRevision = _confRev, SampleSynchronization = _smpSynch,
            DeclaredSampleRate = _declaredRate, DeclaredSampleMode = _declaredMode,
            NominalFrequencyHz = _timebase.NominalFrequencyHz ?? _options.NominalFrequencyHz,
            SamplesPerCycle = _timebase.SamplesPerCycle!.Value,
            CycleCount = _options.Cycles, TargetSamples = _length, CapturedSamples = _length,
            CapturedFrames = Frames, ParsedAsdus = Asdus,
            FirstSampleCount = _counters[_next],
            LastSampleCount = _counters[(_next + _length - 1) % _length],
            ContinuousTransitions = _length - 1,
            TimebaseReason = _timebase.Reason,
            PayloadShape = $"{_payloadLength} bytes; unscaled INT32",
            Channels = channels
        };
    }

    private void Configure(SampledValueAsdu asdu, int payloadLength)
    {
        ResetWindow();
        _counter.Reset();
        _observedTimebase.Reset();
        _declaredRateVerifier.Reset();
        DataSetReference = asdu.DataSetReference;
        _confRev = asdu.ConfigurationRevision;
        _smpSynch = asdu.SampleSynchronization;
        _payloadLength = payloadLength;
        _declaredRate = asdu.SampleRate ?? _expected?.SampleRate;
        _declaredMode = asdu.SampleMode ?? _expected?.SampleMode;
        var groups = payloadLength >= 8 && payloadLength % 8 == 0;
        _wordIndexes = Enumerable.Range(0, payloadLength / 4)
            .Where(i => !groups || i % 2 == 0)
            .Take(_options.MaximumChannels).ToArray();
        _timebase = SvTimebaseResolver.Resolve(new SvTimebaseEvidence
        {
            DeclaredSampleRate = _declaredRate,
            DeclaredSampleMode = _declaredMode,
            IsFixedLegacyProtectionLayout = groups && payloadLength == 64,
            TrustedNominalFrequencyHz = _options.NominalFrequencyHz
        });
        // A real SV publisher may omit smpRate/smpMod and provide unusable NIC
        // timestamp cadence. Only an EXPLICIT operator-selected display window
        // can bypass rate verification. Raw samples and smpCnt discontinuity
        // checks remain mandatory; this MUST NOT claim MU/PTP synchronization.
        if (!_timebase.IsResolved && _options.DisplaySamplesPerCycleOverride is int assumed)
        {
            _timebase = new SvTimebaseResolution
            {
                SamplesPerCycle = assumed,
                NominalFrequencyHz = _options.NominalFrequencyHz,
                Source = SvTimebaseSource.ObservedLegacyProtectionRate,
                Reason = $"ASSUMED display window {assumed} samples/cycle from operator selection. " +
                    "Sample rate and PTP sync NOT verified; counter continuity enforced."
            };
        }
        if (!_timebase.IsResolved)
        {
            _length = 0;
            _samples = [];
            _diagnostic = "Timebase pending: " +
                "checking smpCnt continuity and capture rate (optional smpRate/smpMod absent).";
            return;
        }
        AllocateResolvedWindow();
    }

    private void AllocateResolvedWindow()
    {
        var length = (long)_timebase!.SamplesPerCycle.GetValueOrDefault() * _options.Cycles;
        if ((length is < 4 or > 4096) &&
            _options.DisplaySamplesPerCycleOverride is int chosen &&
            _timebase is { IsResolved: true })
        {
            _timebase = new SvTimebaseResolution
            {
                SamplesPerCycle = chosen,
                NominalFrequencyHz = _options.NominalFrequencyHz,
                Source = SvTimebaseSource.ObservedLegacyProtectionRate,
                Reason = $"ASSUMED display window {chosen} samples/cycle (conflicting/unusable declared rate). " +
                    "Operator-selected, NOT verified sampling rate or PTP synchronization."
            };
            length = (long)chosen * _options.Cycles;
        }
        if (length is < 4 or > 4096 || _wordIndexes.Length == 0)
        {
            _length = 0;
            _samples = [];
            _diagnostic = "Timebase unresolved: declared smpRate/smpMod creates " +
                $"{length} samples for {_options.Cycles} cycles (limit 4096); " +
                "checking independent capture timestamps and contiguous smpCnt. " +
                _timebase.Reason;
            return;
        }
        _length = (int)length;
        _samples = Enumerable.Range(0, _wordIndexes.Length)
            .Select(_ => new double[_length]).ToArray();
        _counters = new ushort[_length];
        _times = new DateTimeOffset[_length];
        ResetWindow();
        _diagnostic = "Verified capture-rate evidence · collecting coherent SV samples. " +
            _timebase.Reason;
    }

    private void ResetWindow()
    {
        _next = 0;
        _filled = 0;
    }
}
