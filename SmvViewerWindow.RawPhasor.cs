using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class SmvViewerWindow
{
    // Only the current coherent snapshot can publish a waveform, phasor or readout.
    private int _rawVectorAnalysisGeneration;
    private string _rawVectorStatusText =
        "Waiting for a complete, continuous capture. No nominal vector is fabricated.";

    public ObservableCollection<SmvRawVectorMeasurement> RawVectorRows { get; } = new();

    public string RawVectorStatusText
    {
        get => _rawVectorStatusText;
        private set => Set(ref _rawVectorStatusText, value);
    }

    private async void BeginRawVectorAnalysis(SmvSnapshotResult snapshot)
    {
        var generation = Interlocked.Increment(ref _rawVectorAnalysisGeneration);
        RawVectorRows.Clear();
        RawVectorStatusText = "Analyzing raw received channels…";
        RenderRawPhasor();
        try
        {
            var vectors = await Task.Run(() => SmvRawVectorMath.Analyze(snapshot));
            if (!IsCurrentRawVectorOwner(snapshot, generation))
                return;
            foreach (var vector in vectors)
                RawVectorRows.Add(vector);

            RawVectorStatusText = vectors.Count == 0
                ? "No coherent phasor: capture incomplete, discontinuous or malformed."
                : "Raw INT32 fundamental RMS / relative phase; NOT V or A. No trusted ordered SCL scaling.";
            RenderRawPhasor();
        }
        catch (Exception ex)
        {
            if (!IsCurrentRawVectorOwner(snapshot, generation))
                return;
            RawVectorRows.Clear();
            RawVectorStatusText = $"Raw phasor analysis unavailable: {ex.Message}";
            RenderRawPhasor();
        }
    }

    private bool IsCurrentRawVectorOwner(SmvSnapshotResult snapshot, int generation)
    {
        if (generation != _rawVectorAnalysisGeneration ||
            !ReferenceEquals(snapshot, _snapshot) || !IsLoaded)
            return false;

        // P0 capture-selection identity remains the authority, even if analysis
        // finishes before its scheduled evidence assessment.
        return _p0CaptureSelection is null ||
            (SelectedStream is not null && _p0CaptureSelection.Matches(
                SelectedStream.ControlReference,
                SelectedStream.StreamId,
                SelectedStream.DataSetReference,
                SelectedStream.AppId,
                SelectedStream.DestinationMac));
    }

    private void ResetRawVectorPresentation()
    {
        Interlocked.Increment(ref _rawVectorAnalysisGeneration);
        RawVectorRows.Clear();
        RawVectorStatusText = "Waiting for complete SV samples; no synthetic vector is displayed.";
        if (RawPhasorCanvas is not null && IsLoaded)
            RenderRawPhasor();
    }

    private void RawPhasorCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => RenderRawPhasor();

    private void RenderRawPhasor()
    {
        if (RawPhasorCanvas is null)
            return;
        RawPhasorCanvas.Children.Clear();
        var width = RawPhasorCanvas.ActualWidth;
        var height = RawPhasorCanvas.ActualHeight;
        if (width < 130 || height < 130)
            return;

        var center = new Point(width / 2, height / 2 + 3);
        var radius = Math.Max(30, Math.Min(width - 70, height - 55) / 2);
        foreach (var scale in new[] { 0.25, 0.5, 0.75, 1.0 })
        {
            var ellipse = new Ellipse
            {
                Width = radius * 2 * scale,
                Height = radius * 2 * scale,
                Stroke = scale == 1 ? Brushes.SteelBlue : Brushes.LightSteelBlue,
                StrokeThickness = scale == 1 ? 1.4 : 0.7,
                StrokeDashArray = scale == 1 ? null : new DoubleCollection { 3, 4 }
            };
            Canvas.SetLeft(ellipse, center.X - radius * scale);
            Canvas.SetTop(ellipse, center.Y - radius * scale);
            RawPhasorCanvas.Children.Add(ellipse);
        }

        RawPhasorCanvas.Children.Add(new Line
        {
            X1 = center.X - radius - 12, X2 = center.X + radius + 12,
            Y1 = center.Y, Y2 = center.Y,
            Stroke = Brushes.LightSteelBlue, StrokeThickness = 1
        });
        RawPhasorCanvas.Children.Add(new Line
        {
            X1 = center.X, X2 = center.X,
            Y1 = center.Y - radius - 10, Y2 = center.Y + radius + 10,
            Stroke = Brushes.LightSteelBlue, StrokeThickness = 1
        });

        var vectors = RawVectorRows.Where(vector => vector.HasFundamental).ToArray();
        if (vectors.Length == 0)
        {
            AddRawPhasorText("No coherent phasor", center.X - 66, center.Y - 12, Brushes.SlateGray);
            return;
        }

        var max = vectors.Max(vector => vector.FundamentalRms);
        if (max <= 0)
            return;
        foreach (var vector in vectors)
        {
            var radians = vector.PhaseDegrees * Math.PI / 180.0;
            var distance = radius * Math.Clamp(vector.FundamentalRms / max, 0, 1);
            var tip = new Point(
                center.X + distance * Math.Cos(radians),
                center.Y - distance * Math.Sin(radians));
            var direction = tip - center;
            if (direction.Length < 2)
                continue;

            var brush = WaveformBrushes[vector.ChannelIndex % WaveformBrushes.Length];
            RawPhasorCanvas.Children.Add(new Line
            {
                X1 = center.X, Y1 = center.Y, X2 = tip.X, Y2 = tip.Y,
                Stroke = brush, StrokeThickness = 2.2
            });
            direction.Normalize();
            var normal = new Vector(-direction.Y, direction.X);
            var p1 = tip - direction * 10 + normal * 4;
            var p2 = tip - direction * 10 - normal * 4;
            RawPhasorCanvas.Children.Add(new Line
            {
                X1 = tip.X, Y1 = tip.Y, X2 = p1.X, Y2 = p1.Y,
                Stroke = brush, StrokeThickness = 2
            });
            RawPhasorCanvas.Children.Add(new Line
            {
                X1 = tip.X, Y1 = tip.Y, X2 = p2.X, Y2 = p2.Y,
                Stroke = brush, StrokeThickness = 2
            });
            AddRawPhasorText(vector.Label, tip.X + 5, tip.Y - 14, brush);
        }

        var origin = new Ellipse { Width = 6, Height = 6, Fill = Brushes.DodgerBlue };
        Canvas.SetLeft(origin, center.X - 3);
        Canvas.SetTop(origin, center.Y - 3);
        RawPhasorCanvas.Children.Add(origin);
    }

    private void AddRawPhasorText(string text, double left, double top, Brush brush)
    {
        var label = new TextBlock
        {
            Text = text, Foreground = brush, FontSize = 10.0,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 78
        };
        Canvas.SetLeft(label, Math.Clamp(left, 0, Math.Max(0, RawPhasorCanvas.ActualWidth - 78)));
        Canvas.SetTop(label, Math.Clamp(top, 0, Math.Max(0, RawPhasorCanvas.ActualHeight - 16)));
        RawPhasorCanvas.Children.Add(label);
    }
}
