// DigSubAnalyzer presentation port. Original: src/ProcessBus.Core/Presentation/PhasorCalloutRouting.cs
// Copyright original contributors; GPL-3.0-or-later.
namespace ProcessBus.Core.Presentation;

/// <summary>
/// Stateless, allocation-free geometry for a phasor annotation leader.
/// Every route starts at the EXACT tip of its own measured vector.
/// Routing must never substitute a label-side tick for the tip connection.
/// </summary>
public readonly record struct PhasorCalloutPoint(double X, double Y);

public readonly record struct PhasorCalloutRoute(
    PhasorCalloutPoint Tip,
    PhasorCalloutPoint Knee,
    PhasorCalloutPoint LabelEdge);

public static class PhasorCalloutRouting
{
    public static PhasorCalloutRoute Route(
        PhasorCalloutPoint tip,
        PhasorLabelPosition label,
        double labelWidth,
        double labelHeight,
        bool onRight)
    {
        // The label edge is outside text bounds, never under the glyphs.
        var edge = new PhasorCalloutPoint(
            onRight ? label.X - 3.0 : label.X + labelWidth + 3.0,
            label.Y + labelHeight * 0.5);

        // Send each leader outwards from its OWN arrow tip, then diagonally
        // toward its reserved row and horizontally into its label. On aligned
        // Ua/Ia vectors this lifts the inner current leader clear of the longer
        // voltage shaft rather than tracing that shaft or joining its arrowhead.
        var direction = onRight ? 1.0 : -1.0;
        var available = Math.Max(0.0, (edge.X - tip.X) * direction);
        var outward = Math.Min(10.0, available * 0.42);
        var knee = new PhasorCalloutPoint(tip.X + direction * outward, edge.Y);

        return new PhasorCalloutRoute(tip, knee, edge);
    }
}
