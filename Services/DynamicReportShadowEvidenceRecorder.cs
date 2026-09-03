using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Thread-safe bounded recorder used by the upcoming physical G2.6 collector.
/// It accepts only the exact qualified DataSet index/member sequence supplied at
/// construction time. It never synthesizes quality/timestamps and performs no I/O.
/// </summary>
internal sealed class DynamicReportShadowEvidenceRecorder
{
    internal const int MaximumReportObservations = 4096;
    internal const int MaximumPollObservations = 16384;

    private readonly object _sync = new();
    private readonly string _evidenceId;
    private readonly string[] _memberReferences;
    private readonly List<ArMms.MmsDynamicReportShadowReportObservation> _reports = new();
    private readonly List<ArMms.MmsDynamicReportShadowPollObservation> _polls = new();
    private int _reconnectAttempts;
    private int _successfulReconnects;
    private int _reportResubscriptionsAfterReconnect;
    private int _pollReferenceRecoveriesAfterReconnect;
    private int _dynamicActivationAttempts;

    public DynamicReportShadowEvidenceRecorder(
        string evidenceId,
        IReadOnlyList<string> exactMemberReferences)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceId);
        ArgumentNullException.ThrowIfNull(exactMemberReferences);
        if (exactMemberReferences.Count == 0)
            throw new ArgumentException("At least one exact qualified member is required.", nameof(exactMemberReferences));

        _evidenceId = evidenceId.Trim();
        _memberReferences = exactMemberReferences.Select(reference =>
        {
            var normalized = NormalizeReference(reference);
            if (normalized.Length == 0)
                throw new ArgumentException("Qualified member references cannot be empty.", nameof(exactMemberReferences));
            return normalized;
        }).ToArray();

        if (_memberReferences.Distinct(StringComparer.OrdinalIgnoreCase).Count() != _memberReferences.Length)
            throw new ArgumentException("Qualified member references must be duplicate-free.", nameof(exactMemberReferences));
    }

    public void RecordReport(
        int dataSetIndex,
        string memberReference,
        string value,
        string? quality,
        DateTimeOffset? deviceTimestampUtc,
        DateTimeOffset receivedAtUtc,
        ulong? sequenceNumber)
    {
        ValidateExactMember(dataSetIndex, memberReference);
        lock (_sync)
        {
            if (_reports.Count >= MaximumReportObservations)
                throw new InvalidOperationException($"Shadow report evidence exceeded the bounded limit of {MaximumReportObservations} observations.");

            _reports.Add(new ArMms.MmsDynamicReportShadowReportObservation
            {
                DataSetIndex = dataSetIndex,
                MemberReference = _memberReferences[dataSetIndex],
                Value = NormalizeValue(value),
                Quality = NormalizeOptional(quality),
                DeviceTimestampUtc = deviceTimestampUtc,
                ReceivedAtUtc = receivedAtUtc,
                SequenceNumber = sequenceNumber
            });
        }
    }

    public void RecordPoll(
        int dataSetIndex,
        string memberReference,
        string value,
        string? quality,
        DateTimeOffset? deviceTimestampUtc,
        DateTimeOffset readAtUtc)
    {
        ValidateExactMember(dataSetIndex, memberReference);
        lock (_sync)
        {
            if (_polls.Count >= MaximumPollObservations)
                throw new InvalidOperationException($"Shadow polling evidence exceeded the bounded limit of {MaximumPollObservations} observations.");

            _polls.Add(new ArMms.MmsDynamicReportShadowPollObservation
            {
                DataSetIndex = dataSetIndex,
                MemberReference = _memberReferences[dataSetIndex],
                Value = NormalizeValue(value),
                Quality = NormalizeOptional(quality),
                DeviceTimestampUtc = deviceTimestampUtc,
                ReadAtUtc = readAtUtc
            });
        }
    }

    public void RecordDynamicActivationAttempt()
    {
        lock (_sync)
            _dynamicActivationAttempts++;
    }

    public void RecordReconnectAttempt()
    {
        lock (_sync)
            _reconnectAttempts++;
    }

    public void RecordReconnectSuccess(
        bool reportResubscribed,
        bool pollReferenceRecovered)
    {
        lock (_sync)
        {
            _successfulReconnects++;
            if (reportResubscribed)
                _reportResubscriptionsAfterReconnect++;
            if (pollReferenceRecovered)
                _pollReferenceRecoveriesAfterReconnect++;
        }
    }

    public ArMms.MmsDynamicReportShadowVerificationEvidence BuildEvidence(DateTimeOffset observedAtUtc)
    {
        lock (_sync)
        {
            return new ArMms.MmsDynamicReportShadowVerificationEvidence
            {
                EvidenceId = _evidenceId,
                ObservedAtUtc = observedAtUtc,
                MemberReferences = _memberReferences.ToArray(),
                ReportObservations = _reports.ToArray(),
                PollObservations = _polls.ToArray(),
                ReconnectAttempts = _reconnectAttempts,
                SuccessfulReconnects = _successfulReconnects,
                ReportResubscriptionsAfterReconnect = _reportResubscriptionsAfterReconnect,
                PollReferenceRecoveriesAfterReconnect = _pollReferenceRecoveriesAfterReconnect,
                DynamicActivationAttempts = _dynamicActivationAttempts
            };
        }
    }

    private void ValidateExactMember(int dataSetIndex, string memberReference)
    {
        if (dataSetIndex < 0 || dataSetIndex >= _memberReferences.Length)
            throw new ArgumentOutOfRangeException(nameof(dataSetIndex), dataSetIndex, $"Shadow DataSet index must be inside 0..{_memberReferences.Length - 1}.");

        var normalized = NormalizeReference(memberReference);
        if (!_memberReferences[dataSetIndex].Equals(normalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Shadow observation identity mismatch at DataSet index {dataSetIndex}: expected={_memberReferences[dataSetIndex]}, actual={normalized}.");
        }
    }

    private static string NormalizeReference(string? reference)
        => NormalizeOptional(reference).Replace('$', '.');

    private static string NormalizeValue(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized.Length == 0)
            throw new ArgumentException("Shadow process value cannot be empty.", nameof(value));
        return normalized;
    }

    private static string NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
