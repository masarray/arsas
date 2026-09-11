using System.Runtime.CompilerServices;
using System.Text;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private const int MaxP1D4HarmonicOverviewSignals = 8;
    private const int P1D4HarmonicCacheCapacity = 384;
    private ulong _p1d4LastRenderedHarmonicOverviewFrame = ulong.MaxValue;
    private string _p1d4LastRenderedHarmonicOverviewSignature = string.Empty;
    private ComtradeSignalItem[] _p1d4ResolvedHarmonicSignals = Array.Empty<ComtradeSignalItem>();
    private ulong _p1d4ResolvedHarmonicFingerprint;
    private string _p1d4ResolvedHarmonicSignature = string.Empty;
    private bool _p1d4ResolvedHarmonicSelectionInitialized;
    private readonly BoundedFifoCache<HarmonicCacheKey, ComtradeHarmonicSpectrum> _p1d4HarmonicFrameCache =
        new(P1D4HarmonicCacheCapacity);

    private IReadOnlyList<ComtradeSignalItem> ResolveP1D4HarmonicOverviewSignals()
    {
        if (SignalList.ItemsSource is IEnumerable<ComtradeSignalItem> source)
        {
            var fingerprint = 1469598103934665603UL;
            var checkedCount = 0;
            foreach (var item in source)
            {
                if (!item.IsAnalog || !_disturbanceVisibleSignals.Contains(item))
                    continue;
                MixP1D4HarmonicFingerprint(ref fingerprint, item);
                checkedCount++;
                if (checkedCount >= MaxP1D4HarmonicOverviewSignals)
                    break;
            }

            if (checkedCount > 0)
            {
                fingerprint ^= (ulong)checkedCount;
                fingerprint *= 1099511628211UL;
                if (_p1d4ResolvedHarmonicSelectionInitialized &&
                    _p1d4ResolvedHarmonicFingerprint == fingerprint &&
                    _p1d4ResolvedHarmonicSignals.Length == checkedCount)
                    return _p1d4ResolvedHarmonicSignals;

                var resolved = new ComtradeSignalItem[checkedCount];
                var write = 0;
                foreach (var item in source)
                {
                    if (!item.IsAnalog || !_disturbanceVisibleSignals.Contains(item))
                        continue;
                    resolved[write++] = item;
                    if (write >= resolved.Length)
                        break;
                }
                CacheP1D4HarmonicSelection(resolved, fingerprint);
                return _p1d4ResolvedHarmonicSignals;
            }
        }

        if (_activeSignal is { IsAnalog: true } active)
        {
            var fingerprint = 1469598103934665603UL;
            MixP1D4HarmonicFingerprint(ref fingerprint, active);
            fingerprint ^= 1UL;
            fingerprint *= 1099511628211UL;
            if (!_p1d4ResolvedHarmonicSelectionInitialized ||
                _p1d4ResolvedHarmonicFingerprint != fingerprint ||
                _p1d4ResolvedHarmonicSignals.Length != 1)
                CacheP1D4HarmonicSelection(new[] { active }, fingerprint);
            return _p1d4ResolvedHarmonicSignals;
        }

        CacheP1D4HarmonicSelection(Array.Empty<ComtradeSignalItem>(), 0);
        return _p1d4ResolvedHarmonicSignals;
    }

    private static void MixP1D4HarmonicFingerprint(ref ulong fingerprint, ComtradeSignalItem item)
    {
        fingerprint ^= item.Index;
        fingerprint *= 1099511628211UL;
        fingerprint ^= unchecked((uint)RuntimeHelpers.GetHashCode(item));
        fingerprint *= 1099511628211UL;
    }

    private void CacheP1D4HarmonicSelection(ComtradeSignalItem[] signals, ulong fingerprint)
    {
        _p1d4ResolvedHarmonicSignals = signals;
        _p1d4ResolvedHarmonicFingerprint = fingerprint;
        _p1d4ResolvedHarmonicSignature = BuildP1D4HarmonicOverviewSignatureCore(signals);
        _p1d4ResolvedHarmonicSelectionInitialized = true;
    }

    private string BuildP1D4HarmonicOverviewSignature(IReadOnlyList<ComtradeSignalItem> signals)
    {
        if (ReferenceEquals(signals, _p1d4ResolvedHarmonicSignals))
            return _p1d4ResolvedHarmonicSignature;
        return BuildP1D4HarmonicOverviewSignatureCore(signals);
    }

    private static string BuildP1D4HarmonicOverviewSignatureCore(IReadOnlyList<ComtradeSignalItem> signals)
    {
        if (signals.Count == 0)
            return string.Empty;
        var builder = new StringBuilder(signals.Count * 5);
        for (var index = 0; index < signals.Count; index++)
        {
            if (index > 0) builder.Append(',');
            builder.Append(signals[index].Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    private async Task<IReadOnlyList<P1D4HarmonicOverviewEntry>> LoadP1D4HarmonicOverviewAsync(
        IReadOnlyList<ComtradeSignalItem> signals,
        ulong referenceFrame,
        CancellationToken token)
    {
        var spectra = new ComtradeHarmonicSpectrum?[signals.Count];
        var missingPositions = new int[signals.Count];
        var missingSignals = new ComtradeSignalItem[signals.Count];
        var missingCount = 0;

        for (var index = 0; index < signals.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var key = new HarmonicCacheKey(signals[index].Index, referenceFrame);
            if (_p1d4HarmonicFrameCache.TryGetValue(key, out var cached))
            {
                spectra[index] = cached;
            }
            else
            {
                missingPositions[missingCount] = index;
                missingSignals[missingCount] = signals[index];
                missingCount++;
            }
        }

        if (missingCount > 0)
        {
            await _nativeGate.WaitAsync(token);
            try
            {
                var loaded = await Task.Run(() =>
                {
                    var result = new ComtradeHarmonicSpectrum[missingCount];
                    for (var index = 0; index < missingCount; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        result[index] = _record.ReadHarmonicSpectrum(
                            missingSignals[index].Index,
                            referenceFrame,
                            25);
                    }
                    return result;
                }, token);

                for (var index = 0; index < missingCount; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var loadedSpectrum = loaded[index];
                    var position = missingPositions[index];
                    var signal = missingSignals[index];
                    spectra[position] = loadedSpectrum;
                    _p1d4HarmonicFrameCache.Set(
                        new HarmonicCacheKey(signal.Index, referenceFrame),
                        loadedSpectrum);
                }
            }
            finally
            {
                _nativeGate.Release();
            }
        }

        var validCount = 0;
        for (var index = 0; index < spectra.Length; index++)
        {
            if (spectra[index] is not null)
                validCount++;
        }
        if (validCount == 0)
            return Array.Empty<P1D4HarmonicOverviewEntry>();

        var entries = new P1D4HarmonicOverviewEntry[validCount];
        var write = 0;
        for (var index = 0; index < signals.Count; index++)
        {
            if (spectra[index] is not { } spectrum)
                continue;
            entries[write++] = new P1D4HarmonicOverviewEntry(signals[index], spectrum);
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

        var validCount = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            var spectrum = entries[index].Spectrum;
            if (spectrum.Valid && spectrum.Bins.Count > 0)
                validCount++;
        }
        if (validCount == 0)
        {
            HarmonicsView.ShowMessage(
                "Harmonics comparison",
                "H does not contain a valid full-cycle harmonic window for the checked analog channels.");
            StatusTextBlock.Text = "Native ArdIrec harmonics • H • no valid checked analog spectra.";
            return;
        }

        var displays = new ComtradeHarmonicOverviewSpectrum[validCount];
        ComtradeSignalItem? firstValidSignal = null;
        var displayIndex = 0;
        var maximumOrder = 0;
        for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            var entry = entries[entryIndex];
            var spectrum = entry.Spectrum;
            if (!spectrum.Valid || spectrum.Bins.Count == 0)
                continue;

            firstValidSignal ??= entry.Signal;
            var metadata = _record.AnalogChannels[checked((int)entry.Signal.Index)];
            var bins = new ComtradeHarmonicDisplayBin[spectrum.Bins.Count];
            var binMaximum = 0;
            for (var binIndex = 0; binIndex < spectrum.Bins.Count; binIndex++)
            {
                var bin = spectrum.Bins[binIndex];
                bins[binIndex] = new ComtradeHarmonicDisplayBin(
                    bin.Order,
                    bin.MagnitudeRms,
                    bin.PercentOfFundamental,
                    bin.AngleDegrees);
                binMaximum = Math.Max(binMaximum, bin.Order);
            }

            displays[displayIndex++] = new ComtradeHarmonicOverviewSpectrum(
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
                bins);
            maximumOrder = Math.Max(maximumOrder, Math.Max(spectrum.MaximumResolvableOrder, binMaximum));
        }
        maximumOrder = Math.Min(10, maximumOrder);

        HarmonicsView.ShowSpectra(
            "Harmonics comparison",
            $"H • {referenceTimeText} • {displays.Length} checked analog channel(s) • RMS + % fundamental • orders 0…{maximumOrder}",
            displays);

        _p1d4LastRenderedHarmonicOverviewFrame = referenceFrame;
        _p1d4LastRenderedHarmonicOverviewSignature = signature;
        if (firstValidSignal is not null)
            _lastRenderedHarmonicKey = new HarmonicCacheKey(firstValidSignal.Index, referenceFrame);
        StatusTextBlock.Text =
            $"Native ArdIrec harmonic comparison • H • {referenceTimeText} • {displays.Length} channel(s) • H0…H{maximumOrder}";
    }

    private readonly record struct P1D4HarmonicOverviewEntry(
        ComtradeSignalItem Signal,
        ComtradeHarmonicSpectrum Spectrum);
}
