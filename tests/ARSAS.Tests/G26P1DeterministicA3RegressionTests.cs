namespace ARSAS.Tests;

public sealed class G26P1DeterministicA3RegressionTests
{
    [Fact]
    public void A3_ObservesExistingRuntimeCommand_AndNeverExecutesControlItself()
    {
        var source = Read("Services/DynamicReportCommandBoundDataChangeCommissioningService.cs");

        Assert.Contains("runtime.Diagnostic += RuntimeDiagnosticHandler", source, StringComparison.Ordinal);
        Assert.Contains("DynamicReportCommandBoundStimulusWitnessServiceV3.TryBuildRuntimeIntent", source, StringComparison.Ordinal);
        Assert.Contains("Control execution requested:", Read("Services/DynamicReportCommandBoundStimulusWitnessServiceV3.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteControlAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteControl", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A3_PreflightRequiresQualifiedCommandFocusIntersection_BeforeCoreReportMutation()
    {
        var source = Read("Services/DynamicReportCommandBoundDataChangeCommissioningService.cs");

        var targetGate = source.IndexOf("BuildEligibleCommandTargets(", StringComparison.Ordinal);
        var noTargetBlock = source.IndexOf("if (eligibleTargets.Count == 0)", StringComparison.Ordinal);
        var coreStart = source.IndexOf("new DynamicReportSpontaneousDataChangeCommissioningService", StringComparison.Ordinal);

        Assert.True(targetGate >= 0);
        Assert.True(noTargetBlock > targetGate);
        Assert.True(coreStart > noTargetBlock);
        Assert.Contains("No RCB mutation was attempted", source, StringComparison.Ordinal);
        Assert.Contains("Re-qualify an envelope containing CSWI/XCBR status before A3", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A3_PassRequiresSameDataSetIndexForCommandTransitionAndDchgReport()
    {
        var source = Read("Services/DynamicReportCommandBoundDataChangeCommissioningService.cs");

        Assert.Contains("CorrelateIndexes(coreResult.IncludedIndexes, changedIndexes)", source, StringComparison.Ordinal);
        Assert.Contains("coreResult.SpontaneousDataChangeProven &&", source, StringComparison.Ordinal);
        Assert.Contains("witnessResult.CommandCaptured &&", source, StringComparison.Ordinal);
        Assert.Contains("witnessResult.CommandBoundTransitionProven &&", source, StringComparison.Ordinal);
        Assert.Contains("correlatedIndexes.Length > 0", source, StringComparison.Ordinal);
        Assert.Contains("var success = coreResult.IsSuccess && correlation", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A3_ReusesStrictDchgOnlyCoreAndMandatoryCleanup()
    {
        var wrapper = Read("Services/DynamicReportCommandBoundDataChangeCommissioningService.cs");
        var core = Read("Services/DynamicReportSpontaneousDataChangeCommissioningService.cs");

        Assert.Contains("DynamicReportSpontaneousDataChangeCommissioningService", wrapper, StringComparison.Ordinal);
        Assert.Contains("internal const string TemporaryTriggerOptions = \"dchg\"", core, StringComparison.Ordinal);
        Assert.Contains("internal const string TemporaryOptionalFields = \"reason-for-inclusion data-set-name\"", core, StringComparison.Ordinal);
        Assert.Contains("GI=false, integrity=false, qchg=false, dupd=false", core, StringComparison.Ordinal);
        Assert.Contains("triggerGeneralInterrogation: false", core, StringComparison.Ordinal);
        Assert.Contains("SpontaneousReportHasForbiddenReason", core, StringComparison.Ordinal);
        Assert.Contains("MonitorCleanupSucceeded", core, StringComparison.Ordinal);
        Assert.Contains("ProofFieldRestoreSucceeded", core, StringComparison.Ordinal);
        Assert.Contains("FreshCleanupClosureSucceeded", core, StringComparison.Ordinal);
    }

    [Fact]
    public void A3_CannotAdvanceProductionEligibility()
    {
        var source = Read("Services/DynamicReportCommandBoundDataChangeCommissioningService.cs");
        var evidenceWindow = Read("DynamicReportQualificationResultWindow.G26P1A3.cs");

        Assert.DoesNotContain("MarkProductionEligible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", source, StringComparison.Ordinal);
        Assert.Contains("profile remains InformationReportProven", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A3 command-bound dchg PASS != ProductionEligible", evidenceWindow, StringComparison.Ordinal);
        Assert.Contains("Production automatic dynamic reporting remains OFF", evidenceWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void A3_HasSeparateExplicitHotkeyFromA21Witness()
    {
        var ui = Read("DynamicReportCommandBoundWitnessUiBehavior.cs");

        Assert.Contains("(e.Key != Key.F && e.Key != Key.A)", ui, StringComparison.Ordinal);
        Assert.Contains("var a3 = e.Key == Key.A", ui, StringComparison.Ordinal);
        Assert.Contains("DynamicReportCommandBoundDataChangeCommissioningService", ui, StringComparison.Ordinal);
        Assert.Contains("G2.6-P1 A3 READY", Read("Services/DynamicReportCommandBoundDataChangeCommissioningService.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void EngineLock_PinsMergedProductionConsumerButKeepsCurrentFieldStateLocked()
    {
        var engineLock = Read("engines/ARIEC61850.lock.json");

        Assert.Contains("\"ref\": \"main\"", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aa2ddfb47af5f3b806858553568792fbc21a64f1", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #97", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InformationReportProven", engineLock, StringComparison.Ordinal);
        Assert.Contains("production automatic dynamic reporting remains OFF", engineLock, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

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
