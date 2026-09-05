using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// FAT LIVE VALUE must be a presentation of the shared Engineering live objects, never a
/// second cached process image. Evidence state (Value 1 / Value 2, transition history and
/// journal records) intentionally stays in IoTestPointRuntime and is not changed here.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool FatLiveValueAuthorityClassHandlerRegistered =
        RegisterFatLiveValueAuthorityClassHandler();

    private static bool RegisterFatLiveValueAuthorityClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(FatLiveValueAuthorityLoaded));
        return true;
    }

    private static void FatLiveValueAuthorityLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window)
            return;

        // FatV2 builds its columns during the same window lifecycle. Apply this after the
        // current dispatcher work so LIVE VALUE always replaces the final V2 template.
        window.Dispatcher.BeginInvoke(
            new Action(window.InstallAuthoritativeFatLiveValueColumn),
            DispatcherPriority.ContextIdle);
    }

    private void InstallAuthoritativeFatLiveValueColumn()
    {
        var grid = _fatSignalsGrid ?? FindVisualDescendant<DataGrid>(this);
        if (grid == null)
            return;

        // Recycling can display the previous row's text for one render frame while WPF is
        // moving a cell to a new DataContext. Standard virtualization keeps large FAT lists
        // virtualized but never reuses a realized row container for another test point.
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Standard);

        var liveColumn = grid.Columns
            .OfType<DataGridTemplateColumn>()
            .FirstOrDefault(column =>
                string.Equals(column.Header?.ToString(), "LIVE VALUE", StringComparison.OrdinalIgnoreCase));
        if (liveColumn == null)
            return;

        var cell = new FrameworkElementFactory(typeof(FatAuthoritativeLiveValueCell));
        liveColumn.CellTemplate = new DataTemplate { VisualTree = cell };
    }
}

/// <summary>
/// One event-driven FAT cell. It resolves the exact live monitor point already proven by
/// the FAT binding service and, for an operable control DataObject, may display the shared
/// Engineering ControlCurrentValue because that is the direct status image used by the
/// Command Panel. No MMS read is initiated by this cell and no FAT evidence is mutated.
/// </summary>
public sealed class FatAuthoritativeLiveValueCell : StackPanel
{
    private readonly TextBlock _valueText;
    private readonly TextBlock _qualityText;
    private IoTestPointPlan? _plan;
    private IoListTestingWindow? _fatWindow;
    private Iec61850MonitorDevice? _device;
    private Iec61850MonitorPoint? _livePoint;
    private SignalDefinition? _controlSignal;
    private bool _attached;

    public FatAuthoritativeLiveValueCell()
    {
        Orientation = Orientation.Vertical;

        _valueText = new TextBlock
        {
            Text = "—",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.0,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 60, 100)),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _qualityText = new TextBlock
        {
            Text = "Unknown",
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(105, 121, 143)),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        Children.Add(_valueText);
        Children.Add(_qualityText);

        Loaded += Cell_Loaded;
        Unloaded += Cell_Unloaded;
        DataContextChanged += Cell_DataContextChanged;
    }

    private void Cell_Loaded(object sender, RoutedEventArgs e)
    {
        _attached = true;
        _fatWindow = Window.GetWindow(this) as IoListTestingWindow;
        if (_fatWindow != null)
            _fatWindow.PropertyChanged += FatWindow_PropertyChanged;
        RebindAuthoritativeSources();
    }

    private void Cell_Unloaded(object sender, RoutedEventArgs e)
    {
        _attached = false;
        if (_fatWindow != null)
            _fatWindow.PropertyChanged -= FatWindow_PropertyChanged;
        DetachSources();
        _fatWindow = null;
        _plan = null;
    }

    private void Cell_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_attached)
            return;

        // Clear before rebinding so a previous row can never be painted under a new row.
        _valueText.Text = "—";
        _qualityText.Text = "Unknown";
        RebindAuthoritativeSources();
    }

    private void FatWindow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IoListTestingWindow.SelectedIed))
            RebindAuthoritativeSources();
    }

    private void RebindAuthoritativeSources()
    {
        DetachSources();
        _plan = DataContext as IoTestPointPlan;
        if (_plan == null || _fatWindow?.Owner is not MainWindow engineeringWindow)
        {
            Render();
            return;
        }

        _plan.PropertyChanged += Plan_PropertyChanged;
        _device = engineeringWindow.ResolveIoFatCommandDevice(_fatWindow.SelectedIed);
        if (_device == null)
        {
            Render();
            return;
        }

        if (_device.Points is INotifyCollectionChanged pointsChanged)
            pointsChanged.CollectionChanged += DevicePoints_CollectionChanged;
        if (_device.CommandSignals is INotifyCollectionChanged commandsChanged)
            commandsChanged.CollectionChanged += DeviceCommands_CollectionChanged;

        ResolveLiveSources();
        Render();
    }

    private void DetachSources()
    {
        if (_plan != null)
            _plan.PropertyChanged -= Plan_PropertyChanged;
        if (_livePoint != null)
            _livePoint.PropertyChanged -= LivePoint_PropertyChanged;
        if (_controlSignal != null)
            _controlSignal.PropertyChanged -= ControlSignal_PropertyChanged;
        if (_device?.Points is INotifyCollectionChanged pointsChanged)
            pointsChanged.CollectionChanged -= DevicePoints_CollectionChanged;
        if (_device?.CommandSignals is INotifyCollectionChanged commandsChanged)
            commandsChanged.CollectionChanged -= DeviceCommands_CollectionChanged;

        _livePoint = null;
        _controlSignal = null;
        _device = null;
    }

    private void Plan_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IoTestPointPlan.LiveSignalReference) or
            nameof(IoTestPointPlan.LiveBindingState))
        {
            RebindAuthoritativeSources();
        }
    }

    private void DevicePoints_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResolveLiveSources();
        Render();
    }

    private void DeviceCommands_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ResolveLiveSources();
        Render();
    }

    private void LivePoint_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Iec61850MonitorPoint.Value) or
            nameof(Iec61850MonitorPoint.Quality) or
            nameof(Iec61850MonitorPoint.DeviceTimestamp) or
            nameof(Iec61850MonitorPoint.SourceMode) or
            nameof(Iec61850MonitorPoint.Status))
        {
            Render();
        }
    }

    private void ControlSignal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SignalDefinition.ControlCurrentValue) or
            nameof(SignalDefinition.ControlModelText) or
            nameof(SignalDefinition.ControlSupportsOperate))
        {
            Render();
        }
    }

    private void ResolveLiveSources()
    {
        if (_plan == null || _device == null)
            return;

        if (_livePoint != null)
            _livePoint.PropertyChanged -= LivePoint_PropertyChanged;
        if (_controlSignal != null)
            _controlSignal.PropertyChanged -= ControlSignal_PropertyChanged;
        _livePoint = null;
        _controlSignal = null;

        var exactReferences = CandidateReferences(_plan)
            .Select(IoTestLiveBindingService.NormalizeReference)
            .Where(reference => reference.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_plan.LiveSignalReference))
        {
            var exactLive = IoTestLiveBindingService.NormalizeReference(_plan.LiveSignalReference);
            _livePoint = _device.Points.FirstOrDefault(point =>
                IoTestLiveBindingService.NormalizeReference(point.IecReference)
                    .Equals(exactLive, StringComparison.OrdinalIgnoreCase));
        }

        if (_livePoint == null)
        {
            var matches = _device.Points
                .Where(point => exactReferences.Contains(
                    IoTestLiveBindingService.NormalizeReference(point.IecReference)))
                .Take(2)
                .ToList();
            if (matches.Count == 1)
                _livePoint = matches[0];
        }

        var exactControls = _device.CommandSignals
            .Where(signal => ExactControlReferenceMatch(signal, exactReferences))
            .Take(2)
            .ToList();
        if (exactControls.Count == 1)
            _controlSignal = exactControls[0];

        if (_livePoint != null)
            _livePoint.PropertyChanged += LivePoint_PropertyChanged;
        if (_controlSignal != null)
            _controlSignal.PropertyChanged += ControlSignal_PropertyChanged;
    }

    private void Render()
    {
        if (!_attached)
            return;

        // For controllable DO rows, the same ControlCurrentValue shown in Engineering's
        // Command Panel is preferred when initialized. It comes from the shared control
        // backend; this cell does not trigger a read. All other rows display the exact
        // Iec61850MonitorPoint current image. Runtime.CurrentValue is last-resort display
        // fallback only and remains the separate FAT/evidence state machine image.
        var controlValue = _controlSignal?.ControlCurrentValue;
        var liveValue = _livePoint?.Value;
        var value = IsInitialized(controlValue)
            ? controlValue!
            : IsInitialized(liveValue)
                ? liveValue!
                : IsInitialized(_plan?.Runtime.CurrentValue)
                    ? _plan!.Runtime.CurrentValue
                    : "—";

        var liveQuality = _livePoint?.Quality;
        var quality = IsInitialized(liveQuality)
            ? liveQuality!
            : IsInitialized(_plan?.Runtime.CurrentQuality)
                ? _plan!.Runtime.CurrentQuality
                : "Unknown";

        _valueText.Text = value;
        _qualityText.Text = quality;
    }

    private static bool ExactControlReferenceMatch(
        SignalDefinition signal,
        IReadOnlySet<string> exactReferences)
    {
        if (!signal.IsControlSignal || !signal.ControlModelResolved || !signal.ControlSupportsOperate)
            return false;

        var objectReference = IoTestLiveBindingService.NormalizeReference(signal.ObjectReference);
        var displayReference = IoTestLiveBindingService.NormalizeReference(signal.DisplayReference);
        var statusReference = IoTestLiveBindingService.NormalizeReference(signal.ControlStatusReference);
        return exactReferences.Contains(objectReference) ||
               exactReferences.Contains(displayReference) ||
               exactReferences.Contains(statusReference);
    }

    private static IEnumerable<string> CandidateReferences(IoTestPointPlan point)
    {
        if (!string.IsNullOrWhiteSpace(point.LiveSignalReference)) yield return point.LiveSignalReference;
        if (!string.IsNullOrWhiteSpace(point.ReportIecReference)) yield return point.ReportIecReference;
        if (!string.IsNullOrWhiteSpace(point.ObjectReference)) yield return point.ObjectReference;
        if (!string.IsNullOrWhiteSpace(point.EventLogSearchReference)) yield return point.EventLogSearchReference;
        if (!string.IsNullOrWhiteSpace(point.SourceIecReference)) yield return point.SourceIecReference;
        if (!string.IsNullOrWhiteSpace(point.ReportDisplayReference)) yield return point.ReportDisplayReference;
    }

    private static bool IsInitialized(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > 0 &&
               text != "-" &&
               text != "—" &&
               !text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("Pending", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("Not connected", StringComparison.OrdinalIgnoreCase);
    }
}
