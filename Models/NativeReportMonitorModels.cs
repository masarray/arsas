namespace ArIED61850Tester.Models;

public sealed class NativeReportMonitorStartResult
{
    public bool IsSuccess { get; init; }
    public string PlanId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string SubscriptionSummary { get; init; } = string.Empty;
    public int MemberCount { get; init; }
    public int WriteStepCount { get; init; }
    public bool UsedDynamicDataSet { get; init; }
    public string ReportControlReference { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string AcquisitionLabel { get; init; } = string.Empty;
    public IReadOnlyList<string> CoveredReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class NativeReportMonitorSliceResult
{
    public string PlanId { get; init; } = string.Empty;
    public int ReportCount { get; init; }
    public int PollReadCount { get; init; }
    public int UnroutedReportCount { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<NativeReportFrameMetadata> ReportFrames { get; init; } = Array.Empty<NativeReportFrameMetadata>();
    public IReadOnlyList<NativeReportValueUpdate> Updates { get; init; } = Array.Empty<NativeReportValueUpdate>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class NativeReportMonitorStopResult
{
    public bool IsSuccess { get; init; }
    public string PlanId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class NativeReportFrameMetadata
{
    public string ReportControlReference { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public ulong? SequenceNumber { get; init; }
    public ulong? SubSequenceNumber { get; init; }
    public bool? MoreSegmentsFollow { get; init; }
    public bool? BufferOverflow { get; init; }
    public ulong? ConfRev { get; init; }
    public string EntryIdHex { get; init; } = string.Empty;
    public string ReportTimestamp { get; init; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; init; }
}

public sealed class NativeReportValueUpdate
{
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Value { get; init; } = "-";
    public string Quality { get; init; } = string.Empty;
    public string Timestamp { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Source { get; init; } = "report";
    public string ProjectionStatus { get; init; } = string.Empty;
    public bool HasValue { get; init; } = true;
    public bool HasQuality { get; init; }
    public bool HasTimestamp { get; init; }
    public string ReportControlReference { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public ulong? SequenceNumber { get; init; }
    public ulong? ConfRev { get; init; }
    public string ReportTimestamp { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
}
