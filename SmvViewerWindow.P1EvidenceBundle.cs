using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class SmvViewerWindow
{
    private readonly SmvEvidenceBundleExporter _evidenceBundleExporter = new();
    private Button? _exportEvidenceButton;

    private void InitializeP1EvidenceBundle()
    {
        Loaded += (_, _) => InstallEvidenceBundleButton();
    }

    private void InstallEvidenceBundleButton()
    {
        if (_exportEvidenceButton is not null)
            return;

        var closeButton = FindButtonByContent(this, "Close");
        if (closeButton is null || VisualTreeHelper.GetParent(closeButton) is not Grid footerGrid)
            return;

        if (footerGrid.ColumnDefinitions.Count < 2)
            return;

        footerGrid.ColumnDefinitions.Insert(1, new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(closeButton, 2);

        _exportEvidenceButton = new Button
        {
            Content = "Export evidence bundle",
            Width = 160,
            Margin = new Thickness(12, 0, 0, 0),
            IsEnabled = _snapshot is not null,
            ToolTip = "Export PNG waveform, raw CSV, JSON manifest, diagnostics, SHA-256 checksums, and app/engine provenance."
        };
        if (TryFindResource("SoftButton") is Style style)
            _exportEvidenceButton.Style = style;
        _exportEvidenceButton.Click += ExportEvidenceBundle_Click;
        Grid.SetColumn(_exportEvidenceButton, 1);
        footerGrid.Children.Add(_exportEvidenceButton);

        SnapshotChannels.CollectionChanged += (_, _) => RefreshEvidenceBundleButton();
        RefreshEvidenceBundleButton();
    }

    private void RefreshEvidenceBundleButton()
    {
        if (_exportEvidenceButton is not null)
            _exportEvidenceButton.IsEnabled = _snapshot is not null && !CancelButton.IsEnabled;
    }

    private async void ExportEvidenceBundle_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = _snapshot;
        var selection = _p0CaptureSelection;
        if (snapshot is null || selection is null)
        {
            MessageBox.Show(this, "Capture and accept an SV snapshot before exporting evidence.", "SV Evidence Bundle", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var timestamp = DateTimeOffset.UtcNow;
        var safeStream = SanitizeFileName(string.IsNullOrWhiteSpace(snapshot.StreamId) ? $"appid-{snapshot.AppId:X4}" : snapshot.StreamId);
        var dialog = new SaveFileDialog
        {
            Title = "Export SV evidence bundle",
            Filter = "ARSAS SV evidence bundle (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"ARSAS-SV-{safeStream}-{timestamp:yyyyMMdd-HHmmss}Z.zip"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            _exportEvidenceButton!.IsEnabled = false;
            CaptureStatusText = "Exporting auditable SV evidence bundle…";
            StatusText = "ARSAS is writing waveform, raw samples, manifest, diagnostics, integrity hashes, and provenance without modifying the captured stream.";

            var provenance = ReadBuildProvenance();
            var result = await _evidenceBundleExporter.ExportAsync(new SmvEvidenceBundleRequest
            {
                OutputPath = dialog.FileName,
                Snapshot = snapshot,
                WaveformPng = RenderWaveformPng(),
                Selection = selection,
                ApplicationVersion = provenance.ApplicationVersion,
                ApplicationCommit = provenance.ApplicationCommit,
                EngineRepository = provenance.EngineRepository,
                EngineReference = provenance.EngineReference,
                EngineCommit = provenance.EngineCommit,
                ExportedAtUtc = timestamp
            });

            CaptureStatusText = "SV evidence bundle exported.";
            StatusText = $"Evidence bundle saved to {result.OutputPath}. Bundle SHA-256: {result.BundleSha256}.";
            MessageBox.Show(
                this,
                $"Evidence bundle exported successfully.\n\n{result.OutputPath}\n\nSHA-256\n{result.BundleSha256}",
                "SV Evidence Bundle",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            CaptureStatusText = "Evidence bundle export failed.";
            StatusText = $"SV evidence export failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "SV Evidence Bundle", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshEvidenceBundleButton();
        }
    }

    private byte[] RenderWaveformPng()
    {
        UpdateLayout();
        RenderWaveform();
        WaveformCanvas.UpdateLayout();

        var width = Math.Max(1, (int)Math.Ceiling(Math.Max(WaveformCanvas.ActualWidth, 760)));
        var height = Math.Max(1, (int)Math.Ceiling(Math.Max(WaveformCanvas.ActualHeight, WaveformCanvas.Height)));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);

        var background = new DrawingVisual();
        using (var context = background.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            var brush = new VisualBrush(WaveformCanvas)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };
            context.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }
        bitmap.Render(background);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BuildProvenance ReadBuildProvenance()
    {
        var assembly = typeof(SmvViewerWindow).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                   ?? assembly.GetName().Version?.ToString()
                                   ?? "unknown";
        var version = informationalVersion.Split('+')[0];
        var applicationCommit = informationalVersion.Contains('+')
            ? informationalVersion[(informationalVersion.IndexOf('+') + 1)..]
            : "not-embedded";

        using var stream = assembly.GetManifestResourceStream("ARSAS.ARIEC61850.lock.json")
                           ?? throw new InvalidOperationException("Embedded ARIEC61850 provenance lock was not found.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        return new BuildProvenance(
            version,
            applicationCommit,
            root.GetProperty("repository").GetString() ?? "unknown",
            root.GetProperty("ref").GetString() ?? "unknown",
            root.GetProperty("commit").GetString() ?? "unknown");
    }

    private static Button? FindButtonByContent(DependencyObject root, string content)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button && string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
                return button;
            var nested = FindButtonByContent(child, content);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "stream" : sanitized.Trim();
    }

    private sealed record BuildProvenance(
        string ApplicationVersion,
        string ApplicationCommit,
        string EngineRepository,
        string EngineReference,
        string EngineCommit);
}
