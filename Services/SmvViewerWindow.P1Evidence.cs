using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArIED61850Tester.Services;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class SmvViewerWindow
{
    private bool _isEvidenceExportBusy;
    private string _evidenceExportStatusText =
        "Capture a snapshot to enable the portable engineering evidence bundle.";

    public bool IsEvidenceExportBusy
    {
        get => _isEvidenceExportBusy;
        private set
        {
            if (_isEvidenceExportBusy == value)
                return;
            _isEvidenceExportBusy = value;
            Raise();
        }
    }

    public string EvidenceExportStatusText
    {
        get => _evidenceExportStatusText;
        private set
        {
            if (_evidenceExportStatusText == value)
                return;
            _evidenceExportStatusText = value;
            Raise();
        }
    }

    private async void ExportEvidence_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            MessageBox.Show(
                this,
                "Capture and accept one bounded SV snapshot before exporting evidence.",
                "SV Evidence Bundle",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var stream = SelectedStream;
        var context = new SmvSnapshotEvidenceContext
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            DeviceName = DeviceName,
            EndpointText = EndpointText,
            AdapterDisplayText = SelectedAdapter?.DisplayText ?? "Unrecorded adapter",
            ControlReference = stream?.ControlReference ?? string.Empty,
            SelectedStreamId = stream?.StreamId ?? snapshot.StreamId,
            SelectedDataSetReference = stream?.DataSetReference ?? snapshot.DataSetReference,
            SelectedAppId = stream?.AppId ?? $"0x{snapshot.AppId:X4}",
            SelectedDestinationMac = stream?.DestinationMac ?? snapshot.DestinationMac,
            ExplicitNominalFrequencyHz = ReadSelectedFrequency(),
            Provenance = SmvSnapshotEvidenceProvenance.LoadCurrent()
        };

        var dialog = new SaveFileDialog
        {
            Title = "Export ARSAS SV evidence bundle",
            Filter = "ARSAS SV evidence bundle (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = SmvSnapshotEvidenceExporter.BuildSuggestedFileName(context, snapshot)
        };
        if (dialog.ShowDialog(this) != true)
            return;

        IsEvidenceExportBusy = true;
        ExportEvidenceButton.IsEnabled = false;
        EvidenceExportStatusText = "Rendering waveform and building evidence package…";

        try
        {
            await Dispatcher.InvokeAsync(RenderWaveform);
            var waveformPng = RenderWaveformPng();
            var result = await SmvSnapshotEvidenceExporter.ExportAsync(
                dialog.FileName,
                snapshot,
                context,
                waveformPng);

            EvidenceExportStatusText =
                $"Evidence exported: {Path.GetFileName(result.BundlePath)} · SHA-256 {result.BundleSha256[..12]}…";
            StatusText =
                $"SV evidence bundle exported with {result.Entries.Count:N0} auditable entries. The raw-value and calibration boundaries remain explicit.";

            MessageBox.Show(
                this,
                $"Evidence bundle created successfully.\n\n{result.BundlePath}\n\nSHA-256\n{result.BundleSha256}",
                "SV Evidence Bundle",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            EvidenceExportStatusText = $"Evidence export failed: {ex.Message}";
            StatusText = "No partial evidence package was accepted. Choose a writable destination and retry.";
            MessageBox.Show(
                this,
                ex.Message,
                "SV Evidence Export Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            IsEvidenceExportBusy = false;
            ExportEvidenceButton.IsEnabled = _snapshot is not null && !CancelButton.IsEnabled;
        }
    }

    private byte[] RenderWaveformPng()
    {
        WaveformCanvas.UpdateLayout();
        var renderWidth = WaveformCanvas.ActualWidth > 1 ? WaveformCanvas.ActualWidth : 1000;
        var configuredHeight = double.IsNaN(WaveformCanvas.Height) ? 390 : WaveformCanvas.Height;
        var renderHeight = WaveformCanvas.ActualHeight > 1 ? WaveformCanvas.ActualHeight : configuredHeight;
        var width = Math.Max(1, (int)Math.Ceiling(renderWidth));
        var height = Math.Max(1, (int)Math.Ceiling(renderHeight));
        var bounds = new Rect(0, 0, width, height);

        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, bounds);
            drawing.DrawRectangle(new VisualBrush(WaveformCanvas) { Stretch = Stretch.Fill }, null, bounds);
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}