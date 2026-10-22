using AR.Iec61850.SampledValues.Measurements;

namespace ArIED61850Tester.Services;

/// <summary>
/// DigSubAnalyzer-inspired optional-smpRate field interoperability.
/// Observes received smpCnt continuity and independent NIC capture clock across
/// >=80 ms (not a single inter-arrival delta). No assumed 80 points/cycle
/// and NO timebase or phasor when capture evidence is unconvincing.
/// Designed to stay allocation-free on the receive hot path.
/// </summary>
public sealed class SmvObservedTimebaseDetector
{
    private const double RateTolerance = 0.12;
    private readonly double _nominalHz;
    private ushort? _previous;
    private DateTimeOffset _begin;
    private DateTimeOffset _last;
    private int _consecutive;
    private ushort? _wrap;

    public SmvObservedTimebaseDetector(double nominalHz)
    {
        if (nominalHz is not (50 or 60)) throw new ArgumentOutOfRangeException(nameof(nominalHz));
        _nominalHz = nominalHz;
    }

    public void Reset()
    {
        _previous = null;
        _begin = default;
        _last = default;
        _consecutive = 0;
        _wrap = null;
    }

    public SvTimebaseResolution? Observe(ushort counter, DateTimeOffset capturedAt)
    {
        if (_previous is ushort prior)
        {
            var next = unchecked((ushort)(prior + 1));
            var isWrap = counter == 0 &&
                (prior is 3999 or 4799 or 12799 or 15359);
            if (counter != next && !isWrap)
                Reset();
            else if (isWrap)
                _wrap = (ushort)(prior + 1);
        }

        if (_last != default &&
            (capturedAt < _last.AddMilliseconds(-5) ||
             capturedAt - _last > TimeSpan.FromSeconds(1)))
            Reset();

        if (_begin == default)
            _begin = capturedAt;
        _previous = counter;
        _last = capturedAt;
        _consecutive++;

        var seconds = (capturedAt - _begin).TotalSeconds;
        if (_consecutive < 240 || seconds < 0.08 || seconds > 3.0)
            return null;

        var observedRate = (_consecutive - 1) / seconds;
        foreach (var pointsPerCycle in new[] { 80, 256 })
        {
            var rate = _nominalHz * pointsPerCycle;
            if (Math.Abs(observedRate - rate) > rate * RateTolerance)
                continue;
            if (_wrap.HasValue && Math.Abs(_wrap.Value - rate) > 0.5)
                continue;

            return new SvTimebaseResolution
            {
                NominalFrequencyHz = _nominalHz,
                SamplesPerCycle = pointsPerCycle,
                SampleCounterWrap = _wrap,
                Source = SvTimebaseSource.ObservedLegacyProtectionRate,
                Reason = $"Inferred from {_consecutive} contiguous smpCnt values over " +
                    $"{seconds:0.###} s with independent capture rate {observedRate:0} Hz " +
                    $"near {_nominalHz:0} × {pointsPerCycle} (operator frequency context; " +
                    "not hardware-certified timing)."
            };
        }

        return null;
    }

    public static bool IsSupportedWrap(ushort previous, int pointsPerCycle, double nominalHz)
        => previous is 3999 or 4799 or 12799 or 15359 &&
            Math.Abs(previous + 1 - pointsPerCycle * nominalHz) < 0.5;
}
