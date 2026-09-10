using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow : Window
{
    private const int ExactSignalFrameLimit = 500_000;
    private const int FullRecordAnalogBuckets = 4_096;
    private const int FullRecordDigitalTransitionCap = 100_000;
    private readonly ArdIrecNativeRecord _record;
    private readonly SemaphoreSlim _nativeGate = new(1, 1);
    private ComtradeSignalItem? _activeSignal;

    internal ComtradeWorkspaceWindow(ArdIrecNativeRecord record)
    {
        _record = record;
        InitializeComponent();
        ResetViewButton.IsEnabled = false;
        FullRecordButton.IsEnabled = false;
        PopulateHeader();
        PopulateSignals();
        Closed += ComtradeWorkspaceWindow_Closed;
    }

    private async void ComtradeWorkspaceWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            await _nativeGate.WaitAsync().ConfigureAwait(true);
            try
            {
                _record.Dispose();
            }
            finally
            {
                _nativeGate.Release();
            }
        }
        finally
        {
            _nativeGate.Dispose();
        }
    }

    private void PopulateHeader()
    {
        var info = _record.Info;
        var fileName = Path.GetFileNameWithoutExtension(_record.CfgPath);
        RecordIdentityText.Text = $"{fileName}  •  COMTRADE {info.RevisionYear}  •  {FormatDataFormat(info.DataFormat)}  •  {info.StartTime}";
        StationText.Text = string.IsNullOrWhiteSpace(info.RecorderId)
            ? info.StationName
            : $"{info.StationName}  •  {info.RecorderId}";
        ChannelsText.Text = $"{info.AnalogCount} analog  •  {info.StatusCount} digital";
        FrequencyText.Text = info.NominalFrequency > 0 ? $"{info.NominalFrequency:G5} Hz" : "—";
        FramesText.Text = $"{info.FrameCount:N0}";
        StatusTextBlock.Text = $"Loaded natively from {Path.GetFileName(_record.CfgPath)} • trigger {info.TriggerTime}";
    }

    private void PopulateSignals()
    {
        var signals = new List<ComtradeSignalItem>(_record.AnalogChannels.Count + _record.StatusChannels.Count);
        for (var i = 0; i < _record.AnalogChannels.Count; i++)
        {
            var channel = _record.AnalogChannels[i];
            _record.TryReadAnalogSemantics(checked((uint)i), out var semantics);
            var section = ResolveAnalogSection(channel.Units, semantics);
            var sectionOrder = section switch
            {
                "Voltage" => 0,
                "Current" => 1,
                _ => 2
            };
            var phaseOrder = ResolvePhaseOrder(channel.Phase, channel.Id, semantics);
            var context = string.Join(" • ", new[] { channel.Phase, channel.Circuit, channel.Units }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            signals.Add(new ComtradeSignalItem(
                true,
                checked((uint)i),
                channel.Id,
                context,
                FrozenSignalBrush(ResolveSignalColor(channel.Phase, channel.Id, false)),
                section,
                sectionOrder,
                phaseOrder));
        }

        for (var i = 0; i < _record.StatusChannels.Count; i++)
        {
            var channel = _record.StatusChannels[i];
            var context = string.Join(" • ", new[] { channel.Phase, channel.Circuit, $"normal {channel.NormalState}" }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            signals.Add(new ComtradeSignalItem(
                false,
                checked((uint)i),
                channel.Id,
                context,
                FrozenSignalBrush(ResolveSignalColor(channel.Phase, channel.Id, true)),
                "Digital Events",
                3,
                ResolvePhaseOrder(channel.Phase, channel.Id, null)));
        }

        var ordered = signals
            .OrderBy(item => item.SectionOrder)
            .ThenBy(item => item.PhaseOrder)
            .ThenBy(item => item.Index)
            .ToList();
        SignalList.ItemsSource = ordered;
        var view = CollectionViewSource.GetDefaultView(ordered);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ComtradeSignalItem.Section)));

        SignalCountText.Text = $"{signals.Count} total";
        if (signals.Count > 0)
            SignalList.SelectedIndex = 0;
        else
        {
            NavigationTextBlock.Text = "No COMTRADE channels.";
            ResetViewButton.IsEnabled = false;
            FullRecordButton.IsEnabled = false;
            WaveformView.ShowMessage("No channels", "The COMTRADE record contains no analog or digital channels.");
        }
    }

    private static string ResolveAnalogSection(string units, ComtradeAnalogSemantics? semantics)
    {
        if (semantics is not null)
        {
            return semantics.Role switch
            {
                1 => "Voltage",
                2 => "Current",
                _ => "Other Analog"
            };
        }

        var normalized = (units ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalized is "V" or "KV" or "MV" or "UV") return "Voltage";
        if (normalized is "A" or "KA" or "MA" or "UA") return "Current";
        return "Other Analog";
    }

    private static int ResolvePhaseOrder(string phase, string title, ComtradeAnalogSemantics? semantics)
    {
        if (semantics is not null)
        {
            return semantics.PhaseRole switch
            {
                1 => 0,
                2 => 1,
                3 => 2,
                4 => 3,
                _ => 4
            };
        }

        return NormalizePhase(phase, title) switch
        {
            "L1" => 0,
            "L2" => 1,
            "L3" => 2,
            "N" or "E" => 3,
            _ => 4
        };
    }

    private static Brush FrozenSignalBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void SignalList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SignalList.SelectedItem is ComtradeSignalItem signal)
            _activeSignal = signal;
    }

    private static long ToSignedDelta(ulong frameCount, double fraction)
    {
        if (!double.IsFinite(fraction) || fraction == 0 || frameCount == 0)
            return 0;
        var magnitude = Math.Abs(fraction) * frameCount;
        if (!double.IsFinite(magnitude) || magnitude >= long.MaxValue)
            return fraction < 0 ? long.MinValue + 1 : long.MaxValue;
        var rounded = checked((long)Math.Round(magnitude, MidpointRounding.AwayFromZero));
        return fraction < 0 ? -rounded : rounded;
    }

    private async void FullAnalysis_Click(object sender, RoutedEventArgs e)
    {
        var cfgPath = _record.CfgPath;
        FullAnalysisButton.IsEnabled = false;
        var originalContent = FullAnalysisButton.Content;
        FullAnalysisButton.Content = "Opening…";
        StatusTextBlock.Text = "Opening the complete COMTRADE analysis workspace…";

        try
        {
            if (!ArdIrecViewerLauncher.TryLaunch(cfgPath, out var process, out var launchError) || process is null)
            {
                StatusTextBlock.Text = "Full analysis could not be started.";
                MessageBox.Show(
                    this,
                    launchError,
                    "COMTRADE full analysis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            using (process)
            {
                var activated = false;
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    await Task.Delay(attempt == 0 ? 350 : 220).ConfigureAwait(true);
                    process.Refresh();

                    if (process.HasExited)
                    {
                        var earlyExitError = ArdIrecViewerLauncher.DescribeEarlyExit(process, cfgPath);
                        StatusTextBlock.Text = "Full analysis closed during startup.";
                        MessageBox.Show(
                            this,
                            earlyExitError,
                            "COMTRADE full analysis startup failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    if (!activated)
                        activated = ArdIrecViewerLauncher.TryActivateViewerWindow(process);
                }
            }

            StatusTextBlock.Text = "Full COMTRADE analysis opened • native workspace remains available.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusTextBlock.Text = "Full analysis startup failed.";
            MessageBox.Show(
                this,
                ex.Message,
                "COMTRADE full analysis startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            FullAnalysisButton.Content = originalContent;
            FullAnalysisButton.IsEnabled = true;
        }
    }

    private static string FormatDataFormat(int format) => format switch
    {
        0 => "ASCII",
        1 => "BINARY",
        2 => "BINARY32",
        3 => "FLOAT32",
        _ => "UNKNOWN"
    };

    private sealed record ComtradeSignalItem(
        bool IsAnalog,
        uint Index,
        string Title,
        string Subtitle,
        Brush Accent,
        string Section,
        int SectionOrder,
        int PhaseOrder);
}
