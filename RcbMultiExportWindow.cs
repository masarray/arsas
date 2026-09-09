using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;
using Microsoft.Win32;

namespace ArIED61850Tester;

/// <summary>
/// Multi-select RCB export surface. Selection is intentionally independent from live RCB
/// ownership: availability is engineering evidence, never a lock that hides an RCB from a
/// generic SCL export.
/// </summary>
public sealed class RcbMultiExportWindow : Window
{
    private readonly string _iedName;
    private readonly string _endpoint;
    private readonly Func<CancellationToken, Task<IReadOnlyList<RcbExportRow>>>? _refreshAvailability;
    private readonly Func<IReadOnlyList<RcbExportRow>, SclSchemaProfile, string, CancellationToken, Task<RcbExportCompletion>> _export;
    private readonly ObservableCollection<RcbExportRow> _rows = new();
    private readonly DataGrid _grid;
    private readonly TextBlock _status;
    private readonly TextBlock _summary;
    private readonly Button _checkButton;
    private readonly Button _exportButton;
    private CancellationTokenSource? _operation;

    public RcbMultiExportWindow(
        string iedName,
        string endpoint,
        IReadOnlyList<RcbExportRow> rows,
        Func<CancellationToken, Task<IReadOnlyList<RcbExportRow>>>? refreshAvailability,
        Func<IReadOnlyList<RcbExportRow>, SclSchemaProfile, string, CancellationToken, Task<RcbExportCompletion>> export)
    {
        _iedName = string.IsNullOrWhiteSpace(iedName) ? "IED" : iedName.Trim();
        _endpoint = string.IsNullOrWhiteSpace(endpoint) ? "Offline SCL model" : endpoint.Trim();
        _refreshAvailability = refreshAvailability;
        _export = export ?? throw new ArgumentNullException(nameof(export));

        Title = "RCB Export — Generic IEC 61850 SCL";
        Width = 1080;
        Height = 700;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        titleStack.Children.Add(new TextBlock
        {
            Text = _iedName,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold
        });
        titleStack.Children.Add(new TextBlock
        {
            Text = $"{_endpoint}  •  select one or more native RCBs",
            Margin = new Thickness(0, 3, 0, 0),
            FontSize = 11.5,
            Opacity = 0.72
        });
        header.Children.Add(titleStack);

        _checkButton = new Button
        {
            Content = "Check Availability",
            Padding = new Thickness(13, 7, 13, 7),
            IsEnabled = _refreshAvailability != null,
            VerticalAlignment = VerticalAlignment.Center
        };
        _checkButton.Click += CheckAvailability_Click;
        Grid.SetColumn(_checkButton, 1);
        header.Children.Add(_checkButton);
        root.Children.Add(header);

        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = true,
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            ItemsSource = _rows
        };
        _grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "SELECT",
            Width = 62,
            Binding = new Binding(nameof(RcbExportRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            }
        });
        _grid.Columns.Add(TextColumn("REPORT CONTROL BLOCK", nameof(RcbExportRow.Name), 180));
        _grid.Columns.Add(TextColumn("TYPE", nameof(RcbExportRow.Type), 105));
        _grid.Columns.Add(TextColumn("DATASET", nameof(RcbExportRow.DataSetName), 170));
        _grid.Columns.Add(TextColumn("MEMBERS", nameof(RcbExportRow.MemberCountText), 90));
        _grid.Columns.Add(TextColumn("STATUS", nameof(RcbExportRow.StatusText), 120));
        _grid.Columns.Add(TextColumn("REFERENCE", nameof(RcbExportRow.Reference), new DataGridLength(1, DataGridLengthUnitType.Star)));
        Grid.SetRow(_grid, 2);
        root.Children.Add(_grid);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var footerText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _summary = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold };
        _status = new TextBlock
        {
            Margin = new Thickness(0, 4, 12, 0),
            FontSize = 10.8,
            Opacity = 0.72,
            TextWrapping = TextWrapping.Wrap
        };
        footerText.Children.Add(_summary);
        footerText.Children.Add(_status);
        footer.Children.Add(footerText);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        var clear = new Button { Content = "Clear", Padding = new Thickness(12, 7, 12, 7) };
        clear.Click += (_, _) =>
        {
            foreach (var row in _rows) row.IsSelected = false;
            RefreshSelectionSummary();
        };
        actions.Children.Add(clear);

        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => Close();
        actions.Children.Add(cancel);

        _exportButton = new Button
        {
            Content = "Export SCL",
            Padding = new Thickness(15, 7, 15, 7),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = false
        };
        _exportButton.Click += Export_Click;
        actions.Children.Add(_exportButton);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        Content = root;
        ReplaceRows(rows, preserveSelection: false);
        _status.Text = "Export keeps the IED's native/static DataSets and only the RCBs you select; ARSAS runtime acquisition DataSets are not synthesized into this file.";
    }

    protected override void OnClosed(EventArgs e)
    {
        _operation?.Cancel();
        _operation?.Dispose();
        foreach (var row in _rows)
            row.PropertyChanged -= Row_PropertyChanged;
        base.OnClosed(e);
    }

    private static DataGridTextColumn TextColumn(string header, string path, DataGridLength width)
        => new()
        {
            Header = header,
            Width = width,
            IsReadOnly = true,
            Binding = new Binding(path) { Mode = BindingMode.OneWay }
        };

    private void ReplaceRows(IReadOnlyList<RcbExportRow> rows, bool preserveSelection)
    {
        var selected = preserveSelection
            ? _rows.Where(row => row.IsSelected)
                .Select(row => row.SelectionIdentity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _rows)
            row.PropertyChanged -= Row_PropertyChanged;
        _rows.Clear();

        foreach (var row in rows
                     .OrderByDescending(row => row.MemberCount > 0)
                     .ThenByDescending(row => row.Buffered)
                     .ThenBy(row => row.Reference, StringComparer.OrdinalIgnoreCase))
        {
            row.IsSelected = selected.Contains(row.SelectionIdentity);
            row.PropertyChanged += Row_PropertyChanged;
            _rows.Add(row);
        }

        RefreshSelectionSummary();
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RcbExportRow.IsSelected))
            RefreshSelectionSummary();
    }

    private void RefreshSelectionSummary()
    {
        var count = _rows.Count(row => row.IsSelected);
        _summary.Text = $"{count:N0} selected • {_rows.Count:N0} RCB(s) discovered";
        _exportButton.IsEnabled = _operation == null && count > 0;
    }

    private async void CheckAvailability_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null || _refreshAvailability == null)
            return;

        _operation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        SetBusy(true);
        _status.Text = "Checking RCB availability read-only…";
        try
        {
            var rows = await _refreshAvailability(_operation.Token).ConfigureAwait(true);
            ReplaceRows(rows, preserveSelection: true);
            _status.Text = $"Availability checked {DateTime.Now:HH:mm:ss}. Existing selections were preserved.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Availability check cancelled or timed out; no RCB was modified.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Availability check failed: {ex.Message}";
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_operation != null)
            return;

        var selected = _rows.Where(row => row.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            _status.Text = "Select at least one RCB before export.";
            return;
        }

        if (selected.Any(row => row.RequiresConfirmation))
        {
            var advisoryRows = selected.Where(row => row.RequiresConfirmation).ToArray();
            var advisoryNames = string.Join(", ", advisoryRows.Select(row => row.Name));
            var message =
                $"Availability could not be fully confirmed for {advisoryRows.Length} selected RCB(s): {advisoryNames}.\n\n" +
                "The SCL export itself is read-only. ARSAS will not reserve, enable, disable, or modify any RCB during export. " +
                "Availability is commissioning information only; confirm ownership before another SAS client reserves or enables these RCBs.\n\n" +
                "Continue generating the SCL export?";
            var answer = MessageBox.Show(
                this,
                message,
                "RCB Export Note",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.OK);
            if (answer != MessageBoxResult.OK)
                return;
        }

        var editionDialog = new SaveSclWindow(
            _iedName,
            $"Generic RCB export • {selected.Length} selected RCB(s)",
            SclSchemaProfile.Edition1V16)
        {
            Owner = this
        };
        if (editionDialog.ShowDialog() != true)
            return;

        var schema = editionDialog.ViewModel.SelectedSchemaProfile;
        var editionSuffix = schema.IsEdition2 ? "ed2" : "ed1";
        var fileDialog = new SaveFileDialog
        {
            Title = $"Export generic IEC 61850 SCL — {selected.Length} selected RCB(s)",
            Filter = "Configured IED Description (*.cid)|*.cid|Substation Configuration Language (*.scd;*.icd;*.iid)|*.scd;*.icd;*.iid|All files (*.*)|*.*",
            DefaultExt = ".cid",
            AddExtension = true,
            FileName = $"{SafeFileStem(_iedName)}-selected-rcb-{editionSuffix}.cid"
        };
        if (fileDialog.ShowDialog(this) != true)
            return;

        _operation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        SetBusy(true);
        _status.Text = $"Generating generic SCL with {selected.Length} selected RCB(s)…";
        try
        {
            var completion = await _export(selected, schema.Profile, fileDialog.FileName, _operation.Token).ConfigureAwait(true);
            _status.Text = completion.Message;
            MessageBox.Show(this, completion.Message, "RCB Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Export cancelled or timed out. Source SCL and the IED were not modified.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "RCB Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _grid.IsEnabled = !busy;
        _checkButton.IsEnabled = !busy && _refreshAvailability != null;
        RefreshSelectionSummary();
        if (busy)
            _exportButton.IsEnabled = false;
    }

    private static string SafeFileStem(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (value ?? string.Empty)
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray();
        var result = new string(chars).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(result) ? "IED" : result;
    }
}
