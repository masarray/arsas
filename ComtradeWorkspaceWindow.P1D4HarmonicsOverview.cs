using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private const int MaxP1D4HarmonicOverviewSignals = 8;
    private ulong _p1d4LastRenderedHarmonicOverviewFrame = ulong.MaxValue;
    private string _p1d4LastRenderedHarmonicOverviewSignature = string.Empty;

    private IReadOnlyList<ComtradeSignalItem> ResolveP1D4HarmonicOverviewSignals()
    {
        if (SignalList.ItemsSource is IEnumerable<ComtradeSignalItem> source)
        {
            var checkedAnalogs = source
                .Where(item => item.IsAnalog && _disturbanceVisibleSignals.Contains(item))
                .Take(MaxP1D4HarmonicOverviewSignals)
                .ToArray();
            if (checkedAnalogs.Length > 0)
                return checkedAnalogs;
        }

        return _activeSignal is { IsAnalog: true } active
            ? new[] { active }
            : Array.Empty<ComtradeSignalItem>();
    }

    private static string BuildP1D4HarmonicOverviewSignature(IReadOnlyList<ComtradeSignalItem> signals)
        => string.Join(",", signals.Select(signal => signal.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private async Task<IReadOnlyList<P1D4HarmonicOverviewEntry>> LoadP1D4HarmonicOverviewAsync(
        IReadOnlyList<ComtradeSignalItem> signals,
        ulong referenceFrame,
        CancellationToken token)
    {
        var spectra = new ComtradeHarmonicSpectrum?[signals.Count];
        var missing = new List<(int Position, ComtradeSignalItem Signal)>();

        for (var index = 0; index < signals.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var key = new HarmonicCacheKey(signals[index].Index, referenceFrame);
            if (_harmonicFrameCache.TryGetValue(key, out var cached))
                spectra[index] = cached;
            else
                missing.Add((index, signals[index]));
        }

        if (missing.Count > 0)
        {
            await _nativeGate.WaitAsync(token);
            try
            {
                var loaded = await Task.Run(() =>
                {
                    var result = new ComtradeHarmonicSpectrum[missing.Count];
                    for (var index = 0; index < missing.Count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        result[index] = _record.ReadHarmonicSpectrum(
                            missing[index].Signal.Index,
                            referenceFrame,
                            25);
                    }
                    return result;
                }, token);

                for (var index = 0; index < missing.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var loadedSpectrum = loaded[index];
                    spectra[missing[index].Position] = loadedSpectrum;
                    if (_harmonicFrameCache.Count >= 384)
                        _harmonicFrameCache.Clear();
                    _harmonicFrameCache[new HarmonicCacheKey(missing[index].Signal.Index, referenceFrame)] = loadedSpectrum;
                }
            }
            finally
            {
                _nativeGate.Release();
            }
        }

        var entries = new List<P1D4HarmonicOverviewEntry>(signals.Count);
        for (var index = 0; index < signals.Count; index++)
        {
            if (spectra[index] is { } spectrum)
                entries.Add(new P1D4HarmonicOverviewEntry(signals[index], spectrum));
        }
        return entries;
    }

    private void PresentP1D4HarmonicOverview(
        ulong referenceFrame,
        double referenceMilliseconds,
        string signature,
        IReadOnlyList<P1D4HarmonicOverviewEntry> entries)
    {
        var referenceTimeText = FormatAnalysisReferenceTime(referenceMilliseconds);
        AnalysisReferenceTextBlock.Text =
            $"Analysis reference: H • frame {referenceFrame:N0} • {referenceTimeText}";

        var valid = entries
            .Where(entry => entry.Spectrum.Valid && entry.Spectrum.Bins.Count > 0)
            .ToArray();
        if (valid.Length == 0)
        {
            HarmonicsView.ShowMessage(
                "Harmonics comparison",
                "H does not contain a valid full-cycle harmonic window for the checked analog channels.");
            StatusTextBlock.Text = "Native ArdIrec harmonics • H • no valid checked analog spectra.";
            return;
        }

        var displays = valid.Select(entry =>
        {
            var metadata = _record.AnalogChannels[checked((int)entry.Signal.Index)];
            var spectrum = entry.Spectrum;
            return new ComtradeHarmonicOverviewSpectrum(
                entry.Signal.Title,
                metadata.Units,
                spectrum.DcComponent,
                spectrum.FundamentalRms,
                spectrum.ThdPercent,
                spectrum.DominantOrder,
                spectrum.DominantRms,
                spectrum.DominantPercent,
                spectrum.EstimatedSampleRateHz,
                spectrum.MaximumResolvableOrder,
                spectrum.Bins.Select(bin => new ComtradeHarmonicDisplayBin(
                    bin.Order,
                    bin.MagnitudeRms,
                    bin.PercentOfFundamental,
                    bin.AngleDegrees)).ToArray());
        }).ToArray();

        var maximumOrder = Math.Min(
            10,
            displays.Select(display => Math.Max(
                    display.MaximumResolvableOrder,
                    display.Bins.Count == 0 ? 0 : display.Bins.Max(bin => bin.Order)))
                .DefaultIfEmpty(0)
                .Max());

        HarmonicsView.ShowSpectra(
            "Harmonics comparison",
            $"H • {referenceTimeText} • {displays.Length} checked analog channel(s) • RMS + % fundamental • orders 0…{maximumOrder}",
            displays);

        _p1d4LastRenderedHarmonicOverviewFrame = referenceFrame;
        _p1d4LastRenderedHarmonicOverviewSignature = signature;
        _lastRenderedHarmonicKey = new HarmonicCacheKey(valid[0].Signal.Index, referenceFrame);
        StatusTextBlock.Text =
            $"Native ArdIrec harmonic comparison • H • {referenceTimeText} • {displays.Length} channel(s) • H0…H{maximumOrder}";
    }

    private readonly record struct P1D4HarmonicOverviewEntry(
        ComtradeSignalItem Signal,
        ComtradeHarmonicSpectrum Spectrum);
}
