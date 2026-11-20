using AR.Iec61850.SampledValues.Measurements;

namespace ArIED61850Tester.Services;

/// <summary>
/// Conservatively rescues a field publisher whose smpRate is a rate in Hz
/// despite smpMod=0/absent. A multi-ASDU Ethernet frame shares one NIC
/// capture timestamp, so timestamp-rate estimation alone is insufficient.
/// Require an explicit rate matching the selected nominal system and a
/// complete, contiguous multi-cycle smpCnt sequence before display.
/// </summary>
public sealed class SmvDeclaredRateCounterVerifier
{
    private readonly double _nominal;
    private readonly int _cycles;
    private ushort? _last;
    private int _length;
    private ushort? _rate;
    private ushort? _mode;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _lastAt;
    public SmvDeclaredRateCounterVerifier(double nominal, int cycles)
    {
        if (nominal is not (50 or 60) || cycles is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(nominal));
        _nominal = nominal;
        _cycles = cycles;
    }

    public string ProgressText { get; private set; } =
        "Timebase unresolved: need plausible declared sampling rate and contiguous sample-counter evidence.";

    public void Reset()
    {
        _last = null;
        _length = 0;
        _rate = null;
        _mode = null;
        _startedAt = default;
        _lastAt = default;
        ProgressText = "Timebase unresolved: checking declared rate and counter continuity.";
    }

    public SvTimebaseResolution? Observe(ushort counter, DateTimeOffset capturedAt, ushort? declaredRate, ushort? declaredMode)
    {
        if (declaredRate != _rate || declaredMode != _mode)
        {
            Reset();
            _rate = declaredRate;
            _mode = declaredMode;
        }

        // Only contradictory field encodings that could be samples/second.
        // A valid conventional smpRate per-period timebase is handled by
        // the primary resolver; no semantics are inferred from payload shape.
        var rate = declaredRate.GetValueOrDefault();
        var points = (int)Math.Round(rate / _nominal);
        if (rate == 0 || Math.Abs(rate - points * _nominal) > 0.01 ||
            points is not (80 or 256) ||
            (long)points * _cycles > 4096 ||
            declaredMode is not (null or 0))
        {
            ProgressText = "Timebase unresolved: declared rate does not prove a supported display window; checking independent NIC clock.";
            return null;
        }

        var wrap = rate;
        if (_last is ushort prior)
        {
            var next = prior == wrap - 1 ? (ushort)0 : unchecked((ushort)(prior + 1));
            if (counter != next)
                _length = 0;
        }
        if (_lastAt != default &&
            (capturedAt < _lastAt.AddMilliseconds(-5) ||
             capturedAt - _lastAt > TimeSpan.FromSeconds(1)))
        {
            _length = 0;
            _startedAt = default;
        }
        if (_startedAt == default)
            _startedAt = capturedAt;
        _last = counter;
        _lastAt = capturedAt;
        _length++;
        var required = checked(points * _cycles);
        ProgressText = $"Timebase unresolved: {Math.Min(_length, required)}/{required} sequential smpCnt samples, declared {rate} samples/s at {_nominal:0} Hz.";
        // A completely frozen NIC clock is not independent evidence of any
        // active sampled-value rate. A batched multi-ASDU packet is fine:
        // several samples can share a timestamp, but across two complete
        // cycles the capture clock must advance by a plausible amount.
        var elapsedSeconds = (capturedAt - _startedAt).TotalSeconds;
        var idealSeconds = (required - 1.0) / rate;
        if (_length < required || elapsedSeconds < idealSeconds * 0.30 ||
            elapsedSeconds > idealSeconds * 5)
            return null;

        return new SvTimebaseResolution
        {
            NominalFrequencyHz = _nominal,
            SamplesPerCycle = points,
            SampleCounterWrap = wrap,
            Source = SvTimebaseSource.ObservedLegacyProtectionRate,
            Reason = $"Display-only rate: declared {rate} interpreted as samples/second with {_length} contiguous smpCnt samples at explicitly selected {_nominal:0} Hz. NIC clock advanced, but per-ASDU timestamp cadence unverified; verify publisher profile."
        };
    }
}
