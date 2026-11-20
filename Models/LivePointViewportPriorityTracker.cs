using System.Collections.Concurrent;

namespace ArIED61850Tester.Models;

/// <summary>
/// Immutable identity-only viewport hints for the *existing* canonical point
/// presentation queue. No process value, session, event or IEC observation is
/// owned here. Independent Explorer and Global grids may realize the same row.
/// Concurrent reads are safe from the acquisition callback thread.
/// </summary>
public enum LivePointViewportKind { Explorer, Global }

public sealed class LivePointViewportPriorityTracker
{
    private readonly ConcurrentDictionary<string, byte> _explorer =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _global =
        new(StringComparer.Ordinal);

    private ConcurrentDictionary<string, byte> For(LivePointViewportKind viewport)
        => viewport == LivePointViewportKind.Global ? _global : _explorer;

    /// <returns>True only when this exact point first became realized.</returns>
    public bool Realize(LivePointViewportKind viewport, string exactKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exactKey);
        return For(viewport).TryAdd(exactKey, 0);
    }

    public void Unrealize(LivePointViewportKind viewport, string exactKey)
    {
        if (!string.IsNullOrWhiteSpace(exactKey))
            For(viewport).TryRemove(exactKey, out _);
    }

    public void Clear(LivePointViewportKind viewport) => For(viewport).Clear();

    public bool IsRealized(string exactKey)
        => !string.IsNullOrWhiteSpace(exactKey) &&
           (_explorer.ContainsKey(exactKey) || _global.ContainsKey(exactKey));

    public int RealizedCount => _explorer.Count + _global.Count;
}
