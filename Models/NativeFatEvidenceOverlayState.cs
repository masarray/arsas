namespace ArIED61850Tester.Models;

/// <summary>
/// FAT-only evidence payload layered over a canonical Engineering row.
/// </summary>
public sealed class NativeFatEvidenceSlotState
{
    public string Value1 { get; set; } = string.Empty;
    public string Value2 { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
}

/// <summary>
/// Per-IED UI/evidence state. EvidenceByRow is sparse and keyed by the canonical
/// Engineering point key; it is not a second signal/row collection.
/// </summary>
public sealed class NativeFatIedSessionCacheState
{
    public string? ActiveRowKey { get; set; }
    public int LastScrollIndex { get; set; }
    public Dictionary<string, NativeFatEvidenceSlotState> EvidenceByRow { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}
