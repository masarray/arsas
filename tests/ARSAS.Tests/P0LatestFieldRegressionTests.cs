using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class P0LatestFieldRegressionTests
{
    [Theory]
    [InlineData("A", "IEDLD/MMXU1.A.phsA.cVal.mag.f", "A PhsA")]
    [InlineData("A", "IEDLD/MMXU1.A.phsB.cVal.mag.f", "A PhsB")]
    [InlineData("A", "IEDLD/MMXU1.A.phsC.cVal.mag.f", "A PhsC")]
    [InlineData("ThdA", "IEDLD/MMXU1.ThdA.phsA.instMag.f", "ThdA PhsA")]
    [InlineData("ThdPPV", "IEDLD/MMXU1.ThdPPV.phsAB.instMag.f", "ThdPPV PhsAB")]
    public void FatSignalDisplayName_PreservesDoFamilyAndAddsCompactPhaseContext(
        string signalName,
        string reference,
        string expected)
    {
        Assert.Equal(expected, IoSignalDisplayName.Format(signalName, reference));
        Assert.Equal(expected, IoFatSignalDisplayNameFormatter.Format(signalName, reference));
        Assert.Equal(reference, reference); // presentation must never rewrite technical identity
    }

    [Fact]
    public void RemovedFatSignals_AreFilteredFromActiveGridWithoutSilentlyUntickingTest()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.ActiveFatView.cs"));

        Assert.Contains("Filter = item => item is IoTestPointPlan point && point.IsIncludedInFat", source, StringComparison.Ordinal);
        Assert.Contains("_activeFatView?.Refresh()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("point.TestEnabled = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadedFaultRecords_UseTheSameRowSelectionAuthorityForSafeRedownload()
    {
        var source = File.ReadAllText(FindRepoFile("FaultRecordWindow.RedownloadSelectionAuthority.cs"));
        var transfer = File.ReadAllText(FindRepoFile("FaultRecordWindow.RedownloadUx.cs"));
        var model = File.ReadAllText(FindRepoFile("FaultRecordWindow.xaml.cs"));

        Assert.Contains("TryResolveTransferRow", source, StringComparison.Ordinal);
        Assert.Contains("row.IsSelected = !row.IsSelected", source, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", source, StringComparison.Ordinal);
        Assert.Contains("ConfigureRecordRow(row)", source, StringComparison.Ordinal);
        Assert.Contains("UpdateSmartSelectionUi()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_redownloadSelections", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_redownloadSelections", transfer, StringComparison.Ordinal);
        Assert.Contains("new Binding(nameof(FaultRecordRow.IsSelected))", transfer, StringComparison.Ordinal);
        Assert.Contains("row.IsSelected && row.CanSelectForDownload", transfer, StringComparison.Ordinal);
        Assert.Contains("ValidateFreshRecordDirectory", transfer, StringComparison.Ordinal);
        Assert.Contains(".arsas-redownload-", transfer, StringComparison.Ordinal);
        Assert.Contains("CommitFreshRecordDirectory", transfer, StringComparison.Ordinal);
        Assert.Contains("public bool CanSelectForDownload => Record.Files.Count > 0", model, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandDefaults_KeepInterlockAndSyncCheckedUntilCtlModelIsResolved()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0CommandDefaults.cs"));

        Assert.Contains("signal.ControlInterlockCheck = true", source, StringComparison.Ordinal);
        Assert.Contains("signal.ControlSynchroCheck = true", source, StringComparison.Ordinal);
        Assert.Contains("ControlModelResolved", source, StringComparison.Ordinal);
        Assert.Contains("FinalizeP0CommandDefaults", source, StringComparison.Ordinal);
        Assert.Contains("signal.PropertyChanged -= P0CommandSignal_PropertyChanged", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AssociationWatchdog_DetectsOfflineWithoutCyclicMmsProcessReadsAndArmsReconnect()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0AssociationLiveness.cs"));

        Assert.Contains("TryPingEndpointAsync", source, StringComparison.Ordinal);
        Assert.Contains("failures < 2", source, StringComparison.Ordinal);
        Assert.Contains("device.Status = \"Offline\"", source, StringComparison.Ordinal);
        Assert.Contains("_associationReconnectWanted.Add", source, StringComparison.Ordinal);
        Assert.Contains("IsMmsEndpointReachableAsync", source, StringComparison.Ordinal);
        Assert.Contains("ConnectUsingSavedModelAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadValueAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IecSignalReadResolver", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiRcbExport_ProductionClickUsesMultiSelectAndNativeDataSetsOnly()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.MultiRcbExport.cs"));
        var window = File.ReadAllText(FindRepoFile("RcbMultiExportWindow.cs"));
        var authority = File.ReadAllText(FindRepoFile("MainWindow.RcbExportClickAuthority.cs"));

        Assert.Contains("IReadOnlyList<RcbExportRow> selectedRows", source, StringComparison.Ordinal);
        Assert.Contains("MergeScopedLnChildren(merged, additional, \"DataSet\")", source, StringComparison.Ordinal);
        Assert.Contains("MergeScopedLnChildren(merged, additional, \"ReportControl\")", source, StringComparison.Ordinal);
        Assert.Contains("ValidateGenericMultiRcbDocument", source, StringComparison.Ordinal);
        Assert.Contains("ExportLegacySasRcbAsync", source, StringComparison.Ordinal);
        Assert.Contains("_rows.Where(row => row.IsSelected).ToArray()", window, StringComparison.Ordinal);
        Assert.Contains("select one or more native RCBs", window, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[ModuleInitializer]", authority, StringComparison.Ordinal);
        Assert.Contains("window.IedEditRcbMulti_Click", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateDynamic", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiveSignalPresentation_UsesSharedSemanticNameAuthorityAtEngineeringColumnLevel()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.FieldPresentationFix.cs"));

        Assert.Contains("ApplySemanticSignalColumns", source, StringComparison.Ordinal);
        Assert.Contains("CreateSemanticSignalBinding(\"IecTelegram\")", source, StringComparison.Ordinal);
        Assert.Contains("IoSignalDisplayName.Format", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IoFatSignalDisplayNameFormatter.Format", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatIecReference_RemainsOperatorResizableAndPreservesSessionWidth()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.ColumnSizing.cs"));

        Assert.Contains("_fatSignalsGrid.CanUserResizeColumns = true", source, StringComparison.Ordinal);
        Assert.Contains("\"IEC REFERENCE\"", source, StringComparison.Ordinal);
        Assert.Contains("MaximumFatIecReferenceWidth = 4096d", source, StringComparison.Ordinal);
        Assert.Contains("_sessionFatIecReferenceWidth", source, StringComparison.Ordinal);
        Assert.Contains("FatIecReferenceColumn_WidthChanged", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatEvidence_CannotAdvanceAheadOfCommittedLiveValue()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatSharedProcessEvidence.cs"));

        Assert.Contains("projectedPlans.Count == 0", source, StringComparison.Ordinal);
        Assert.Contains("GetP0FatPointIndex(fat.Project, forceRebuild: true)", source, StringComparison.Ordinal);
        Assert.Contains("_p0FatLiveVisibleSequences[plan.TestPointId] = processSequence", source, StringComparison.Ordinal);
        Assert.Contains("IsFatLiveCommitCurrent", source, StringComparison.Ordinal);
        Assert.Contains("CanPublishEvidenceForTest(liveVisibleSequence, committed.ProcessSequence)", source, StringComparison.Ordinal);
        Assert.Contains("plan.Runtime.CurrentValue", source, StringComparison.Ordinal);
        Assert.Contains("coordinator.PrimaryController.Enqueue(committed.Entry)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.Background", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IedExplorerHeader_ReturnsHomeWithoutUnloadingEngineeringOrFatWorkspace()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.ExplorerHomeNavigation.cs"));

        Assert.Contains("MainTabs.SelectedIndex = 0", source, StringComparison.Ordinal);
        Assert.Contains("SelectedDevice = null", source, StringComparison.Ordinal);
        Assert.Contains("InstallFirstRunTestingChoices()", source, StringComparison.Ordinal);
        Assert.Contains("RestoreFirstRunLauncherContract()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Devices.Clear", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseIo", source, StringComparison.OrdinalIgnoreCase);
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
