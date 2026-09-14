using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Controls;

internal sealed record ComtradeLocusSeries(
    string Loop,
    Color Color,
    IReadOnlyList<ComtradeDistancePoint> Points);

/// <summary>
/// Lightweight dual R-X renderer. Static trajectories/grid are cached in a frozen DrawingGroup;
/// C1/C2 overlays are drawn separately so cursor scrubbing never rebuilds the locus trajectories.
/// </summary>
internal sealed class ComtradeLocusView : FrameworkElement
{
    private static readonly Typeface Typeface = new("Segoe UI");
    private static readonly Brush TextBrush = FrozenBrush(Color.FromRgb(43, 60, 80));
    private static readonly Brush MutedBrush = FrozenBrush(Color.FromRgb(112, 130, 151));
    private static readonly Brush GridBrush = FrozenBrush(Color.FromRgb(229, 235, 242));
    private static readonly Brush AxisBrush = FrozenBrush(Color.FromRgb(151, 166, 184));
    private static readonly Brush BorderBrush = FrozenBrush(Color.FromRgb(211, 222, 235));
    private static readonly Brush Cursor1Brush = FrozenBrush(Color.FromRgb(218, 132, 20));
    private static readonly Brush Cursor2Brush = FrozenBrush(Color.FromRgb(24, 151, 190));
    private static readonly Pen GridPen = FrozenPen(GridBrush, 0.8);
    private static readonly Pen AxisPen = FrozenPen(AxisBrush, 1.0);
    private static readonly Pen BorderPen = FrozenPen(BorderBrush, 1.0);
    private static readonly Pen Cursor1Pen = FrozenPen(Cursor1Brush, 1.5);
    private static readonly Pen Cursor2Pen = FrozenPen(Cursor2Brush, 1.5);

    private string _title = "Protection locus";
    private string _subtitle = string.Empty;
    private string _representation = "Secondary";
    private IReadOnlyList<ComtradeLocusSeries> _earth = Array.Empty<ComtradeLocusSeries>();
    private IReadOnlyList<ComtradeLocusSeries> _phase = Array.Empty<ComtradeLocusSeries>();
    private IReadOnlyList<ComtradeDistancePoint> _cursor1 = Array.Empty<ComtradeDistancePoint>();
    private IReadOnlyList<ComtradeDistancePoint> _cursor2 = Array.Empty<ComtradeDistancePoint>();
    private DrawingGroup? _staticLayer;
    private double _staticWidth = double.NaN;
    private double _staticHeight = double.NaN;
    private PlotTransform _earthTransform;
    private PlotTransform _phaseTransform;

    internal ComtradeLocusView()
    {
        SnapsToDevicePixels = true;
        Focusable = false;
        MouseWheel += OnMouseWheel;
    }

    internal void ShowMessage(string title, string message)
    {
        _title = title;
        _subtitle = message;
        _earth = Array.Empty<ComtradeLocusSeries>();
        _phase = Array.Empty<ComtradeLocusSeries>();
        _cursor1 = Array.Empty<ComtradeDistancePoint>();
        _cursor2 = Array.Empty<ComtradeDistancePoint>();
        InvalidateStatic();
    }

    internal void ShowTrajectories(
        string title,
        string subtitle,
        string representation,
        IReadOnlyList<ComtradeLocusSeries> earth,
        IReadOnlyList<ComtradeLocusSeries> phase)
    {
        _title = title;
        _subtitle = subtitle;
        _representation = representation;
        _earth = earth ?? Array.Empty<ComtradeLocusSeries>();
        _phase = phase ?? Array.Empty<ComtradeLocusSeries>();
        InvalidateStatic();
    }

    internal void SetCursorPoints(
        IReadOnlyList<ComtradeDistancePoint>? cursor1,
        IReadOnlyList<ComtradeDistancePoint>? cursor2)
    {
        _cursor1 = cursor1 ?? Array.Empty<ComtradeDistancePoint>();
        _cursor2 = cursor2 ?? Array.Empty<ComtradeDistancePoint>();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        EnsureStaticLayer();
        if (_staticLayer is not null)
            dc.DrawDrawing(_staticLayer);
        DrawCursorOverlay(dc, _earthTransform, _cursor1, _cursor2, true);
        DrawCursorOverlay(dc, _phaseTransform, _cursor1, _cursor2, false);
    }

    private void EnsureStaticLayer()
    {
        if (_staticLayer is not null && NearlyEqual(_staticWidth, ActualWidth) && NearlyEqual(_staticHeight, ActualHeight))
            return;

        _staticWidth = ActualWidth;
        _staticHeight = ActualHeight;
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            DrawText(dc, _title, 15.0, FontWeights.SemiBold, TextBrush, new Point(16, 12));
            DrawText(dc, _subtitle, 9.5, FontWeights.Normal, MutedBrush, new Point(16, 35));

            if (_earth.Count == 0 && _phase.Count == 0)
            {
                DrawText(dc, _subtitle, 11.0, FontWeights.Normal, MutedBrush, new Point(20, 78));
            }
            else
            {
                const double gap = 12.0;
                const double left = 16.0;
                const double top = 62.0;
                var usableWidth = Math.Max(240.0, ActualWidth - left * 2 - gap);
                var panelWidth = usableWidth * 0.5;
                var panelHeight = Math.Max(250.0, ActualHeight - top - 46.0);
                var earthRect = new Rect(left, top, panelWidth, panelHeight);
                var phaseRect = new Rect(left + panelWidth + gap, top, panelWidth, panelHeight);
                _earthTransform = DrawPanel(dc, earthRect, "EARTH LOOPS", _earth);
                _phaseTransform = DrawPanel(dc, phaseRect, "PHASE-PHASE LOOPS", _phase);
            }
        }
        group.Freeze();
        _staticLayer = group;
    }

    private PlotTransform DrawPanel(
        DrawingContext dc,
        Rect panel,
        string header,
        IReadOnlyList<ComtradeLocusSeries> series)
    {
        dc.DrawRoundedRectangle(Brushes.White, BorderPen, panel, 5, 5);
        DrawText(dc, header, 10.0, FontWeights.SemiBold, TextBrush, new Point(panel.Left + 10, panel.Top + 8));
        DrawText(dc, $"R-X • {_representation} Ω", 8.7, FontWeights.Normal, MutedBrush,
            new Point(panel.Left + 10, panel.Top + 25));

        var plot = new Rect(panel.Left + 48, panel.Top + 48, Math.Max(80, panel.Width - 64), Math.Max(80, panel.Height - 82));
        var transform = BuildTransform(plot, series);
        DrawGrid(dc, transform);

        for (var index = 0; index < series.Count; index++)
            DrawSeries(dc, transform, series[index]);

        var legendX = panel.Left + 12;
        var legendY = panel.Bottom - 23;
        for (var index = 0; index < series.Count; index++)
        {
            var item = series[index];
            var pen = FrozenPen(FrozenBrush(item.Color), 1.6);
            dc.DrawLine(pen, new Point(legendX, legendY + 6), new Point(legendX + 16, legendY + 6));
            DrawText(dc, item.Loop, 8.4, FontWeights.SemiBold, TextBrush, new Point(legendX + 20, legendY));
            legendX += 76;
        }
        return transform;
    }

    private static PlotTransform BuildTransform(Rect plot, IReadOnlyList<ComtradeLocusSeries> series)
    {
        var rValues = new List<double>();
        var xValues = new List<double>();
        for (var seriesIndex = 0; seriesIndex < series.Count; seriesIndex++)
        {
            var points = series[seriesIndex].Points;
            for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                var point = points[pointIndex];
                if (!point.Valid || !double.IsFinite(point.R) || !double.IsFinite(point.X)) continue;
                rValues.Add(Math.Abs(point.R));
                xValues.Add(Math.Abs(point.X));
            }
        }

        var rHalf = RobustHalfRange(rValues);
        var xHalf = RobustHalfRange(xValues);
        var commonHalf = Math.Max(1e-6, Math.Max(rHalf, xHalf));
        var scale = Math.Min(plot.Width / (2.0 * commonHalf), plot.Height / (2.0 * commonHalf));
        scale = Math.Max(1e-9, scale * 0.90);
        return new PlotTransform(plot, plot.Left + plot.Width * 0.5, plot.Top + plot.Height * 0.5, scale, commonHalf);
    }

    private static double RobustHalfRange(List<double> values)
    {
        if (values.Count == 0) return 1.0;
        values.Sort();
        var index = Math.Clamp((int)Math.Floor((values.Count - 1) * 0.90), 0, values.Count - 1);
        var robust = values[index];
        if (!double.IsFinite(robust) || robust <= 1e-9)
            robust = values[^1];
        return Math.Max(1.0, robust * 1.15);
    }

    private static void DrawGrid(DrawingContext dc, PlotTransform transform)
    {
        var plot = transform.Plot;
        dc.DrawRectangle(null, BorderPen, plot);
        for (var i = 1; i < 8; i++)
        {
            var x = plot.Left + plot.Width * i / 8.0;
            var y = plot.Top + plot.Height * i / 8.0;
            dc.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
        dc.DrawLine(AxisPen, new Point(plot.Left, transform.CenterY), new Point(plot.Right, transform.CenterY));
        dc.DrawLine(AxisPen, new Point(transform.CenterX, plot.Top), new Point(transform.CenterX, plot.Bottom));
        DrawText(dc, "R", 9.0, FontWeights.SemiBold, TextBrush, new Point(plot.Right - 12, transform.CenterY + 4));
        DrawText(dc, "X", 9.0, FontWeights.SemiBold, TextBrush, new Point(transform.CenterX + 5, plot.Top + 2));
        DrawText(dc, "0", 7.8, FontWeights.Normal, MutedBrush, new Point(transform.CenterX + 4, transform.CenterY + 3));
        DrawText(dc, $"±{transform.HalfRange:G4} Ω", 7.8, FontWeights.Normal, MutedBrush,
            new Point(plot.Left + 3, plot.Top + 3));
    }

    private static void DrawSeries(DrawingContext dc, PlotTransform transform, ComtradeLocusSeries series)
    {
        var brush = FrozenBrush(series.Color);
        var pen = FrozenPen(brush, 1.25);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var open = false;
            var points = series.Points;
            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                if (!point.Valid || !double.IsFinite(point.R) || !double.IsFinite(point.X))
                {
                    open = false;
                    continue;
                }
                var mapped = transform.Map(point.R, point.X);
                if (!open)
                {
                    context.BeginFigure(mapped, false, false);
                    open = true;
                }
                else
                {
                    context.LineTo(mapped, true, false);
                }
            }
        }
        geometry.Freeze();
        dc.PushClip(new RectangleGeometry(transform.Plot));
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }

    private static void DrawCursorOverlay(
        DrawingContext dc,
        PlotTransform transform,
        IReadOnlyList<ComtradeDistancePoint> cursor1,
        IReadOnlyList<ComtradeDistancePoint> cursor2,
        bool earth)
    {
        if (!transform.Valid) return;
        DrawCursorSet(dc, transform, cursor1, Cursor1Pen, "C1", earth);
        DrawCursorSet(dc, transform, cursor2, Cursor2Pen, "C2", earth);
    }

    private static void DrawCursorSet(
        DrawingContext dc,
        PlotTransform transform,
        IReadOnlyList<ComtradeDistancePoint> points,
        Pen pen,
        string label,
        bool earth)
    {
        for (var index = 0; index < points.Count; index++)
        {
            var point = points[index];
            if (!point.Valid || !double.IsFinite(point.R) || !double.IsFinite(point.X)) continue;
            var isEarth = point.Loop <= ArdIrecLocusNativeSession.LoopL3E;
            if (isEarth != earth) continue;
            var mapped = transform.Map(point.R, point.X);
            if (!transform.Plot.Contains(mapped)) continue;
            const double radius = 4.5;
            dc.DrawLine(pen, new Point(mapped.X - radius, mapped.Y), new Point(mapped.X + radius, mapped.Y));
            dc.DrawLine(pen, new Point(mapped.X, mapped.Y - radius), new Point(mapped.X, mapped.Y + radius));
            DrawText(dc, $"{label} {LoopName(point.Loop)}", 7.5, FontWeights.SemiBold, pen.Brush,
                new Point(mapped.X + 6, mapped.Y - 12));
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Reserved for the next operator-controlled zoom increment. The current viewport is a
        // stable conformal robust fit; consuming wheel here would interfere with parent scrolling.
    }

    private void InvalidateStatic()
    {
        _staticLayer = null;
        _staticWidth = double.NaN;
        _staticHeight = double.NaN;
        InvalidateVisual();
    }

    internal static string LoopName(int loop) => loop switch
    {
        ArdIrecLocusNativeSession.LoopL1E => "L1-E",
        ArdIrecLocusNativeSession.LoopL2E => "L2-E",
        ArdIrecLocusNativeSession.LoopL3E => "L3-E",
        ArdIrecLocusNativeSession.LoopL1L2 => "L1-L2",
        ArdIrecLocusNativeSession.LoopL2L3 => "L2-L3",
        ArdIrecLocusNativeSession.LoopL3L1 => "L3-L1",
        _ => "?"
    };

    private static void DrawText(
        DrawingContext dc,
        string text,
        double size,
        FontWeight weight,
        Brush brush,
        Point point)
    {
        if (string.IsNullOrEmpty(text)) return;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(Typeface.FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            1.0);
        dc.DrawText(formatted, point);
    }

    private static Brush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static bool NearlyEqual(double left, double right)
        => double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) <= 0.25;

    private readonly record struct PlotTransform(Rect Plot, double CenterX, double CenterY, double Scale, double HalfRange)
    {
        internal bool Valid => Plot.Width > 0 && Plot.Height > 0 && Scale > 0 && double.IsFinite(Scale);
        internal Point Map(double r, double x) => new(CenterX + r * Scale, CenterY - x * Scale);
    }
}
