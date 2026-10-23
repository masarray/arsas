using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class RcbExportFilterWindow : Window
{
    private readonly RcbExportFilterViewModel _viewModel;
    private readonly DispatcherTimer _successOverlayTimer;
    private bool _selectionUpdateInProgress;
    private CancellationTokenSource? _activeOperation;
    private Border? _availabilityBusyOverlay;
    private Border? _successOverlay;
    private TextBlock? _successOverlayTitle;
    private TextBlock? _successOverlayDetail;

    public RcbExportFilterWindow(RcbExportWindowOptions options)
    {
        InitializeComponent();
        _viewModel = new RcbExportFilterViewModel(options);
        DataContext = _viewModel;
        _successOverlayTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _successOverlayTimer.Tick += (_, _) => HideSuccessOverlay();
    }

    public RcbExportFilterWindow(string iedName, string endpoint)
        : this(BuildMockOptions(iedName, endpoint))
    {
    }

    protected override void OnClosed(EventArgs e)
    {
        _successOverlayTimer.Stop();
        _activeOperation?.Cancel();
        _activeOperation?.Dispose();
        base.OnClosed(e);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureTransientOverlays();
        var initial = FirstPreferredRow();
        _viewModel.SelectOnly(initial);
        RcbGrid.SelectedItem = initial;
        CheckAvailabilityButton.IsEnabled = _viewModel.Options.IsMock || _viewModel.Options.RefreshAvailabilityAsync != null;
        MockStatusText.Text = initial == null
            ? "No selectable populated RCB is currently available."
            : $"{initial.Name} selected • {initial.DataSetName} • {initial.MemberCount:N0} FCDA.";
        RefreshSelectionUi();

        // Production workflow is deliberately eager: opening the window starts a read-only
        // availability audit immediately. The awaited network work stays off the UI thread in
        // the probe service while this window presents an animated non-blocking wait state.
        if (!_viewModel.Options.IsMock && _viewModel.Options.RefreshAvailabilityAsync != null)
            await RunAvailabilityCheckAsync(automatic: true);
    }

    private void RcbCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_selectionUpdateInProgress || sender is not CheckBox { DataContext: RcbExportRow row })
            return;

        _selectionUpdateInProgress = true;
        try
        {
            _viewModel.SelectOnly(row);
            RcbGrid.SelectedItem = row;
        }
        finally
        {
            _selectionUpdateInProgress = false;
        }

        MockStatusText.Text = $"{row.Name} selected • {row.DataSetName} • {row.MemberCount:N0} FCDA.";
        RefreshSelectionUi();
    }

    private void RcbCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_selectionUpdateInProgress || sender is not CheckBox { DataContext: RcbExportRow row })
            return;

        if (ReferenceEquals(_viewModel.SelectedRow, row))
            _viewModel.SelectedRow = null;
        RefreshSelectionUi();
    }

    private async void CheckAvailability_Click(object sender, RoutedEventArgs e)
        => await RunAvailabilityCheckAsync(automatic: false);

    private async Task RunAvailabilityCheckAsync(bool automatic)
    {
        if (_activeOperation != null)
            return;

        CheckAvailabilityButton.IsEnabled = false;
        CheckAvailabilityText.Text = "Checking…";
        _viewModel.AvailabilityCheckedText = "Reading RptEna, reservation, Owner, and DataSet directory…";
        MockStatusText.Text = automatic
            ? "Checking RCB availability automatically — read-only, no reservation or write."
            : "Read-only availability check in progress — no RCB will be reserved or modified.";
        SetAvailabilityBusyState(true);
        _activeOperation = new CancellationTokenSource(TimeSpan.FromSeconds(35));

        try
        {
            if (_viewModel.Options.IsMock)
            {
                await Task.Delay(650, _activeOperation.Token);
            }
            else
            {
                var refresh = _viewModel.Options.RefreshAvailabilityAsync
                    ?? throw new InvalidOperationException("Connect the IED before checking live RCB availability.");
                var rows = await refresh(_activeOperation.Token);
                _viewModel.ReplaceRows(rows);

                var preferred = FirstPreferredRow();
                _viewModel.SelectOnly(preferred);
                RcbGrid.SelectedItem = preferred;
                if (preferred != null)
                    RcbGrid.ScrollIntoView(preferred);
            }

            _viewModel.AvailabilityCheckedText = $"Checked {DateTime.Now:HH:mm:ss} • read-only";
            MockStatusText.Text = "Availability ready. Green is proven free; red is occupied/unusable; yellow requires confirmation.";
        }
        catch (OperationCanceledException)
        {
            _viewModel.AvailabilityCheckedText = "Availability check cancelled or timed out";
            MockStatusText.Text = "No RCB was modified. Retry after confirming MMS connectivity.";
        }
        catch (Exception ex)
        {
            _viewModel.AvailabilityCheckedText = "Availability check failed • no RCB modified";
            MockStatusText.Text = ex.Message;
        }
        finally
        {
            _activeOperation.Dispose();
            _activeOperation = null;
            SetAvailabilityBusyState(false);
            CheckAvailabilityText.Text = "Check Availability";
            CheckAvailabilityButton.IsEnabled = _viewModel.Options.IsMock || _viewModel.Options.RefreshAvailabilityAsync != null;
            RefreshSelectionUi();
        }
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _selectionUpdateInProgress = true;
        try
        {
            _viewModel.ClearSelection();
            RcbGrid.SelectedItem = null;
        }
        finally
        {
            _selectionUpdateInProgress = false;
        }

        MockStatusText.Text = "All RCBs cleared. Select exactly one populated RCB.";
        RefreshSelectionUi();
    }

    private void SelectAvailable_Click(object sender, RoutedEventArgs e)
    {
        var row = FirstPreferredRow();
        if (row == null)
        {
            MockStatusText.Text = "No selectable populated RCB is available.";
            return;
        }

        _selectionUpdateInProgress = true;
        try
        {
            _viewModel.SelectOnly(row);
            RcbGrid.SelectedItem = row;
            RcbGrid.ScrollIntoView(row);
        }
        finally
        {
            _selectionUpdateInProgress = false;
        }

        MockStatusText.Text = $"{row.Name} selected. Export will retain this RCB only.";
        RefreshSelectionUi();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_activeOperation != null)
            return;

        var selected = _viewModel.SelectedRow;
        if (selected?.IsSelectable != true)
        {
            MockStatusText.Text = "Select exactly one RCB with a populated DataSet before export.";
            RefreshSelectionUi();
            return;
        }

        if (selected.RequiresConfirmation)
        {
            var warning = selected.Availability == MmsRcbOperationalAvailability.UsedByCaller
                ? "This RCB is active in the current ARSAS session. The CID can be generated, but stop ARSAS reporting before the target SAS tries to reserve or enable this RCB."
                : "Live availability could not be proven from the attributes exposed by this IED. The CID can still be generated, but verify the RCB is not used by another client before importing it.";
            if (MessageBox.Show(this, warning + "\n\nContinue with export?", "Confirm RCB Selection",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
        }

        var editionDialog = new SaveSclWindow(
            _viewModel.IedName,
            $"Legacy SAS filter • {selected.Name} • {selected.DataSetName}",
            SclSchemaProfile.Edition1V16)
        {
            Owner = this
        };
        if (editionDialog.ShowDialog() != true)
            return;

        var schema = editionDialog.ViewModel.SelectedSchemaProfile;
        if (_viewModel.Options.IsMock || _viewModel.Options.ExportAsync == null)
        {
            MockStatusText.Text = $"UX mock: {selected.Name} prepared for {schema.DisplayName}. Production engine export is disabled in demo mode.";
            return;
        }

        var editionSuffix = schema.IsEdition2 ? "ed2" : "ed1";
        var fileDialog = new SaveFileDialog
        {
            Title = $"Export legacy SAS CID — {selected.Name} — {schema.DisplayName}",
            Filter = "Configured IED Description (*.cid)|*.cid|All files (*.*)|*.*",
            DefaultExt = ".cid",
            AddExtension = true,
            FileName = $"{SafeFileStem(_viewModel.IedName)}-legacy-sas-{SafeFileStem(selected.Name)}-{editionSuffix}.cid"
        };
        if (fileDialog.ShowDialog(this) != true)
            return;

        _activeOperation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        SetBusyState(true, "Filtering SCL and validating one retained RCB…");
        try
        {
            var completion = await _viewModel.Options.ExportAsync(
                selected,
                schema.Profile,
                fileDialog.FileName,
                _activeOperation.Token);

            MockStatusText.Text = completion.Message;
            ShowSuccessOverlay(completion);
        }
        catch (OperationCanceledException)
        {
            MockStatusText.Text = "Export cancelled or timed out. The source SCL was not modified.";
        }
        catch (Exception ex)
        {
            MockStatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "RCB Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _activeOperation.Dispose();
            _activeOperation = null;
            SetBusyState(false, string.Empty);
            RefreshSelectionUi();
        }
    }

    private RcbExportRow? FirstPreferredRow()
        => _viewModel.Rows.FirstOrDefault(row => row.IsSelectable && row.Availability == MmsRcbOperationalAvailability.Available)
           ?? _viewModel.Rows.FirstOrDefault(row => row.IsSelectable);

    private void SetBusyState(bool busy, string status)
    {
        ExportButton.IsEnabled = !busy && _viewModel.CanExport;
        CheckAvailabilityButton.IsEnabled = !busy && (_viewModel.Options.IsMock || _viewModel.Options.RefreshAvailabilityAsync != null);
        RcbGrid.IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(status))
            MockStatusText.Text = status;
    }

    private void SetAvailabilityBusyState(bool busy)
    {
        if (_availabilityBusyOverlay != null)
            _availabilityBusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RcbGrid.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy && _viewModel.CanExport;
    }

    private void RefreshSelectionUi()
    {
        ExportButton.IsEnabled = _activeOperation == null && _viewModel.CanExport;
    }

    private void EnsureTransientOverlays()
    {
        if (Content is not Grid root || _availabilityBusyOverlay != null)
            return;

        _availabilityBusyOverlay = BuildAvailabilityOverlay();
        Grid.SetRowSpan(_availabilityBusyOverlay, Math.Max(1, root.RowDefinitions.Count));
        Panel.SetZIndex(_availabilityBusyOverlay, 100);
        root.Children.Add(_availabilityBusyOverlay);

        _successOverlay = BuildSuccessOverlay();
        Grid.SetRowSpan(_successOverlay, Math.Max(1, root.RowDefinitions.Count));
        Panel.SetZIndex(_successOverlay, 110);
        root.Children.Add(_successOverlay);
    }

    private Border BuildAvailabilityOverlay()
    {
        var title = new TextBlock
        {
            Text = "Checking RCB availability",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(32, 48, 74)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var detail = new TextBlock
        {
            Text = "Reading RptEna, reservation, Owner and DataSet directory\nRead-only — no RCB will be reserved or modified",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 117, 139)),
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 7, 0, 14)
        };
        var progress = new ProgressBar
        {
            Width = 250,
            Height = 6,
            IsIndeterminate = true,
            Foreground = new SolidColorBrush(Color.FromRgb(49, 93, 191)),
            Background = new SolidColorBrush(Color.FromRgb(225, 233, 246))
        };
        var card = new Border
        {
            Width = 390,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 220, 248)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(24, 21, 24, 21),
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 5,
                Opacity = 0.16,
                Color = Color.FromRgb(30, 51, 85)
            },
            Child = new StackPanel
            {
                Children = { title, detail, progress }
            }
        };
        return new Border
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(190, 244, 248, 253)),
            CornerRadius = new CornerRadius(18),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new Grid
            {
                Children = { card }
            }
        };
    }

    private Border BuildSuccessOverlay()
    {
        _successOverlayTitle = new TextBlock
        {
            Text = "CID exported successfully",
            FontSize = 14.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(20, 104, 57))
        };
        _successOverlayDetail = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 10.8,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 93, 113)),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 330
        };
        var check = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(Color.FromRgb(226, 247, 234)),
            Child = new TextBlock
            {
                Text = "✓",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(check);
        var text = new StackPanel { Children = { _successOverlayTitle, _successOverlayDetail } };
        Grid.SetColumn(text, 2);
        content.Children.Add(text);

        return new Border
        {
            Visibility = Visibility.Collapsed,
            Opacity = 0,
            Width = 410,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 10, 10, 0),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(169, 224, 188)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(15, 13, 15, 13),
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 4,
                Opacity = 0.17,
                Color = Color.FromRgb(30, 70, 48)
            },
            Child = content
        };
    }

    private void ShowSuccessOverlay(RcbExportCompletion completion)
    {
        EnsureTransientOverlays();
        if (_successOverlay == null || _successOverlayTitle == null || _successOverlayDetail == null)
            return;

        _successOverlayTitle.Text = "Legacy SAS CID exported";
        _successOverlayDetail.Text =
            $"{Path.GetFileName(completion.OutputPath)}  •  {completion.SchemaDisplayName}\n" +
            $"1 RCB retained  •  {completion.DataSetMemberCount:N0} FCDA  •  {completion.RemovedReportControlCount:N0} removed";
        _successOverlay.Visibility = Visibility.Visible;
        _successOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        _successOverlayTimer.Stop();
        _successOverlayTimer.Start();
    }

    private void HideSuccessOverlay()
    {
        _successOverlayTimer.Stop();
        if (_successOverlay == null || _successOverlay.Visibility != Visibility.Visible)
            return;

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (_successOverlay != null)
                _successOverlay.Visibility = Visibility.Collapsed;
        };
        _successOverlay.BeginAnimation(OpacityProperty, fade);
    }

    private static string SafeFileStem(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string((value ?? "IED")
            .Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "IED" : cleaned;
    }

    private static RcbExportWindowOptions BuildMockOptions(string iedName, string endpoint)
    {
        var name = string.IsNullOrWhiteSpace(iedName) ? "IED" : iedName.Trim();
        var rows = new[]
        {
            MockRow(name, "A_BRCB01", true, "dsTripEvents", "Static DataSet • protection events", 128, MmsRcbOperationalAvailability.Available),
            MockRow(name, "A_URCB01", false, "dsBayStatus", "Static DataSet • status indications", 84, MmsRcbOperationalAvailability.Available),
            MockRow(name, "A_BRCB02", true, "dsProtection", "Static DataSet • protection start/trip", 96, MmsRcbOperationalAvailability.InUse),
            MockRow(name, "A_BRCB03", true, "dsMeasurements", "Static DataSet • analog measurements", 64, MmsRcbOperationalAvailability.InUse),
            MockRow(name, "A_URCB02", false, "—", "Empty DataSet", 0, MmsRcbOperationalAvailability.DataSetEmpty)
        };
        return new RcbExportWindowOptions
        {
            IedName = name,
            Endpoint = endpoint,
            IsMock = true,
            CanCheckAvailability = true,
            Rows = rows
        };
    }

    private static RcbExportRow MockRow(
        string iedName,
        string rcbName,
        bool buffered,
        string dataSet,
        string detail,
        int memberCount,
        MmsRcbOperationalAvailability availability)
        => new()
        {
            Name = rcbName,
            ExportName = rcbName,
            Reference = $"{iedName}LD0/LLN0.{(buffered ? "BR" : "RP")}.{rcbName}",
            Type = buffered ? "Buffered" : "Unbuffered",
            Buffered = buffered,
            DataSetName = dataSet,
            DataSetReference = dataSet == "—" ? string.Empty : $"{iedName}LD0/LLN0.{dataSet}",
            DataSetDetail = detail,
            MemberCount = memberCount,
            Availability = availability,
            Confidence = MmsRcbAvailabilityConfidence.Exact,
            StatusText = RcbExportRow.ToStatusText(availability),
            Reason = availability == MmsRcbOperationalAvailability.Available
                ? "Mock RptEna=false and reservation state free."
                : availability == MmsRcbOperationalAvailability.InUse
                    ? "Mock RptEna=true or reservation is held by another client."
                    : "Mock DataSet is empty."
        };
}
