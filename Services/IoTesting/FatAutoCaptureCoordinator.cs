using System.Globalization;
using System.Text.RegularExpressions;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record FatAutoCaptureDecision(
    FatValueEvidence? Evidence,
    FatAutoCaptureStage Stage,
    string Message,
    FatValueEvidence? ShiftedValue1Evidence = null)
{
    public static FatAutoCaptureDecision None(FatAutoCaptureStage stage, string message)
        => new(null, stage, message);
}

/// <summary>
/// Automatic Value 1 / Value 2 latch. The first good readable value is latched as
/// Value 1 and the first meaningful different value as Value 2. Once both slots are
/// populated, later meaningful process changes keep a rolling pair: previous Value 2
/// becomes Value 1 and the newest process value becomes Value 2.
///
/// Explicit operator Snapshot/Recapture remains authoritative for the current pair until the
/// operator changes it again. Fully automatic pairs keep rolling so repeated FAT operations
/// cannot leave stale Value 1 / Value 2 in the report.
///
/// Report-backed process values are event evidence already, so they are accepted
/// immediately. Polling fallback keeps the three-sample analog settling guard so noisy
/// cyclic reads are not mistaken for a meaningful condition change.
/// </summary>
public sealed class FatAutoCaptureCoordinator
{
    internal const int AnalogStableSampleCount = 3;
    internal const double AnalogRelativeSettlingFraction = 0.0005d;
    private static readonly Regex NumericToken = new(
        @"[-+]?(?:\d+(?:[\.,]\d*)?|[\.,]\d+)(?:[eE][-+]?\d+)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Dictionary<string, AnalogCandidate> _analogCandidates =
        new(StringComparer.OrdinalIgnoreCase);

    public FatAutoCaptureDecision Observe(IoTestPointPlan point, IoTestObservation observation)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(observation);

        if (!point.WorkspaceSelected || !point.IsIncludedInFat || !point.TestEnabled || !point.ImportReady)
        {
            Clear(point);
            return FatAutoCaptureDecision.None(
                FatAutoCaptureStage.WaitingValue1,
                "Automatic capture is outside the active FAT scope.");
        }

        var (qualityVerdict, qualityReason) = IoTestTransitionEvaluator.EvaluateQuality(observation.Quality);
        if (qualityVerdict != IoEvidenceVerdict.Accepted)
        {
            Clear(point);
            return FatAutoCaptureDecision.None(
                point.HasValue1Evidence ? FatAutoCaptureStage.WaitingChange : FatAutoCaptureStage.WaitingValue1,
                $"Waiting for good-quality live value: {qualityReason}.");
        }

        if (string.IsNullOrWhiteSpace(observation.RawValue) || observation.RawValue.Trim() is "-" or "—")
        {
            Clear(point);
            return FatAutoCaptureDecision.None(
                point.HasValue1Evidence ? FatAutoCaptureStage.WaitingChange : FatAutoCaptureStage.WaitingValue1,
                "Waiting for a readable live value.");
        }

        var rollingPair = point.HasValue1Evidence && point.HasValue2Evidence;
        var operatorOwnedPair = rollingPair &&
            ((point.Runtime.Value1Evidence is { CaptureKind: not FatEvidenceCaptureKind.AutomaticValue }) ||
             (point.Runtime.Value2Evidence is { CaptureKind: not FatEvidenceCaptureKind.AutomaticValue }));
        if (operatorOwnedPair)
        {
            Clear(point);
            return FatAutoCaptureDecision.None(
                FatAutoCaptureStage.Complete,
                "Current Value 1 / Value 2 contains explicit operator evidence; automatic rolling is paused until the operator recaptures it.");
        }

        if (rollingPair && IsEquivalent(point.Value2Text, observation.RawValue))
        {
            Clear(point);
            return FatAutoCaptureDecision.None(
                FatAutoCaptureStage.Complete,
                "Current Value 1 / Value 2 pair is up to date; waiting for the next meaningful process change.");
        }

        var slot = point.HasValue1Evidence ? FatValueSlot.Value2 : FatValueSlot.Value1;
        var comparisonBaseline = rollingPair ? point.Value2Text : point.Value1Text;
        if (slot == FatValueSlot.Value2 && !rollingPair && IsEquivalent(point.Value1Text, observation.RawValue))
        {
            Clear(point);
            return FatAutoCaptureDecision.None(
                FatAutoCaptureStage.WaitingChange,
                "Value 1 is stable; waiting for a meaningful new condition.");
        }

        if (point.SignalKind != FatSignalKind.Analog || IsReportBacked(observation.AcquisitionSource))
        {
            Clear(point);
            var reportBackedAnalog = point.SignalKind == FatSignalKind.Analog;
            var evidence = CreateEvidence(slot, observation);
            return BuildDecision(point, evidence, rollingPair, reportBackedAnalog);
        }

        if (!TryParseNumeric(observation.RawValue, out var numeric))
        {
            Clear(point);
            return FatAutoCaptureDecision.None(
                slot == FatValueSlot.Value1 ? FatAutoCaptureStage.WaitingValue1 : FatAutoCaptureStage.WaitingChange,
                "Analog polling value is not numerically stable enough for automatic capture; manual Recapture remains available.");
        }

        var key = point.TestPointId;
        var tolerance = SettlingTolerance(numeric, comparisonBaseline);
        if (!_analogCandidates.TryGetValue(key, out var candidate) ||
            candidate.Slot != slot ||
            Math.Abs(numeric - candidate.Center) > tolerance)
        {
            _analogCandidates[key] = new AnalogCandidate(slot, numeric, numeric, numeric, 1);
            return FatAutoCaptureDecision.None(
                slot == FatValueSlot.Value1 ? FatAutoCaptureStage.WaitingValue1 : FatAutoCaptureStage.StabilizingValue2,
                slot == FatValueSlot.Value1 ? "Stabilizing polled Value 1…" : "Stabilizing polled Value 2…");
        }

        var next = candidate.Add(numeric);
        _analogCandidates[key] = next;
        var nextTolerance = SettlingTolerance(next.Center, comparisonBaseline);
        if (next.Count < AnalogStableSampleCount || next.Max - next.Min > nextTolerance)
        {
            return FatAutoCaptureDecision.None(
                slot == FatValueSlot.Value1 ? FatAutoCaptureStage.WaitingValue1 : FatAutoCaptureStage.StabilizingValue2,
                slot == FatValueSlot.Value1 ? "Stabilizing polled Value 1…" : "Stabilizing polled Value 2…");
        }

        _analogCandidates.Remove(key);
        return BuildDecision(point, CreateEvidence(slot, observation), rollingPair, reportBackedAnalog: false);
    }

    public void Clear(IoTestPointPlan point)
    {
        ArgumentNullException.ThrowIfNull(point);
        _analogCandidates.Remove(point.TestPointId);
    }

    public void Clear() => _analogCandidates.Clear();

    private static FatAutoCaptureDecision BuildDecision(
        IoTestPointPlan point,
        FatValueEvidence evidence,
        bool rollingPair,
        bool reportBackedAnalog)
    {
        if (rollingPair && evidence.Slot == FatValueSlot.Value2)
        {
            var previousValue2 = point.Runtime.Value2Evidence;
            var shiftedValue1 = previousValue2 == null
                ? null
                : previousValue2 with
                {
                    EvidenceId = Guid.NewGuid(),
                    Slot = FatValueSlot.Value1
                };

            // Decision construction is intentionally pure. The controller journals the
            // complete rolling-pair transition first and only then promotes both pointers.
            // This prevents Value 1 from advancing in memory if durable evidence append fails.
            return new FatAutoCaptureDecision(
                evidence,
                FatAutoCaptureStage.Complete,
                reportBackedAnalog
                    ? "Latest analog change captured from report-backed process data; Value 1 / Value 2 advanced to the newest transition pair."
                    : "Latest live change captured; Value 1 / Value 2 advanced to the newest transition pair.",
                shiftedValue1);
        }

        return new FatAutoCaptureDecision(
            evidence,
            evidence.Slot == FatValueSlot.Value1 ? FatAutoCaptureStage.WaitingChange : FatAutoCaptureStage.Complete,
            evidence.Slot == FatValueSlot.Value1
                ? reportBackedAnalog
                    ? "Stable analog Value 1 captured immediately from report-backed process data; waiting for a meaningful change."
                    : "Value 1 captured automatically; waiting for a meaningful change."
                : reportBackedAnalog
                    ? "Stable analog Value 2 captured immediately from the new report-backed process condition; later changes will keep the pair current."
                    : "Value 2 captured automatically from the new live condition; later changes will keep the pair current.");
    }

    private static bool IsReportBacked(string? acquisitionSource)
    {
        if (string.IsNullOrWhiteSpace(acquisitionSource))
            return false;

        var source = acquisitionSource.Trim();
        if (source.Contains("POLL", StringComparison.OrdinalIgnoreCase))
            return false;

        return source.Contains("BRCB", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("URCB", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("RCB", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("REPORT", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("INFORMATIONREPORT", StringComparison.OrdinalIgnoreCase);
    }

    private static FatValueEvidence CreateEvidence(FatValueSlot slot, IoTestObservation observation)
        => new(
            Guid.NewGuid(),
            slot,
            FatEvidenceCaptureKind.AutomaticValue,
            observation.RawValue.Trim(),
            observation.CapturedAt,
            observation.IedTimestamp,
            string.IsNullOrWhiteSpace(observation.Quality) ? "Unknown" : observation.Quality.Trim(),
            string.IsNullOrWhiteSpace(observation.AcquisitionSource) ? "Unknown" : observation.AcquisitionSource.Trim(),
            observation.Sequence,
            observation.ConnectionGeneration);

    private static bool IsEquivalent(string? baseline, string? current)
    {
        if (Iec61850MonitorPoint.AreSemanticallyEquivalent(baseline ?? string.Empty, current ?? string.Empty))
            return true;

        // Aggregate FAT rows such as "A=11, B=12, C=0" are one evidence value, not a
        // scalar analog. Numeric tolerance is valid only when each side contains exactly
        // one numeric token. Therefore changing phase C to 13 is always a new condition and
        // advances Value 2 to the full newest aggregate "A=11, B=12, C=13".
        return TryParseScalarNumeric(baseline, out var left) &&
               TryParseScalarNumeric(current, out var right) &&
               Math.Abs(left - right) <= SettlingTolerance(right, baseline);
    }

    private static bool TryParseScalarNumeric(string? raw, out double value)
    {
        value = 0d;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var matches = NumericToken.Matches(raw.Trim());
        if (matches.Count != 1)
            return false;

        var token = matches[0].Value;
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        token = token.Replace(',', '.');
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static double SettlingTolerance(double value, string? baseline)
    {
        var baselineMagnitude = TryParseNumeric(baseline, out var parsedBaseline)
            ? Math.Abs(parsedBaseline)
            : 0d;
        var scale = Math.Max(1d, Math.Max(Math.Abs(value), baselineMagnitude));
        return Math.Max(1e-9d, scale * AnalogRelativeSettlingFraction);
    }

    internal static bool TryParseNumeric(string? raw, out double value)
    {
        value = 0d;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var match = NumericToken.Match(raw.Trim());
        if (!match.Success)
            return false;

        var token = match.Value;
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        token = token.Replace(',', '.');
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private sealed record AnalogCandidate(
        FatValueSlot Slot,
        double Center,
        double Min,
        double Max,
        int Count)
    {
        public AnalogCandidate Add(double value)
        {
            var nextCount = Count + 1;
            return this with
            {
                Center = ((Center * Count) + value) / nextCount,
                Min = Math.Min(Min, value),
                Max = Math.Max(Max, value),
                Count = nextCount
            };
        }
    }
}