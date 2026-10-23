using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;
using Microsoft.Win32;

namespace ArIED61850Tester;

/// <summary>
/// Physical relay-bench recovery for the legacy SAS RCB dialog. Some production input paths
/// can still land here instead of RcbMultiExportWindow, so this window must itself be a true
/// multi-select surface. Row click, checkbox click, Space and Shift-range all mutate the same
/// RcbExportRow.IsSelected authority. Export consumes every ticked row through the proven
/// generic multi-RCB SCL engine.
/// </summary>
public partial class RcbExportFilterWindow
{
    private int _p1LegacyRcbAnchorIndex = -1;
    private bool _p1LegacyRcbAnchorValue;

    [ModuleInitializer]
    internal static void RegisterP1LegacyRcbMultiSelectionAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(P1LegacyRcbGrid_PreviewMouseLeftButtonDown),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(DataGrid),
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(P1LegacyRcbGrid_PreviewKeyDown),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(Button),
            ButtonBase.ClickEvent,
            new RoutedEventHandler(P1LegacyRcbExport_Click),
            handledEventsToo: true);
    }

    private static void P1LegacyRcbGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            sender is not DataGrid grid ||
            Window.GetWindow(grid) is not RcbExportFilterWindow window ||
            !ReferenceEquals(grid, window.RcbGrid) ||
            e.OriginalSource is not DependencyObject source ||
            FindP1LegacyRcbAncestor<DataGridColumnHeader>(source) != null ||
            FindP1LegacyRcbAncestor<ScrollBar>(source) != null)
        {
            return;
        }

        var visualRow = FindP1LegacyRcbAncestor<DataGridRow>(source);
        if (visualRow?.DataContext is not RcbExportRow row || !row.IsSelectable)
            return;

        window.P1ToggleLegacyRcbRow(row, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        e.Handled = true;
    }

    private static void P1LegacyRcbGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space ||
            sender is not DataGrid grid ||
            Window.GetWindow(grid) is not RcbExportFilterWindow window ||
            !ReferenceEquals(grid, window.RcbGrid) ||
            grid.SelectedItem is not RcbExportRow row ||
            !row.IsSelectable)
        {
            return;
        }

        window.P1ToggleLegacyRcbRow(row, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        e.Handled = true;
    }

    private void P1ToggleLegacyRcbRow(RcbExportRow row, bool extendRange)
    {
        var rows = _viewModel.Rows.Cast<RcbExportRow>().ToList();
        var targetIndex = rows.IndexOf(row);
        if (targetIndex < 0)
            return;

        _selectionUpdateInProgress = true;
        try
        {
            MainWindow.ApplyRcbSelectionForTest(
                rows,
                ref _p1LegacyRcbAnchorIndex,
                ref _p1LegacyRcbAnchorValue,
                targetIndex,
                extendRange);

            var focus = row.IsSelected
                ? row
                : rows.FirstOrDefault(candidate => candidate.IsSelected);
            RcbGrid.SelectedItem = focus;
            _viewModel.SelectedRow = focus;
        }
        finally
        {
            _selectionUpdateInProgress = false;
        }

        P1RefreshLegacyRcbSelectionUi();
    }

    private static void P1LegacyRcbExport_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            Window.GetWindow(button) is not RcbExportFilterWindow window ||
            !ReferenceEquals(button, window.ExportButton))
        {
            return;
        }

        // Class handlers run before the XAML instance Click handler. Marking the event handled
        // prevents the old single-row Export_Click path from consuming only SelectedRow.
        e.Handled = true;
        window.P1StartLegacyMultiExport();
    }

    private async void P1StartLegacyMultiExport()
    {
        if (_activeOperation != null)
            return;

        var selected = _viewModel.Rows
            .Where(row => row.IsSelected && row.IsSelectable)
            .ToArray();
        if (selected.Length == 0)
        {
            MockStatusText.Text = "Select at least one RCB before export.";
            P1RefreshLegacyRcbSelectionUi();
            return;
        }

        var attention = selected.Where(row => row.RequiresConfirmation).ToArray();
        if (attention.Length > 0)
        {
            var names = string.Join(", ", attention.Take(8).Select(row => row.Name));
            if (attention.Length > 8)
                names += $", +{attention.Length - 8} more";
            var warning =
                $"{attention.Length} selected RCB(s) are not proven free/fully available: {names}.\n\n" +
                "Export is read-only and does not reserve or enable any RCB. Verify ownership before the target SAS imports/enables them. Continue?";
            if (MessageBox.Show(
                    this,
                    warning,
                    "Confirm RCB Selection",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var editionDialog = new SaveSclWindow(
            _viewModel.IedName,
            $"Legacy SAS multi-RCB filter • {selected.Length} selected RCB(s)",
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
            Title = $"Export legacy SAS CID — {selected.Length} selected RCB(s) — {schema.DisplayName}",
            Filter = "Configured IED Description (*.cid)|*.cid|All files (*.*)|*.*",
            DefaultExt = ".cid",
            AddExtension = true,
            FileName = $"{SafeFileStem(_viewModel.IedName)}-legacy-sas-{selected.Length}-rcb-{editionSuffix}.cid"
        };
        if (fileDialog.ShowDialog(this) != true)
            return;

        if (_viewModel.Options.IsMock || Owner is not MainWindow engineering)
        {
            MockStatusText.Text = "Multi-RCB export requires the production Engineering workspace.";
            return;
        }

        _activeOperation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        SetBusyState(true, $"Filtering SCL and validating {selected.Length} retained RCB(s)…");
        try
        {
            var completion = await engineering.ExportP1LegacyMultiRcbAsync(
                    selected,
                    schema.Profile,
                    fileDialog.FileName,
                    _activeOperation.Token)
                .ConfigureAwait(true);

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
            P1RefreshLegacyRcbSelectionUi();
        }
    }

    private void P1RefreshLegacyRcbSelectionUi()
    {
        var selected = _viewModel.Rows.Where(row => row.IsSelected && row.IsSelectable).ToArray();
        FooterSelectionSummaryText.Text = selected.Length switch
        {
            0 => "No RCB selected",
            1 => $"{selected[0].Name} • {selected[0].Type} • {selected[0].MemberCount:N0} members",
            _ => $"{selected.Length:N0} RCBs selected • {selected.Sum(row => Math.Max(0, row.MemberCount)):N0} members"
        };
        FooterRemovalSummaryText.Text =
            $"{selected.Length:N0} retained • {Math.Max(0, _viewModel.Rows.Count - selected.Length):N0} removed";
        ExportButton.IsEnabled = _activeOperation == null && selected.Length > 0;
    }

    private static T? FindP1LegacyRcbAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current != null)
        {
            if (current is T match)
                return match;

            DependencyObject? parent = null;
            try
            {
                parent = VisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
            }

            if (parent == null && current is FrameworkElement element)
                parent = element.Parent;
            current = parent;
        }
        return null;
    }
}
