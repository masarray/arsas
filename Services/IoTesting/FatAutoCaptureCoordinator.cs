using System.Globalization;
using System.Text.RegularExpressions;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record FatAutoCaptureDecision(
    FatValueEvidence? Evidence,
    FatAutoCaptureStage Stage,
    string Message)
{
    public static FatAutoCaptureDecision None(FatAutoCaptureStage stage, string message)
        => new(null, stage, message);
}

/// <summary>
/// P3 automatic Value 1 / Value 2 latch. This coordinator never mutates the current
/// evidence pointers: callers must durably append the returned evidence first, then
/// promote it. That keeps the evidence journal authoritative when storage fails.
///
/// Poll-backed analog values use a small deterministic settling window rather than
/// capturing every transient sample. Authoritative report/Static DataSet observations do
/// not necessarily repeat the same analog value, so one good report sample is accepted as
/// the stable condition. Discrete/Other values use semantic change and are latched
/// immediately after good-quality observation.
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

        if (point.HasValue1Evidence && point.HasValue2Evidence)
        {
            Clear(point);
            return FatAutoCaptureDecision.None(
                FatAutoCaptureStage.Complete,
                "Current Value 1 / Value 2 evidence is complete; explicit Recapture is required to replace it.");
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

        var slot = point.HasValue1Evidence ? FatValueSlot.Value2 : FatValueSlot.Value1;
        if (slot == FatValueSlot.Value2 && IsEquivalent(point.Value1Text, observation.RawValue))
        {
            Clear(point);
            return FatAutoCaptureDecision.None(
                FatAutoCaptureStage.WaitingChange,
                "Value 1 is stable; waiting for a meaningful new condition.");
        }

        if (point.SignalKind != FatSignalKind.Analog)
        {
            Clear(point);
            return new FatAutoCaptureDecision(
                CreateEvidence(slot, observation),
                slot == FatValueSlot.Value1 ? FatAutoCaptureStage.WaitingChange : FatAutoCaptureStage.Complete,
                slot == FatValueSlot.Value1
                    ? "Value 1 captured automatically; waiting for a meaningful change."
                    : "Value 2 captured automatically from the new live condition.");
        }

        if (!TryParseNumeric(observation.RawValue, out var numeric))
        {
            Clear(point);
            return FatAutoCaptureDecision.None(
                slot == FatValueSlot.Value1 ? FatAutoCaptureStage.WaitingValue1 : FatAutoCaptureStage.WaitingChange,
                "Analog value is not numeric enough for automatic capture; operator Recapture remains available after a valid live value is established.");
        }

        // Static DataSet / InformationReport acquisition is change-driven. Requiring three
        // identical reports would leave a perfectly steady analog point uncaptured forever.
        // One good report observation is already the authoritative relay image, so accept it
        // immediately while retaining the three-sample settling rule for polling sources.
        if (IsAuthoritativeReportObservation(observation))
        {
            Clear(point);
            return new FatAutoCaptureDecision(
                CreateEvidence(slot, observation),
                slot == FatValueSlot.Value1 ? FatAutoCaptureStage.WaitingChange : FatAutoCaptureStage.Complete,
                slot == FatValueSlot.Value1
                    ? "Report-backed analog Value 1 captured automatically; waiting for a meaningful new condition."
                    : "Report-backed analog Value 2 captured automatically; current evidence is complete.");
        }

        var key = point.TestPointId;
        var tolerance = SettlingTolerance(numeric, point.Value1Text);
        if (!_analogCandidates.TryGetValue(key, out var candidate) ||
            candidate.Slot != slot ||
            Math.Abs(numeric - candidate.Center) > tolerance)
        {
            _analogCandidates[key] = new AnalogCandidate(slot, numeric, numeric, numeric, 1);
            return FatAutoCaptureDecision.None(
                slot == FatValueSlot.Value1 ? FatAutoCaptureStage.WaitingValue1 : FatAutoCaptureStage.StabilizingValue2,
                slot == FatValueSlot.Value1 ? "Stabilizing Value 1…" : "Stabilizing Value 2…");
        }

        var next = candidate.Add(numeric);
        _analogCandidates[key] = next;
        var nextTolerance = SettlingTolerance(next.Center, point.Value1Text);
        if (next.Count < AnalogStableSampleCount || next.Max - next.Min > nextTolerance)
        {
            return FatAutoCaptureDecision.None(
                slot == FatValueSlot.Value1 ? FatAutoCaptureStage.WaitingValue1 : FatAutoCaptureStage.StabilizingValue2,
                slot == FatValueSlot.Value1 ? "Stabilizing Value 1…" : "Stabilizing Value 2…");
        }

        _analogCandidates.Remove(key);
        return new FatAutoCaptureDecision(
            CreateEvidence(slot, observation),
            slot == FatValueSlot.Value1 ? FatAutoCaptureStage.WaitingChange : FatAutoCaptureStage.Complete,
            slot == FatValueSlot.Value1
                ? "Stable analog Value 1 captured; waiting for a meaningful new condition."
                : "Stable analog Value 2 captured; current evidence is complete.");
    }

    public void Clear(IoTestPointPlan point)
    {
        ArgumentNullException.ThrowIfNull(point);
        _analogCandidates.Remove(point.TestPointId);
    }

    public void Clear() => _analogCandidates.Clear();

    private static bool IsAuthoritativeReportObservation(IoTestObservation observation)
    {
        var source = observation.AcquisitionSource?.Trim() ?? string.Empty;
        return source.Contains("Static DataSet", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("InformationReport", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("Report", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("URCB", StringComparison.OrdinalIgnoreCase) ||
               source.Contains("BRCB", StringComparison.OrdinalIgnoreCase);
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

        return TryParseNumeric(baseline, out var left) &&
               TryParseNumeric(current, out var right) &&
               Math.Abs(left - right) <= SettlingTolerance(right, baseline);
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
