// Original DigSubAnalyzer PhasorDiagramControl; GPL-3.0-or-later
// Adapted to display two UNMAPPED RAW channel groups, not engineering U/I.
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ProcessBus.Core.Models;
using ProcessBus.Core.Presentation;

namespace ProcessBus.App.Wpf.Controls;

/// <summary>
/// A strictly presentational six-vector plot. Angles and magnitudes come ONLY from
/// the selected stream's engineering snapshot; no nominal templates, tweening,
/// phase correction, or packet-rate redraws are performed by this control.
/// </summary>
public sealed class PhasorDiagramControl : FrameworkElement
{
    private const double VoltageRadiusFactor = 0.96;
    private const double CurrentRadiusFactor = 0.73;
    private const double VoltageLineThickness = 2.7;
    private const double CurrentLineThickness = 2.4;

    private IReadOnlyList<PhasorDisplayItem> _visualItems = Array.Empty<PhasorDisplayItem>();

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable<PhasorDisplayItem>),
            typeof(PhasorDiagramControl),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnItemsSourceChanged));

    public static readonly DependencyProperty IsStreamLiveProperty =
        DependencyProperty.Register(
            nameof(IsStreamLive),
            typeof(bool),
            typeof(PhasorDiagramControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush PanelBrush = CreateBrush("#0C1B2B");
    private static readonly Brush TextSecondaryBrush = CreateBrush("#A7B8CB");
    private static readonly Brush TextMutedBrush = CreateBrush("#7E93AA");
    private static readonly Brush StaleBrush = CreateBrush("#F0B533");
    private static readonly Brush OriginBrush = CreateBrush("#EAF3FF");
    private static readonly Brush OriginInnerBrush = CreateBrush("#1D8FFF");

    private static readonly Brush PhaseRBrush = CreateBrush("#F02D4A");
    private static readonly Brush PhaseSBrush = CreateBrush("#F5B400");
    private static readonly Brush PhaseTBrush = CreateBrush("#258DF4");

    private static readonly Pen OuterBorderPen = CreatePen(CreateBrush("#1D8FFF"), 1.4);
    private static readonly Pen RingPen = CreatePen(CreateBrush("#28445E"), 1.0, [3.0, 5.0]);
    private static readonly Pen AxisPen = CreatePen(CreateBrush("#45627E"), 1.2);
    private static readonly Pen DiagonalPen = CreatePen(CreateBrush("#1D344E"), 0.85, [2.5, 5.0]);
    // Tinted, hairline leaders: voltage solid and current dashed, matching
    // their respective arrow families. Cached/frozen like all plot pens.
    private static readonly Pen[] VoltageLeaderPens =
    [
        CreatePen(CreateBrush("#A86877"), 0.95),
        CreatePen(CreateBrush("#C0A055"), 0.95),
        CreatePen(CreateBrush("#729FC8"), 0.95)
    ];
    private static readonly Pen[] CurrentLeaderPens =
    [
        CreatePen(CreateBrush("#A86877"), 1.05, [2.0, 2.0]),
        CreatePen(CreateBrush("#C0A055"), 1.05, [2.0, 2.0]),
        CreatePen(CreateBrush("#729FC8"), 1.05, [2.0, 2.0])
    ];
    private static readonly Pen[] VoltagePens =
    [
        CreatePen(PhaseRBrush, VoltageLineThickness),
        CreatePen(PhaseSBrush, VoltageLineThickness),
        CreatePen(PhaseTBrush, VoltageLineThickness)
    ];
    private static readonly Pen[] CurrentPens =
    [
        CreatePen(PhaseRBrush, CurrentLineThickness, [3.0, 1.8]),
        CreatePen(PhaseSBrush, CurrentLineThickness, [3.0, 1.8]),
        CreatePen(PhaseTBrush, CurrentLineThickness, [3.0, 1.8])
    ];
    private static readonly Pen[] CurrentArrowPens =
    [
        CreatePen(PhaseRBrush, CurrentLineThickness),
        CreatePen(PhaseSBrush, CurrentLineThickness),
        CreatePen(PhaseTBrush, CurrentLineThickness)
    ];

    private static readonly Typeface LabelTypeface =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface BodyTypeface =
        new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly record struct RenderedVector(PhasorDisplayItem Item, Point Tip, Brush Brush, FormattedText Label);

    public IEnumerable<PhasorDisplayItem>? ItemsSource
    {
        get => (IEnumerable<PhasorDisplayItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// False means the arrows are a historical HOLD, not evidence of live traffic.
    /// This flag is bound to the selected SV stream's existing IsActive state.
    /// </summary>
    public bool IsStreamLive
    {
        get => (bool)GetValue(IsStreamLiveProperty);
        set => SetValue(IsStreamLiveProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        if (obj is not PhasorDiagramControl control)
            return;

        // Copy just the small selected-stream presentation list when it changes.
        // Do not re-enumerate, allocate brushes/pens, or transform packets on repaint.
        control._visualItems = ((IEnumerable<PhasorDisplayItem>?)args.NewValue)?.ToArray() ?? [];
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Respect constrained WPF rows; requesting a larger plot causes label clipping.
        var width = double.IsInfinity(availableSize.Width) ? 290 : Math.Max(0, availableSize.Width);
        var height = double.IsInfinity(availableSize.Height) ? 245 : Math.Max(0, availableSize.Height);
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = RenderSize.Width;
        var height = RenderSize.Height;
        dc.DrawRectangle(PanelBrush, null, new Rect(0, 0, width, height));
        if (width < 130 || height < 130)
            return;

        // Reserve side rails for collision-free labels, rather than letting text
        // overlap the arrows or escape beyond the narrow analyzer workspace.
        var center = new Point(width / 2.0, height / 2.0 - 2);
        var radius = Math.Max(0, Math.Min(width / 2.0 - 69, height / 2.0 - 35));
        if (radius < 12)
            return;

        DrawGrid(dc, center, radius);

        var voltageMax = 0.0;
        var currentMax = 0.0;
        foreach (var item in _visualItems)
        {
            if (!IsRenderable(item))
                continue;
            if (item.Family == PhasorFamily.Voltage)
                voltageMax = Math.Max(voltageMax, item.Magnitude!.Value);
            else
                currentMax = Math.Max(currentMax, item.Magnitude!.Value);
        }

        if (voltageMax <= 0 && currentMax <= 0)
        {
            DrawCenteredMessage(dc, center, "Phasor data pending");
            return;
        }

        var vectors = new List<RenderedVector>(6);
        dc.PushOpacity(IsStreamLive ? 1.0 : 0.42);
        foreach (var item in _visualItems)
        {
            if (!IsRenderable(item))
                continue;

            var maximum = item.Family == PhasorFamily.Voltage ? voltageMax : currentMax;
            if (maximum <= 0)
                continue;

            // U and I use independent normalized radii. Never compare V with A
            // numerically or infer absolute magnitudes from the same circle.
            var fraction = Math.Clamp(item.Magnitude!.Value / maximum, 0.0, 1.0);
            var range = item.Family == PhasorFamily.Voltage
                ? VoltageRadiusFactor : CurrentRadiusFactor;
            var radians = item.AngleDegrees!.Value * Math.PI / 180.0;
            var length = radius * range * fraction;
            var tip = new Point(
                center.X + length * Math.Cos(radians),
                center.Y - length * Math.Sin(radians));
            var phase = PhaseIndex(item.Name);
            var brush = PhaseBrush(phase);
            var pen = item.Family == PhasorFamily.Voltage ? VoltagePens[phase] : CurrentPens[phase];
            var arrowPen = item.Family == PhasorFamily.Voltage ? pen : CurrentArrowPens[phase];

            dc.PushOpacity(ChannelFreshnessDisplay.Opacity(item.Freshness));
            dc.DrawLine(pen, center, tip);
            DrawArrow(dc, center, tip, arrowPen, item.Family == PhasorFamily.Voltage ? 9.5 : 8.0);
            dc.Pop();
            vectors.Add(new RenderedVector(item, tip, brush,
                CreateText(ToPhaseLabel(item.Name), 11.0, brush, LabelTypeface)));
        }

        dc.DrawEllipse(OriginInnerBrush, null, center, 4.2, 4.2);
        dc.DrawEllipse(OriginBrush, null, center, 2.25, 2.25);

        var anchors = new PhasorLabelAnchor[vectors.Count];
        for (var i = 0; i < vectors.Count; i++)
        {
            var vector = vectors[i];
            anchors[i] = new PhasorLabelAnchor(
                vector.Tip.X, vector.Tip.Y,
                vector.Label.Width, vector.Label.Height,
                vector.Tip.X >= center.X,
                vector.Item.Family == PhasorFamily.Current);
        }

        // Reserve two footer rows so label packing never obscures scale readouts.
        var positions = PhasorLabelLayout.Arrange(anchors, width, height,
            topPadding: IsStreamLive ? 18 : 30,
            bottomPadding: 42);

        for (var i = 0; i < vectors.Count; i++)
        {
            var vector = vectors[i];
            var position = positions[i];
            dc.PushOpacity(ChannelFreshnessDisplay.Opacity(vector.Item.Freshness));
            var route = PhasorCalloutRouting.Route(
                new PhasorCalloutPoint(vector.Tip.X, vector.Tip.Y),
                position, vector.Label.Width, vector.Label.Height, anchors[i].Right);
            var phase = PhaseIndex(vector.Item.Name);
            var leaderPen = vector.Item.Family == PhasorFamily.Voltage
                ? VoltageLeaderPens[phase] : CurrentLeaderPens[phase];

            // Always start at the actual measured vector TIP. Each fasor owns
            // its route; even collinear Ua/Ia may never share an invented anchor.
            // The elbow separates labels without drawing across the shaft.
            dc.DrawLine(leaderPen,
                new Point(route.Tip.X, route.Tip.Y),
                new Point(route.Knee.X, route.Knee.Y));
            dc.DrawLine(leaderPen,
                new Point(route.Knee.X, route.Knee.Y),
                new Point(route.LabelEdge.X, route.LabelEdge.Y));
            dc.DrawText(vector.Label, new Point(position.X, position.Y));
            dc.Pop();
        }
        dc.Pop();

        // Numeric scale evidence comes from the SAME six vectors as arrows.
        // This footer is a visual reference (per-family maximum), not a
        // calibration claim or one shared physical voltage/current scale.
        var voltageUnit = _visualItems.FirstOrDefault(item =>
            item.Family == PhasorFamily.Voltage && IsRenderable(item))?.Unit ?? "counts";
        var currentUnit = _visualItems.FirstOrDefault(item =>
            item.Family == PhasorFamily.Current && IsRenderable(item))?.Unit ?? "counts";
        var voltageScaleText = PhasorScaleCaption.Format("A", voltageMax, voltageUnit);
        var currentScaleText = PhasorScaleCaption.Format("B", currentMax, currentUnit);
        DrawText(dc, $"{voltageScaleText}   ·   {currentScaleText}",
            10.0, new Point(6, height - 31), TextSecondaryBrush, BodyTypeface);
        DrawText(dc, "RAW A solid · RAW B dashed · independent count scales",
            9.5, new Point(6, height - 15), TextMutedBrush, BodyTypeface);
        if (!IsStreamLive)
            DrawText(dc, "STALE · last valid samples", 11, new Point(7, 5), StaleBrush, LabelTypeface);
    }

    private static bool IsRenderable(PhasorDisplayItem item) =>
        item.HasValue &&
        double.IsFinite(item.Magnitude!.Value) &&
        double.IsFinite(item.AngleDegrees!.Value) &&
        item.Magnitude.Value > 0;

    private static int PhaseIndex(string name) => name switch
    {
        "Ub" or "Ib" or "Ch2" or "Ch6" => 1,
        "Uc" or "Ic" or "Ch3" or "Ch7" => 2,
        _ => 0
    };

    private static Brush PhaseBrush(int index) => index switch
    {
        1 => PhaseSBrush,
        2 => PhaseTBrush,
        _ => PhaseRBrush
    };

    private static string ToPhaseLabel(string name) => name switch
    {
        "Ua" => "Ua (R)",
        "Ub" => "Ub (S)",
        "Uc" => "Uc (T)",
        "Ia" => "Ia (R)",
        "Ib" => "Ib (S)",
        "Ic" => "Ic (T)",
        _ => name
    };

    private static void DrawArrow(DrawingContext dc, Point origin, Point tip, Pen pen, double size)
    {
        var direction = tip - origin;
        if (direction.LengthSquared < 1)
            return;

        direction.Normalize();
        var normal = new Vector(-direction.Y, direction.X);
        dc.DrawLine(pen, tip, tip - direction * size + normal * (size * 0.42));
        dc.DrawLine(pen, tip, tip - direction * size - normal * (size * 0.42));
    }

    private void DrawGrid(DrawingContext dc, Point center, double radius)
    {
        foreach (var fraction in new[] { 0.25, 0.5, 0.75, 1.0 })
            dc.DrawEllipse(null, fraction == 1.0 ? OuterBorderPen : RingPen,
                center, radius * fraction, radius * fraction);

        dc.DrawLine(AxisPen, new Point(center.X - radius, center.Y),
            new Point(center.X + radius, center.Y));
        dc.DrawLine(AxisPen, new Point(center.X, center.Y - radius),
            new Point(center.X, center.Y + radius));

        var offset = radius / Math.Sqrt(2);
        dc.DrawLine(DiagonalPen, new Point(center.X - offset, center.Y - offset),
            new Point(center.X + offset, center.Y + offset));
        dc.DrawLine(DiagonalPen, new Point(center.X + offset, center.Y - offset),
            new Point(center.X - offset, center.Y + offset));

        // Tick labels stay inside the ring so they cannot collide with side labels.
        DrawText(dc, "0°", 9, new Point(center.X + radius - 19, center.Y + 4), TextMutedBrush, BodyTypeface);
        DrawText(dc, "180°", 9, new Point(center.X - radius + 4, center.Y + 4), TextMutedBrush, BodyTypeface);
        DrawText(dc, "+90°", 9, new Point(center.X + 5, center.Y - radius + 3), TextMutedBrush, BodyTypeface);
        DrawText(dc, "270°", 9, new Point(center.X + 5, center.Y + radius - 14), TextMutedBrush, BodyTypeface);
    }

    private void DrawCenteredMessage(DrawingContext dc, Point center, string message)
    {
        var text = CreateText(message, 12, TextSecondaryBrush, BodyTypeface);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private void DrawText(DrawingContext dc, string value, double size, Point point, Brush brush, Typeface typeface) =>
        dc.DrawText(CreateText(value, size, brush, typeface), point);

    private FormattedText CreateText(string value, double size, Brush brush, Typeface typeface) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Brush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Brush brush, double thickness, double[]? dash = null)
    {
        var pen = new Pen(brush, thickness);
        if (dash is not null)
            pen.DashStyle = new DashStyle(dash, 0);
        pen.Freeze();
        return pen;
    }
}
