using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly IoListExcelImportService _ioListExcelImportService = new();
    private readonly IoTestLiveBindingService _ioTestLiveBindingService = new();
    private FrameworkElement? _ioListTestingLauncherCard;
    private IoTestSessionController? _activeIoTestSessionController;
    private long _ioTestObservationSequence;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Dispatcher.BeginInvoke(new Action(InstallFirstRunTestingChoices), DispatcherPriority.Loaded);
    }

    private void InstallFirstRunTestingChoices()
    {
        if (_ioListTestingLauncherCard != null || MainTabs.Items.Count == 0)
            return;
        if (MainTabs.Items[0] is not TabItem explorerTab || explorerTab.Content is not Grid explorerGrid)
            return;

        var workspace = explorerGrid.Children
            .OfType<Grid>()
            .FirstOrDefault(child => Grid.GetColumn(child) == 2);
        var emptyState = workspace?.Children
            .OfType<Border>()
            .FirstOrDefault(border =>
                BindingOperations.GetBinding(border, UIElement.VisibilityProperty)?.Path?.Path == nameof(EmptyExplorerVisibility));
        if (emptyState?.Child is not Grid heroGrid)
            return;

        var generalTestingCard = heroGrid.Children.OfType<Border>().SingleOrDefault();
        if (generalTestingCard?.Child is not StackPanel generalContent)
            return;

        heroGrid.Children.Remove(generalTestingCard);
        ConfigureGeneralTestingCard(generalTestingCard, generalContent);

        var ioListCard = CreateIoListTestingCard();
        var chooser = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(28),
            MaxWidth = 1000
        };
        chooser.Children.Add(generalTestingCard);
        chooser.Children.Add(ioListCard);
        heroGrid.Children.Add(chooser);
        _ioListTestingLauncherCard = ioListCard;
    }

    private void ConfigureGeneralTestingCard(Border card, StackPanel content)
    {
        card.Width = 510;
        card.MaxWidth = 510;
        card.Margin = new Thickness(0, 0, 12, 0);
        card.HorizontalAlignment = HorizontalAlignment.Stretch;
        card.VerticalAlignment = VerticalAlignment.Stretch;
        card.BorderBrush = BrushFromHex("#DCE6F5");
        card.BorderThickness = new Thickness(1);

        var title = content.Children.OfType<TextBlock>().FirstOrDefault();
        var description = content.Children.OfType<TextBlock>().Skip(1).FirstOrDefault();
        var actions = content.Children.OfType<WrapPanel>().FirstOrDefault();
        if (title == null || description == null || actions == null)
            return;

        content.Children.Insert(0, new TextBlock
        {
            Text = "GENERAL IEC 61850 TESTING",
            Style = TryFindResource("MicroLabel") as Style,
            Foreground = TryFindResource("Accent") as Brush,
            Margin = new Thickness(0, 0, 0, 6)
        });
        title.Text = "Add an IED for general testing";
        description.Text = "Connect a relay by IP for model discovery, signal selection, live monitoring, event analysis, and the complete IEC 61850 engineering workflow.";

        actions.Children.Clear();
        actions.Children.Add(CreateLauncherButton(
            "Add IED",
            "LucidePlus",
            "PrimaryButton",
            AddRelay_Click,
            Brushes.White,
            new Thickness(0, 0, 10, 0)));
        actions.Children.Add(CreateLauncherButton(
            "Open Project",
            "LucideFolderOpen",
            "SoftButton",
            OpenProject_Click,
            null,
            new Thickness(0, 0, 10, 0)));
        actions.Children.Add(new TextBlock
        {
            Text = "Manual discovery and general-purpose testing",
            Style = TryFindResource("Caption") as Style,
            VerticalAlignment = VerticalAlignment.Center
        });
    }

    private Border CreateIoListTestingCard()
    {
        var card = new Border
        {
            Width = 430,
            MaxWidth = 430,
            Background = BrushFromHex("#F8FAFC"),
            Opacity = 0.94,
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(22),
            BorderBrush = BrushFromHex("#BFD2F1"),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "FAT / IO LIST TESTING",
            Style = TryFindResource("MicroLabel") as Style,
            Foreground = TryFindResource("Accent") as Brush,
            Margin = new Thickness(0, 0, 0, 6)
        });
        content.Children.Add(new TextBlock
        {
            Text = "Run FAT from an approved IO List",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Ink") as Brush,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "Import the ARSAS Excel template, choose an IED, and capture ordered ON and OFF timestamps automatically in a dedicated evidence workspace.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.4,
            Foreground = TryFindResource("Muted") as Brush,
            Margin = new Thickness(0, 10, 0, 18)
        });
        content.Children.Add(CreateLauncherButton(
            "Open IO List Workbook",
            "LucideFileInput",
            "PrimaryButton",
            OpenIoListTesting_Click,
            Brushes.White,
            new Thickness(0, 0, 0, 10)));
        content.Children.Add(new TextBlock
        {
            Text = "IED-scoped signals · read-only IEC 61850 observation · automatic FAT evidence",
            Style = TryFindResource("Caption") as Style,
            TextWrapping = TextWrapping.Wrap
        });
        card.Child = content;
        return card;
    }

    private Button CreateLauncherButton(
        string text,
        string iconResource,
        string styleResource,
        RoutedEventHandler handler,
        Brush? iconStroke,
        Thickness margin)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = TryFindResource(iconResource) as Geometry,
            Style = TryFindResource("LucideIcon") as Style
        };
        if (iconStroke != null)
            icon.Stroke = iconStroke;

        var buttonContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        buttonContent.Children.Add(new Viewbox
        {
            Width = 15,
            Height = 15,
            Margin = new Thickness(0, 0, 7, 0),
            Child = icon
        });
        buttonContent.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold
        });

        var button = new Button
        {
            Style = TryFindResource(styleResource) as Style,
            Content = buttonContent,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = margin,
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += handler;
        return button;
    }

    private static Brush BrushFromHex(string value)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));

    private async void OpenIoListTesting_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import ARSAS IO List FAT workbook",
            Filter = "ARSAS IO List workbook (*.xlsx)|*.xlsx|Excel workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        SetStatus($"Importing IO List test plan from {Path.GetFileName(dialog.FileName)}…");
        try
        {
            var import = await _ioListExcelImportService.ImportAsync(
                dialog.FileName,
                _applicationCancellation.Token);
            var errors = import.AllFindings
                .Where(finding => finding.Severity == IoTestImportFindingSeverity.Error)
                .ToList();
            if (!import.IsValid)
            {
                var details = string.Join(
                    Environment.NewLine,
                    errors.Take(12).Select(finding => $"• {finding.Code}: {finding.Message}"));
                if (errors.Count > 12)
                    details += $"{Environment.NewLine}• …and {errors.Count - 12} more error(s).";

                SetStatus("IO List import was rejected. The source workbook was not guessed or partially executed.");
                MessageBox.Show(
                    this,
                    $"ARSAS could not import this IO List workbook safely.\n\n{details}",
                    "IO List import rejected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var binding = _ioTestLiveBindingService.Bind(import.Project, Devices);
            var warnings = import.AllFindings.Count(finding =>
                finding.Severity == IoTestImportFindingSeverity.Warning);
            SetStatus(
                $"IO List ready: {import.Project.Ieds.Count} IED, {import.Project.SignalCount} SDI, " +
                $"{binding.SignalBoundCount} matched to the loaded workspace, {warnings} warning(s).");

            var journalRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ARSAS",
                "IO Testing Evidence");
            using var controller = new IoTestSessionController(
                import.Project,
                ResolveIoTestDevice,
                action => Dispatcher.BeginInvoke(action, DispatcherPriority.Background),
                journalRoot);
            var window = new IoListTestingWindow(import.Project, controller) { Owner = this };
            _activeIoTestSessionController = controller;
            Interlocked.Exchange(ref _ioTestObservationSequence, DateTime.UtcNow.Ticks);
            _runtime.PointUpdated += Runtime_IoTestPointUpdated;
            Hide();
            try
            {
                window.ShowDialog();
            }
            finally
            {
                _runtime.PointUpdated -= Runtime_IoTestPointUpdated;
                _activeIoTestSessionController = null;
                Show();
                if (WindowState == System.Windows.WindowState.Minimized)
                    WindowState = System.Windows.WindowState.Normal;
                Activate();
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("IO List import cancelled.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            AddLog("ERROR", "IO Testing", ex.Message);
            MarkDiagnosticAlert();
            SetStatus("IO List import failed. Diagnostics is marked with !.");
            MessageBox.Show(this, ex.Message, "IO List import failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Runtime_IoTestPointUpdated(Iec61850PointSnapshot snapshot)
    {
        var point = snapshot.Point;
        _activeIoTestSessionController?.Enqueue(new Iec61850EventEntry
        {
            Sequence = Interlocked.Increment(ref _ioTestObservationSequence),
            DeviceId = point.DeviceId,
            PointKey = point.PointKey,
            DeviceTimestamp = snapshot.DeviceTimestamp,
            DeviceName = point.DeviceName,
            IpAddress = point.IpAddress,
            SignalName = point.SignalName,
            IecReference = point.IecReference,
            OldValue = snapshot.PreviousValue,
            NewValue = snapshot.Value,
            Quality = snapshot.Quality,
            SourceMode = snapshot.SourceMode,
            Reason = snapshot.Reason
        });
    }

    private Iec61850MonitorDevice? ResolveIoTestDevice(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        return Devices.FirstOrDefault(device =>
            device.DeviceId.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            device.Name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            device.SclIedName.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            device.IpAddress.Equals(key, StringComparison.OrdinalIgnoreCase));
    }
}
