using System.IO;
using System.Threading;
using System.Windows;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class MainWindow
{
    // P0.4 owns only the short append transaction. SCL parsing happens before this gate,
    // and independent IED connection/evidence workflows do not acquire it.
    private readonly SemaphoreSlim _ioFatSclAppendGate = new(1, 1);

    internal async Task OpenSclForLoadedFatAppendAsync(IoListTestingWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!ReferenceEquals(window, _loadedIoFatWindow) || !window.IsLoaded)
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Add IEC 61850 SCL to loaded FAT workspace",
            Filter = "IEC 61850 SCL (*.scd;*.cid;*.icd;*.iid;*.ssd)|*.scd;*.cid;*.icd;*.iid;*.ssd|XML SCL (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var result = await window.ImportAdditionalSclSourcesAsync(this, dialog.FileNames);
        if (!result.Succeeded)
        {
            MessageBox.Show(
                this,
                result.Message,
                "SCL could not be added to FAT",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    internal async Task<IoTestSessionActionResult> AppendSclIedsToLoadedFatAsync(
        IoListTestingWindow window,
        IReadOnlyCollection<string> sclPaths)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(sclPaths);
        if (!ReferenceEquals(window, _loadedIoFatWindow) || !window.IsLoaded)
            return IoTestSessionActionResult.Failure("The target FAT workspace is no longer active.");

        var requestedPaths = sclPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedPaths.Length == 0)
            return IoTestSessionActionResult.Failure("Select at least one SCL source to add to FAT.");

        // Parse and validate without taking the workspace mutation gate. Existing IEDs may
        // keep connecting/monitoring, and the single active evidence session keeps its
        // immutable capture scope while ARIEC builds the new source workspaces.
        SetStatus($"Parsing {requestedPaths.Length} additional SCL source(s) · existing FAT IED workflows remain active…");
        var import = await _ioFatSclProjectImportService.ImportAdditionalAsync(
            requestedPaths,
            _applicationCancellation.Token);

        await _ioFatSclAppendGate.WaitAsync(_applicationCancellation.Token);
        try
        {
            // Revalidate after the asynchronous parse. The operator may have closed or
            // replaced the workspace while the files were being inspected.
            if (!ReferenceEquals(window, _loadedIoFatWindow) || !window.IsLoaded)
                return IoTestSessionActionResult.Failure("The target FAT workspace changed before the SCL append could be committed.");

            var existingSources = window.Project.Sources
                .ToDictionary(source => source.Sha256, StringComparer.OrdinalIgnoreCase);
            var uniqueImportedSources = import.Sources
                .Where(source => !existingSources.ContainsKey(source.Sha256))
                .ToArray();

            var existingIedKeys = window.Project.Ieds
                .Select(ied => $"{ied.IedName.Trim()}|{ied.IpAddress.Trim()}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var addedIeds = import.Project.Ieds
                .Where(ied => existingIedKeys.Add($"{ied.IedName.Trim()}|{ied.IpAddress.Trim()}"))
                .ToArray();
            if (addedIeds.Length == 0)
            {
                return IoTestSessionActionResult.Failure(
                    "The selected SCL source contains no new IED endpoint; every imported IED is already present in this FAT workspace.");
            }

            // Persist source bytes before publishing the new plans to the active project.
            // This gate serializes only append operations; it does not lock any IED's
            // connection preparation or evidence journal.
            if (window.Storage != null && uniqueImportedSources.Length > 0)
            {
                await window.Storage.AddSourcesAsync(
                    import.Project,
                    import.SourceInputs,
                    _applicationCancellation.Token);
            }

            foreach (var ied in addedIeds)
                window.Project.Ieds.Add(ied);

            var allSources = window.Project.Sources
                .Concat(uniqueImportedSources)
                .GroupBy(source => source.Sha256, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(source => source.SourceId, StringComparer.Ordinal)
                .ToArray();
            window.Project.SetSources(allSources, IoFatSourceIdentity.ComputeSetFingerprint(allSources));

            foreach (var point in addedIeds.SelectMany(ied => ied.TestPoints))
                point.PropertyChanged += IoFatSelectionPoint_PropertyChanged;

            // Existing running/preparing IEDs are deliberately untouched. Only the newly
            // appended endpoints join Engineering and the loaded FAT explorer.
            SynchronizeImportedSclFatWithEngineering(window.Project, addedIeds);
            window.RegisterAddedIeds(addedIeds);
            window.Storage?.ScheduleSave();

            var warningCount = import.Findings.Count(finding =>
                finding.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ||
                finding.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
            var message = $"Added {addedIeds.Length} IED(s) and {addedIeds.Sum(ied => ied.TestPoints.Count)} static DataSet point(s) to the loaded FAT workspace" +
                          (warningCount == 0 ? "." : $" · {warningCount} import warning(s).");
            SetStatus(message);
            return IoTestSessionActionResult.Success(message);
        }
        finally
        {
            _ioFatSclAppendGate.Release();
        }
    }
}
