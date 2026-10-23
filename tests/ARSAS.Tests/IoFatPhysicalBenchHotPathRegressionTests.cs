namespace ARSAS.Tests;

public sealed class IoFatPhysicalBenchHotPathRegressionTests
{
    [Fact]
    public void AlreadyLiveStartContinue_BypassesPreparationAndOwnsButtonBeforeLegacyHandler()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.P0RelayBenchHotPath.cs"));

        Assert.Contains("RegisterClassHandler", source, StringComparison.Ordinal);
        Assert.Contains("typeof(Button)", source, StringComparison.Ordinal);
        Assert.Contains("Button.ClickEvent", source, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", source, StringComparison.Ordinal);
        Assert.Contains("SelectedIed?.IsLiveMonitoring == true", source, StringComparison.Ordinal);
        Assert.Contains("Session.Start(ied, live)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareIoTestIedForFatAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Stop_SealsAndSavesAwayFromDispatcher()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.P0RelayBenchHotPath.cs"));

        Assert.Contains("IoTestEvidenceJournal.BeginDeferredSealScope()", source, StringComparison.Ordinal);
        Assert.Contains("await IoTestEvidenceJournal.AwaitDeferredSealsAsync()", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(Storage.SaveNow)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceNotifications_AreNotAmplifiedIntoFullProjectionForEveryJournalProperty()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoTestMultiSessionCoordinator.cs"));

        Assert.Contains("switch (e.PropertyName)", source, StringComparison.Ordinal);
        Assert.Contains("case nameof(IoTestSessionController.EvidenceRecordCount)", source, StringComparison.Ordinal);
        Assert.Contains("Raise(nameof(EvidenceRecordCount))", source, StringComparison.Ordinal);
        Assert.Contains("case nameof(IoTestSessionController.LastJournalHash)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private void Child_PropertyChanged(object? sender, PropertyChangedEventArgs e)\n        => RaiseProjectionProperties();",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FatControl_FinalValueIsReconciledFromSharedEngineeringProcessImage()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0RelayBenchControlAuthority.cs"));
        var hotPath = File.ReadAllText(FindRepoFile("IoListTestingWindow.P0RelayBenchHotPath.cs"));

        Assert.Contains("signal.ControlStatusReference", source, StringComparison.Ordinal);
        Assert.Contains("device.Points", source, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(point => point.Sequence)", source, StringComparison.Ordinal);
        Assert.Contains("signal.ControlCurrentValue = latest.Value", source, StringComparison.Ordinal);
        Assert.Contains("ReconcileIoFatCommandValueFromSharedProcessImage(signal)", hotPath, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeFatReport_IsUsableRecordAndKeepsAcceptanceSignOff()
    {
        var core = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatV2ReportLayoutEngine.cs"));
        var supplemental = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatSupplementalReportLayoutDecorator.cs"));

        Assert.Contains("\"FAT REPORT\"", core, StringComparison.Ordinal);
        Assert.DoesNotContain("\"PREVIEW\"", core, StringComparison.Ordinal);
        Assert.DoesNotContain("\"AS TESTED\"", core, StringComparison.Ordinal);

        Assert.Contains("\"FOR FAT RECORD\"", supplemental, StringComparison.Ordinal);
        Assert.DoesNotContain("\"NOT FOR ISSUE\"", supplemental, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CUSTOMER FAT RECORD\"", supplemental, StringComparison.Ordinal);
        Assert.Contains("\"TESTED BY\"", supplemental, StringComparison.Ordinal);
        Assert.Contains("\"WITNESSED BY\"", supplemental, StringComparison.Ordinal);
        Assert.Contains("\"APPROVED BY\"", supplemental, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
