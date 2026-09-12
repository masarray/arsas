using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private const string NativeFatPrintPreviewTitle = "IEC 61850 FAT Evidence Report";
    private const string NativeFatPrintPreviewSubtitle = "Static DataSet verification · generic Value 1 / Value 2 evidence · source identity preserved";

    /// <summary>
    /// P3 renderer. This method receives only an immutable selected-IED snapshot and never
    /// binds to SelectedDevice, canonical live rows, sparse evidence, or acquisition state.
    /// The window therefore remains frozen even while Engineering continues monitoring.
    /// </summary>
    private void ShowNativeFatPrintPreview(NativeFatPrintPreviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var preview = new Window
        {
            Owner = this,
            Title = NativeFatPrintPreviewTitle,
            Width = 1180,
            Height = 820,
            MinWidth = 900,
            MinHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249))
        };

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var page = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 226, 238)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(30, 26, 30, 24),
            Effect = null
        };

        var pageGrid = new Grid();
        pageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pageGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        pageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pageGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        pageGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = NativeFatPrintPreviewTitle,
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 32, 51))
        });
        heading.Children.Add(new TextBlock
        {
            Text = NativeFatPrintPreviewSubtitle,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            Margin = new Thickness(0, 5, 0, 0)
        });
        pageGrid.Children.Add(heading);

        var summary = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
            Margin = new Thickness(0)
        };
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var selectedIed = new StackPanel { Margin = new Thickness(14, 10, 14, 10) };
        selectedIed.Children.Add(new TextBlock
        {
            Text = "SELECTED IED",
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133))
        });
        selectedIed.Children.Add(new TextBlock
        {
            Text = $"{snapshot.IedName} · {snapshot.IpAddress}:{snapshot.Port}",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 32, 51)),
            Margin = new Thickness(0, 3, 0, 0)
        });
        summary.Children.Add(selectedIed);

        var progress = new StackPanel
        {
            Margin = new Thickness(18, 10, 14, 10),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        progress.Children.Add(new TextBlock
        {
            Text = "PROGRESS",
            FontSize = 9.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        progress.Children.Add(new TextBlock
        {
            Text = snapshot.ProgressText,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(36, 88, 184)),
            Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        Grid.SetColumn(progress, 1);
        summary.Children.Add(progress);
        Grid.SetRow(summary, 2);
        pageGrid.Children.Add(summary);

        var grid = new DataGrid
        {
            ItemsSource = snapshot.Rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            IsReadOnly = true,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 32,
            ColumnHeaderHeight = 34,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(229, 234, 242)),
            VerticalGridLinesBrush = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 226, 238)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White
        };
        if (TryFindResource("ModernDataGrid") is Style modernDataGrid)
            grid.Style = modernDataGrid;

        AddPreviewColumn(grid, "Signal", nameof(NativeFatPrintPreviewRow.Signal), 170);
        AddPreviewColumn(grid, "IEC 61850 reference", nameof(NativeFatPrintPreviewRow.IecReference), 285);
        AddPreviewColumn(grid, "Type", nameof(NativeFatPrintPreviewRow.Type), 90);
        AddPreviewColumn(grid, "Live value", nameof(NativeFatPrintPreviewRow.LiveValue), 115);
        AddPreviewColumn(grid, "Value 1", nameof(NativeFatPrintPreviewRow.Value1), 115);
        AddPreviewColumn(grid, "Value 2", nameof(NativeFatPrintPreviewRow.Value2), 115);
        AddPreviewColumn(grid, "Status", nameof(NativeFatPrintPreviewRow.Status), 110);
        AddPreviewColumn(grid, "Result", nameof(NativeFatPrintPreviewRow.Result), 105);

        Grid.SetRow(grid, 4);
        pageGrid.Children.Add(grid);
        page.Child = pageGrid;
        root.Children.Add(page);
        preview.Content = root;
        preview.Show();
    }

    private static void AddPreviewColumn(DataGrid grid, string header, string path, double width)
    {
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path) { Mode = BindingMode.OneWay },
            Width = new DataGridLength(width),
            IsReadOnly = true
        });
    }
}
