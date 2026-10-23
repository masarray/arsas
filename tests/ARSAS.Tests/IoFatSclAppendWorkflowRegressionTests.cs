namespace ARSAS.Tests;

public sealed class IoFatSclAppendWorkflowRegressionTests
{
    [Fact]
    public void P04_LegacyWorkspaceOwnsExplicitSclAppendWithoutAHeaderSwitcherEntryPoint()
    {
        var lifecycle = ReadRepoFile("MainWindow.WorkspaceModeSwitch.cs");
        var addIed = ReadRepoFile("IoListTestingWindow.AddIed.cs");
        var append = ReadRepoFile("MainWindow.IoTesting.SclAppend.cs");
        var host = ReadRepoFile("MainWindow.IoTesting.cs");

        Assert.DoesNotContain("InstallWorkspaceModeSwitch", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenIoFatWorkspaceMenu", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ImportAdditionalSclSourcesAsync(engineeringWindow, dialog.FileNames)", addIed, StringComparison.Ordinal);
        Assert.Contains("AppendSclIedsToLoadedFatAsync(this, sclPaths)", addIed, StringComparison.Ordinal);
        Assert.Contains("Title = \"Add IEC 61850 SCL to loaded FAT workspace\"", append, StringComparison.Ordinal);
        Assert.Contains("ImportAdditionalSclSourcesAsync(this, dialog.FileNames)", append, StringComparison.Ordinal);

        // Workbook and portable project opens intentionally retain replacement semantics.
        Assert.Contains("QueueIoFatWorkspaceReplacement(() => OpenIoListTesting_Click(sender, e))", host, StringComparison.Ordinal);
        Assert.Contains("QueueIoFatWorkspaceReplacement(() => OpenIoListPackage_Click(sender, e))", host, StringComparison.Ordinal);
    }

    [Fact]
    public void P04_AddSclAvailabilityIsIndependentFromIedPreparationAndEvidenceSession()
    {
        var window = ReadRepoFile("IoListTestingWindow.xaml.cs");
        var addIed = ReadRepoFile("IoListTestingWindow.AddIed.cs");

        Assert.Contains("public bool CanAddFatIed => !_isAddingFatIeds;", window, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool CanAddFatIed => !IsPreparingIed", window, StringComparison.Ordinal);
        Assert.Contains("ImportAdditionalSclSourcesAsync", addIed, StringComparison.Ordinal);
        Assert.Contains("AppendSclIedsToLoadedFatAsync(this, sclPaths)", addIed, StringComparison.Ordinal);
        Assert.DoesNotContain("AddSclIedsToLoadedFatAsync(this, dialog.FileNames)", addIed, StringComparison.Ordinal);
    }

    [Fact]
    public void P04_ParsesBeforeAppendGateAndMutatesOnlyNewIeds()
    {
        var append = ReadRepoFile("MainWindow.IoTesting.SclAppend.cs");

        Assert.Contains("SemaphoreSlim _ioFatSclAppendGate", append, StringComparison.Ordinal);
        var parseIndex = append.IndexOf("ImportAdditionalAsync(", StringComparison.Ordinal);
        var gateIndex = append.IndexOf("_ioFatSclAppendGate.WaitAsync", StringComparison.Ordinal);
        Assert.True(parseIndex >= 0 && gateIndex > parseIndex, "SCL parsing must complete before the append mutation gate is acquired.");

        Assert.Contains("ReferenceEquals(window, _loadedIoFatWindow)", append, StringComparison.Ordinal);
        Assert.Contains("existingIedKeys.Add", append, StringComparison.Ordinal);
        Assert.Contains("window.Project.Ieds.Add(ied)", append, StringComparison.Ordinal);
        Assert.Contains("SynchronizeImportedSclFatWithEngineering(window.Project, addedIeds)", append, StringComparison.Ordinal);
        Assert.Contains("window.RegisterAddedIeds(addedIeds)", append, StringComparison.Ordinal);
        Assert.DoesNotContain("IsPreparingIed", append, StringComparison.Ordinal);
        Assert.DoesNotContain("Session.Stop", append, StringComparison.Ordinal);
    }

    [Fact]
    public void P04_EngineeringOwnsImportedSclBeforeFatRowsArePublished()
    {
        var append = ReadRepoFile("MainWindow.IoTesting.SclAppend.cs");

        var synchronizeIndex = append.IndexOf(
            "SynchronizeImportedSclFatWithEngineering(window.Project, addedIeds)",
            StringComparison.Ordinal);
        var staticSelectionIndex = append.IndexOf("ApplyStaticDataSetSelection(device)", StringComparison.Ordinal);
        var publishIndex = append.IndexOf("window.Project.Ieds.Add(ied)", StringComparison.Ordinal);
        var registerIndex = append.IndexOf("window.RegisterAddedIeds(addedIeds)", StringComparison.Ordinal);

        Assert.True(synchronizeIndex >= 0, "Engineering synchronization must be present.");
        Assert.True(publishIndex > synchronizeIndex,
            "The exact ARIEC workspace must be established in Engineering before FAT publishes the imported IED row.");
        Assert.True(staticSelectionIndex > synchronizeIndex && staticSelectionIndex < publishIndex,
            "Static DataSet authority must be chosen in Engineering before the pending FAT plan is published.");
        Assert.True(registerIndex > publishIndex,
            "The FAT explorer may register new rows only after authority-first publication completes.");
        Assert.Contains("IoFatEngineeringSelectionBridge.ApplyEngineeringSignalSelection", append, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
