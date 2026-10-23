using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester.Models;

/// <summary>
/// FAT-only evidence payload layered over one canonical Engineering row.
/// Value1/Value2 keep their raw text for backwards compatibility while the structured
/// FatValueEvidence objects preserve timestamp, quality, acquisition source and sequence.
/// </summary>
public sealed class NativeFatEvidenceSlotState
{
    public string Value1 { get; set; } = string.Empty;
    public string Value2 { get; set; } = string.Empty;
    public FatValueEvidence? Value1Evidence { get; set; }
    public FatValueEvidence? Value2Evidence { get; set; }
    public string Result { get; set; } = string.Empty;
}

public enum NativeFatEvidenceHydrationState
{
    NotStarted,
    Hydrating,
    Resolved,
    Failed
}

/// <summary>
/// Per-IED UI/evidence state. EvidenceByRow is sparse and keyed by stable
/// IEDName + IEC Telegram identity; it is not a second signal/row collection.
/// </summary>
public sealed class NativeFatIedSessionCacheState
{
    public string? ActiveRowKey { get; set; }
    public int LastScrollIndex { get; set; }
    public bool IsArmed { get; set; }
    public DateTimeOffset? ArmedAt { get; set; }
    public long LastArmElapsedMilliseconds { get; set; }

    // P2 evidence hydration is deliberately independent from canonical row binding.
    // Engineering rows render immediately; only the three sparse evidence columns wait.
    public NativeFatEvidenceHydrationState EvidenceHydrationState { get; set; } =
        NativeFatEvidenceHydrationState.NotStarted;
    public long EvidenceHydrationGeneration { get; set; }
    public DateTimeOffset? EvidenceHydratedAt { get; set; }
    public string EvidenceHydrationError { get; set; } = string.Empty;
    public long LastHydrationElapsedMilliseconds { get; set; }
    public bool IsEvidenceHydrating =>
        EvidenceHydrationState == NativeFatEvidenceHydrationState.Hydrating;

    public Dictionary<string, NativeFatEvidenceSlotState> EvidenceByRow { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}
