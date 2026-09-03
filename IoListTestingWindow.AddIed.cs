using System.IO;
using System.Windows;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private async void AddFatIedFromScl_Click(object sender, RoutedEventArgs e)
    {
        if (!CanAddFatIed || Owner is not MainWindow engineeringWindow)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Add IEC 61850 IED to current FAT workspace",
            Filter = "IEC 61850 SCL (*.scd;*.cid;*.icd;*.iid;*.ssd)|*.scd;*.cid;*.icd;*.iid;*.ssd|XML SCL (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var result = await ImportAdditionalSclSourcesAsync(engineeringWindow, dialog.FileNames);
        if (!result.Succeeded)
            ShowActionResult(result, "IED could not be added");
    }

    internal async Task<IoTestSessionActionResult> ImportAdditionalSclSourcesAsync(
        MainWindow engineeringWindow,
        IReadOnlyCollection<string> sclPaths)
    {
        ArgumentNullException.ThrowIfNull(engineeringWindow);
        ArgumentNullException.ThrowIfNull(sclPaths);
        if (_isAddingFatIeds)
            return IoTestSessionActionResult.Failure("Another SCL append is already being prepared for this FAT workspace.");

        SetAddingFatIeds(true);
        PreparationStatusText = $"Importing {sclPaths.Count} SCL source(s) into the current FAT workspace…";
        try
        {
            var result = await engineeringWindow.AppendSclIedsToLoadedFatAsync(this, sclPaths);
            PreparationStatusText = result.Message;
            return result;
        }
        catch (OperationCanceledException)
        {
            const string message = "SCL append was cancelled.";
            PreparationStatusText = message;
            return IoTestSessionActionResult.Failure(message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            PreparationStatusText = ex.Message;
            return IoTestSessionActionResult.Failure(ex.Message);
        }
        finally
        {
            SetAddingFatIeds(false);
        }
    }

    internal void RegisterAddedIeds(IReadOnlyCollection<Models.IoTesting.IoTestIedPlan> addedIeds)
    {
        Project.InitializeRuntimeNotifications();
        foreach (var ied in addedIeds)
        {
            if (_selectedIedContextInstalled)
                ied.PropertyChanged += ContextIed_PropertyChanged;
            Storage?.TrackAddedIed(ied);
        }

        FatIedList.Items.Refresh();
        SelectedIed = addedIeds.FirstOrDefault() ?? SelectedIed;
        Raise(nameof(ProjectSummary));
        RaiseStatusProperties();
        RaiseSelectedIedContextProperties();
        Storage?.ScheduleSave();
    }
}
