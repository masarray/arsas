using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Makes the embedded production FAT workspace a projection of the Engineering workspace.
/// Opening SCL in Explorer is sufficient; the expensive FAT projection is created only when
/// the operator actually enters FAT so normal Engineering acquisition remains untouched.
/// </summary>
public partial class MainWindow
{
    private bool _productionFatEngineeringBootstrapInstalled;
    private bool _productionFatEngineeringBootstrapBusy;
    private CancellationTokenSource? _productionFatEngineeringBootstrapCts;

    [ModuleInitializer]
    internal static void RegisterProductionFatEngineeringBootstrap()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ProductionFatEngineeringBootstrap_Loaded),
            handledEventsToo: true);
    }

    private static void ProductionFatEngineeringBootstrap_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._productionFatEngineeringBootstrapInstalled)
            return;

        window._productionFatEngineeringBootstrapInstalled = true;
        window.MainTabs.SelectionChanged += window.ProductionFatEngineeringBootstrap_SelectionChanged;
        window.PropertyChanged += window.ProductionFatEngineeringBootstrap_PropertyChanged;
        window.Closed += window.ProductionFatEngineeringBootstrap_Closed;
    }

    private void ProductionFatEngineeringBootstrap_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs) || MainTabs.SelectedIndex != NativeFatWorkspaceIndex)
            return;
        QueueProductionFatEngineeringBootstrap();
    }

    private void ProductionFatEngineeringBootstrap_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedDevice) || MainTabs.SelectedIndex != NativeFatWorkspaceIndex)
            return;

        // A running production FAT session owns its latched IED. The normal embedded-host
        // selection bridge handles idle retargeting. Bootstrap is only needed before a FAT
        // workspace exists.
        if (_productionFatWindow == null && _loadedIoFatWindow == null)
            QueueProductionFatEngineeringBootstrap();
    }

    private void QueueProductionFatEngineeringBootstrap()
    {
        // Never prewarm FAT while the operator is in Explorer/Live Monitor/etc. Creating the
        // production FAT projection off-tab caused field regressions by mutating acquisition
        // authority and adding UI/runtime work during ordinary Engineering operation.
        if (!_productionFatEngineeringBootstrapInstalled || MainTabs.SelectedIndex != NativeFatWorkspaceIndex)
            return;

        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(async () => await EnsureProductionFatFromEngineeringAsync()));
    }

    private async Task EnsureProductionFatFromEngineeringAsync()
    {
        if (_productionFatEngineeringBootstrapBusy ||
            MainTabs.SelectedIndex != NativeFatWorkspaceIndex ||
            !ProductionFatTabReady)
        {
            return;
        }

        if (_productionFatWindow is { IsLoaded: true } || _loadedIoFatWindow is { IsLoaded: true })
        {
            SynchronizeProductionFatSelectedIed();
            return;
        }

        var selected = SelectedDevice;
        if (selected?.SclWorkspace == null ||
            selected.SclWorkspace.DesignModel.DataSets.Sum(dataSet => dataSet.Members.Count) == 0)
        {
            SetStatus(selected == null
                ? "FAT · select an Engineering IED with a static DataSet."
                : $"FAT · {selected.Name} has no static DataSet scope in the Engineering SCL model.");
            return;
        }

        var engineeringDevices = Devices
            .Where(device => device.SclWorkspace != null)
            .Where(device => device.SclWorkspace!.DesignModel.DataSets.Sum(dataSet => dataSet.Members.Count) > 0)
            .Where(device => !string.IsNullOrWhiteSpace(device.SclSourcePath))
            .ToArray();
        if (engineeringDevices.All(device => !ReferenceEquals(device, selected)))
        {
            SetStatus($"FAT · Engineering source provenance for {selected.Name} is unavailable; use Open SCL to restore the source authority.");
            return;
        }

        _productionFatEngineeringBootstrapBusy = true;
        _productionFatEngineeringBootstrapCts?.Cancel();
        _productionFatEngineeringBootstrapCts?.Dispose();
        _productionFatEngineeringBootstrapCts = CancellationTokenSource.CreateLinkedTokenSource(_applicationCancellation.Token);
        var token = _productionFatEngineeringBootstrapCts.Token;
        SetStatus($"FAT · preparing {selected.Name} from the Engineering static DataSet…");

        try
        {
            var projection = await IoFatEngineeringWorkspaceProjectionService.BuildAsync(
                engineeringDevices,
                token);
            token.ThrowIfCancellationRequested();

            // Register the exact same ARIEC workspace instances already owned by Explorer.
            // Production FAT preparation can therefore prove shared SCL authority without
            // reparsing XML or starting a second model/acquisition stack.
            _ioFatSclProjectImportService.AdoptEngineeringRuntimeWorkspaces(projection.RuntimeWorkspaces);

            var launch = await IoTestWorkspaceBootstrapService.OpenSourcesAsync(
                projection.Project,
                projection.SourceInputs,
                IoTestingProjectsRoot(),
                IoTestingEvidenceRoot(),
                CreateIoTestSession,
                token);
            token.ThrowIfCancellationRequested();

            SynchronizeImportedSclFatWithEngineering(launch.Project);

            var retiredManualRows = launch.Project.Ieds.Sum(
                IoFatEngineeringSelectionBridge.RetireManualWorkspaceRowsForStaticDataSetMode);
            if (retiredManualRows > 0)
            {
                AddLog(
                    "INFO",
                    "FAT",
                    $"Automatic Static DataSet scope retired {retiredManualRows} manual SCL workspace overlay(s); static membership remains authoritative.");
            }

            RegisterSharedSclSourcePaths(launch.Project, launch.Project.Ieds, projection.SourceInputs);
            foreach (var ied in launch.Project.Ieds)
            {
                var device = ResolveIoTestDevice(ied.LiveDeviceId)
                             ?? ResolveIoTestDevice(ied.IpAddress)
                             ?? ResolveIoTestDevice(ied.IedName);
                if (device is not null)
                    MarkSharedSelectionAuthority(device);
            }

            launch.Workspace.ScheduleSave();
            await ShowIoTestingWorkspaceAsync(launch, importWarningCount: 0);
            SynchronizeProductionFatSelectedIed();
            SetStatus($"FAT ready · {selected.Name} · Engineering static DataSet authority reused · no SCL re-import.");
        }
        catch (OperationCanceledException)
        {
            // Fast navigation/close is normal. No modal interruption is appropriate here.
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            AddLog("WARN", "FAT", $"Automatic Engineering FAT bootstrap unavailable: {ex.Message}");
            SetStatus($"FAT · could not reuse the Engineering static DataSet automatically: {ex.Message}");
        }
        finally
        {
            _productionFatEngineeringBootstrapBusy = false;
        }
    }

    private void ProductionFatEngineeringBootstrap_Closed(object? sender, EventArgs e)
    {
        MainTabs.SelectionChanged -= ProductionFatEngineeringBootstrap_SelectionChanged;
        PropertyChanged -= ProductionFatEngineeringBootstrap_PropertyChanged;
        Closed -= ProductionFatEngineeringBootstrap_Closed;
        _productionFatEngineeringBootstrapCts?.Cancel();
        _productionFatEngineeringBootstrapCts?.Dispose();
        _productionFatEngineeringBootstrapCts = null;
    }
}
