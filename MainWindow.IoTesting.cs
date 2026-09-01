using System.IO;
using System.Text.Json;
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
    private const int IoFatPollingIntervalMs = 250;
    private readonly IoListExcelImportService _ioListExcelImportService = new();
    private readonly IoFatSclProjectImportService _ioFatSclProjectImportService = new();
    private readonly IoTestLiveBindingService _ioTestLiveBindingService = new();
    private FrameworkElement? _ioListTestingLauncherCard;
    private IoTestSessionController? _activeIoTestSessionController;
    private long _ioTestObservationSequence;
    private int? _pollingIntervalBeforeIoFat;
    private int _ioFatEvidenceDrainDispatchActive;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        InitializeClockSyncLifecycle();
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

        var generalTestingCard = heroGrid.Children
            .OfType<Border>()
            .FirstOrDefault(border =>
                !Equals(border.Tag, "P2IndustrialHeroTint") &&
                border.Child is StackPanel);
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
            Text = "FAT / DATASET VERIFICATION",
            Style = TryFindResource("MicroLabel") as Style,
            Foreground = TryFindResource("Accent") as Brush,
            Margin = new Thickness(0, 0, 0, 6)
        });
        content.Children.Add(new TextBlock
        {
            Text = "Run FAT directly from SCL",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Ink") as Brush,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "Select one or multiple CID / ICD / IID / SCD / SSD files. Every static DataSet member becomes FAT scope. Excel import remains available for legacy IO-list projects.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.4,
            Foreground = TryFindResource("Muted") as Brush,
            Margin = new Thickness(0, 10, 0, 18)
        });
        content.Children.Add(CreateLauncherButton(
            "Open SCL for FAT",
            "LucideFileInput",
            "PrimaryButton",
            OpenSclFatTesting_Click,
            Brushes.White,
            new Thickness(0, 0, 0, 8)));
        content.Children.Add(CreateLauncherButton(
            "Open IO List Workbook",
            "LucideFileInput",
            "SoftButton",
            OpenIoListTesting_Click,
            null,
            new Thickness(0, 0, 0, 8)));
        content.Children.Add(CreateLauncherButton(
            "Open ARSAS Project",
            "LucideFolderOpen",
            "SoftButton",
            OpenIoListPackage_Click,
            null,
            new Thickness(0, 0, 0, 10)));
        content.Children.Add(new TextBlock
        {
            Text = "Static DataSet authority · Value 1 / Value 2 evidence · autosave · portable continuation",
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
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        button.Click += handler;
        return button;
    }

    private static Brush BrushFromHex(string value)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));

    private async void OpenSclFatTesting_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedIoFatWindow is { IsLoaded: true })
        {
            QueueIoFatWorkspaceReplacement(() => OpenSclFatTesting_Click(sender, e));
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Open IEC 61850 SCL for FAT",
            Filter = "IEC 61850 SCL (*.scd;*.cid;*.icd;*.iid;*.ssd)|*.scd;*.cid;*.icd;*.iid;*.ssd|XML SCL (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        SetStatus($"Building FAT scope from {dialog.FileNames.Length} SCL source(s)…");
        try
        {
            var import = await _ioFatSclProjectImportService.ImportAsync(
                dialog.FileNames,
                cancellationToken: _applicationCancellation.Token);
            if (import.Project.SignalCount == 0)
            {
                var details = string.Join(
                    Environment.NewLine,
                    import.Findings.Take(12).Select(finding => $"• {finding.Code}: {finding.Message}"));
                SetStatus("SCL FAT import contains no static DataSet members.");
                MessageBox.Show(
                    this,
                    "The selected SCL source(s) contain no static DataSet members. ARSAS did not fabricate FAT rows.\n\n" + details,
                    "No FAT DataSet scope",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var launch = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
                import.Project,
                import.SourceInputs,
                IoTestingProjectsRoot(),
                IoTestingEvidenceRoot(),
                CreateIoTestSession,
                _applicationCancellation.Token);
            var warningCount = import.Findings.Count(finding =>
                finding.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ||
                finding.Severity.Equals("High", StringComparison.OrdinalIgnoreCase) ||
                finding.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
            await ShowIoTestingWorkspaceAsync(launch, warningCount);
        }
        catch (OperationCanceledException)
        {
            SetStatus("SCL FAT import cancelled.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            ShowIoTestingFailure(ex, "SCL FAT import failed");
        }
    }

    private async void OpenIoListTesting_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedIoFatWindow is { IsLoaded: true })
        {
            QueueIoFatWorkspaceReplacement(() => OpenIoListTesting_Click(sender, e));
            return;
        }

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

            var launch = await IoTestWorkspaceBootstrapService.OpenWorkbookAsync(
                import.Project,
                dialog.FileName,
                IoTestingProjectsRoot(),
                IoTestingEvidenceRoot(),
                CreateIoTestSession,
                _applicationCancellation.Token);
            var importWarnings = import.AllFindings.Count(finding => finding.Severity == IoTestImportFindingSeverity.Warning);
            await ShowIoTestingWorkspaceAsync(launch, importWarnings);
        }
        catch (OperationCanceledException)
        {
            SetStatus("IO List import cancelled.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            ShowIoTestingFailure(ex, "IO List import failed");
        }
    }

    private async void OpenIoListPackage_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedIoFatWindow is { IsLoaded: true })
        {
            QueueIoFatWorkspaceReplacement(() => OpenIoListPackage_Click(sender, e));
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Open ARSAS IO FAT project",
            Filter = IoFatProjectPackageService.OpenDialogFilter,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        SetStatus($"Opening ARSAS IO FAT project {Path.GetFileName(dialog.FileName)}…");
        try
        {
            var launch = await IoTestWorkspaceBootstrapService.OpenPackageAsync(
                dialog.FileName,
                IoTestingProjectsRoot(),
                IoTestingEvidenceRoot(),
                CreateIoTestSession,
                _applicationCancellation.Token);
            await ShowIoTestingWorkspaceAsync(launch, 0);
        }
        catch (OperationCanceledException)
        {
            SetStatus("ARSAS IO FAT project import cancelled.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            ShowIoTestingFailure(ex, "ARSAS IO FAT project import failed");
        }
    }

    private Task ShowIoTestingWorkspaceAsync(IoTestWorkspaceLaunchResult launch, int importWarningCount)
    {
        var binding = _ioTestLiveBindingService.Bind(launch.Project, Devices);
        var restoredText = launch.RestoredProgress ? "saved progress restored" : "new project";
        SetStatus(
            $"FAT ready: {launch.Project.Ieds.Count} IED, {launch.Project.IncludedSignalCount} included point(s), " +
            $"{binding.SignalBoundCount} live-bound, {restoredText}, {importWarningCount + launch.Warnings.Count} warning(s).");

        if (launch.Warnings.Count > 0)
        {
            MessageBox.Show(
                this,
                string.Join(Environment.NewLine, launch.Warnings.Take(12).Select(warning => $"• {warning}")),
                "FAT workspace warnings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        _pollingIntervalBeforeIoFat ??= PollingIntervalMs;
        PollingIntervalMs = Math.Min(PollingIntervalMs, IoFatPollingIntervalMs);

        var controller = launch.Session;
        var persistence = launch.Workspace;
        var window = new IoListTestingWindow(launch.Project, controller, persistence) { Owner = this };
        RegisterLoadedIoFatWindow(window);
        _activeIoTestSessionController = controller;
        Interlocked.Exchange(ref _ioTestObservationSequence, DateTime.UtcNow.Ticks);
        _runtime.PointUpdated += Runtime_IoTestPointUpdated;

        void WindowClosed(object? sender, EventArgs args)
        {
            window.Closed -= WindowClosed;
            _runtime.PointUpdated -= Runtime_IoTestPointUpdated;
            if (ReferenceEquals(_activeIoTestSessionController, controller))
                _activeIoTestSessionController = null;
            controller.Dispose();
            persistence.Dispose();

            if (_pollingIntervalBeforeIoFat.HasValue)
            {
                PollingIntervalMs = _pollingIntervalBeforeIoFat.Value;
                _pollingIntervalBeforeIoFat = null;
            }

            if (!IsLoaded)
                return;
            IsEnabled = true;
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }

        window.Closed += WindowClosed;
        Hide();
        window.Show();
        return Task.CompletedTask;
    }

    private IoTestSessionController CreateIoTestSession(IoTestProject project, string evidenceRoot)
        => new(
            project,
            ResolveIoTestDevice,
            DispatchIoFatEvidence,
            evidenceRoot);

    private void DispatchIoFatEvidence(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var priority = Volatile.Read(ref _ioFatEvidenceDrainDispatchActive) == 0
            ? DispatcherPriority.DataBind
            : DispatcherPriority.Background;

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                Interlocked.Increment(ref _ioFatEvidenceDrainDispatchActive);
                try
                {
                    action();
                }
                finally
                {
                    Interlocked.Decrement(ref _ioFatEvidenceDrainDispatchActive);
                }
            }),
            priority);
    }

    private static string IoTestingProjectsRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARSAS",
        "IO Testing Projects");

    private static string IoTestingEvidenceRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ARSAS",
        "IO Testing Evidence");

    private void ShowIoTestingFailure(Exception ex, string title)
    {
        AddLog("ERROR", "IO Testing", ex.Message);
        MarkDiagnosticAlert();
        SetStatus($"{title}. Diagnostics is marked with !.");
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
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
