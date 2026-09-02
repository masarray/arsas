using System.IO;
using System.Windows;
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

        SetAddingFatIeds(true);
        PreparationStatusText = $"Importing {dialog.FileNames.Length} SCL source(s) into the current FAT workspace…";
        try
        {
            var result = await engineeringWindow.AddSclIedsToLoadedFatAsync(this, dialog.FileNames);
            PreparationStatusText = result.Message;
            if (!result.Succeeded)
                ShowActionResult(result, "IED could not be added");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            PreparationStatusText = ex.Message;
            MessageBox.Show(this, ex.Message, "Add FAT IED failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
