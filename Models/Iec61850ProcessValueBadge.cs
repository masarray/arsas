namespace ArIED61850Tester.Models;

/// <summary>
/// Immutable operator-only process badge. Cached on the command signal;
/// never participates in a write, SBO, or report/capture decision.
/// </summary>
public sealed record Iec61850ProcessValueBadge(
    string DisplayValue, string IecDataType, string ValueTypeToken, string ValueVisualKind);
