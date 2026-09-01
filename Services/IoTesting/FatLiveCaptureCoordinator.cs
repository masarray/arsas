using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Services.IoTesting;

public sealed record FatEvidenceHistoryEntry(
    long HistorySequence,
    string SignalId,
    FatValueEvidence Evidence);

/// <summary>
/// FAT v2 live-value bridge. Runtime identity stays exact and deterministic: one live IEC
/// reference may update several static DataSet-membership rows, but only when the IED identity
/// also matches. Operator disposition remains authoritative; removed rows receive no new live
/// image or evidence until restored.
/// </summary>
public sealed class FatLiveCaptureCoordinator
{
    private readonly FatVerificationProject _project;
    private readonly Dictionary<string, FatLiveValueObservation> _latestBySignalId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FatEvidenceHistoryEntry> _history = new();
    private long _historySequence;

    public FatLiveCaptureCoordinator(FatVerificationProject project)
    {
        _project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public IReadOnlyList<FatEvidenceHistoryEntry> History => _history.AsReadOnly();

    public FatLiveValueObservation? GetLatestObservation(string signalId)
    {
        if (string.IsNullOrWhiteSpace(signalId))
            return null;
        return _latestBySignalId.TryGetValue(signalId, out var observation) ? observation : null;
    }

    /// <summary>
    /// Applies a runtime image to every matching static membership. A genuine discrete edge
    /// atomically records its previous state as Value 1 and new state as Value 2, without any
    /// ON/OFF assumption. Subsequent edges append history first, then replace both current
    /// evidence pointers as one complete recapture.
    /// </summary>
    public IReadOnlyList<FatVerificationSignal> Observe(
        string runtimeReference,
        IEnumerable<string> iedAliases,
        string previousRawValue,
        bool isValueEdge,
        FatLiveValueObservation observation)
    {
        ArgumentNullException.ThrowIfNull(iedAliases);
        ArgumentNullException.ThrowIfNull(observation);

        var normalizedReference = NormalizeReference(runtimeReference);
        if (normalizedReference.Length == 0 || !IsReadable(observation.RawValue))
            return Array.Empty<FatVerificationSignal>();

        var aliases = iedAliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(NormalizeIdentity)
            .Where(alias => alias.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (aliases.Count == 0)
            return Array.Empty<FatVerificationSignal>();

        var matched = _project.Signals
            .Where(signal => signal.IsIncludedInFat)
            .Where(signal => aliases.Contains(NormalizeIdentity(signal.IedName)))
            .Where(signal => NormalizeReference(signal.RuntimeReference).Equals(
                normalizedReference,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var signal in matched)
        {
            _latestBySignalId[signal.SignalId] = observation;
            if (signal.CaptureMode != FatCaptureMode.AutomaticTransition ||
                !isValueEdge ||
                !IsReadable(previousRawValue) ||
                AreSemanticallyEquivalent(previousRawValue, observation.RawValue))
            {
                continue;
            }

            var (qualityVerdict, _) = IoTestTransitionEvaluator.EvaluateQuality(observation.Quality);
            if (qualityVerdict == IoEvidenceVerdict.Rejected)
                continue;

            var value1 = CreateAutomaticEvidence(
                FatValueSlot.Value1,
                previousRawValue,
                observation);
            var value2 = CreateAutomaticEvidence(
                FatValueSlot.Value2,
                observation.RawValue,
                observation);

            // Append-only history is written before current pointers are replaced. P5 owns
            // durable/package persistence of this history; the ordering invariant starts here.
            AppendHistory(signal, value1);
            AppendHistory(signal, value2);
            signal.SetCurrentEvidence(value1);
            signal.SetCurrentEvidence(value2);
        }

        return matched;
    }

    public FatValueEvidence CaptureOperatorSnapshot(string signalId, FatValueSlot slot)
    {
        var signal = _project.Signals.FirstOrDefault(item =>
            item.SignalId.Equals(signalId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"FAT signal '{signalId}' was not found.");

        if (!_latestBySignalId.TryGetValue(signal.SignalId, out var observation))
            throw new InvalidOperationException($"FAT signal '{signal.SignalName}' has no current live IED value to capture.");

        var evidence = FatOperatorSnapshotCaptureService.CreateEvidence(signal, slot, observation);
        AppendHistory(signal, evidence);
        signal.SetCurrentEvidence(evidence);
        return evidence;
    }

    private void AppendHistory(FatVerificationSignal signal, FatValueEvidence evidence)
    {
        _history.Add(new FatEvidenceHistoryEntry(
            checked(++_historySequence),
            signal.SignalId,
            evidence));
    }

    private static FatValueEvidence CreateAutomaticEvidence(
        FatValueSlot slot,
        string rawValue,
        FatLiveValueObservation observation)
        => new(
            Guid.NewGuid(),
            slot,
            FatEvidenceCaptureKind.AutomaticTransition,
            rawValue.Trim(),
            observation.CapturedAt,
            observation.IedTimestamp,
            string.IsNullOrWhiteSpace(observation.Quality) ? "Unknown" : observation.Quality.Trim(),
            string.IsNullOrWhiteSpace(observation.AcquisitionSource) ? "Unknown" : observation.AcquisitionSource.Trim(),
            observation.Sequence,
            observation.ConnectionGeneration);

    private static bool IsReadable(string? rawValue)
    {
        var value = (rawValue ?? string.Empty).Trim();
        return value.Length > 0 &&
               value is not "-" and not "—" &&
               !value.Equals("Pending", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool AreSemanticallyEquivalent(string? left, string? right)
    {
        var a = NormalizeValue(left);
        var b = NormalizeValue(right);
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return true;

        if (decimal.TryParse(a, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var an) &&
            decimal.TryParse(b, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var bn))
        {
            return an == bn;
        }

        return false;
    }

    private static string NormalizeValue(string? value)
    {
        var text = string.Join(' ', (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text switch
        {
            "true" or "on" or "active" or "asserted" => "1",
            "false" or "off" or "inactive" or "deasserted" => "0",
            _ => text
        };
    }

    private static string NormalizeIdentity(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string NormalizeReference(string? value)
        => (value ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".", StringComparison.Ordinal)
            .ToLowerInvariant();
}
