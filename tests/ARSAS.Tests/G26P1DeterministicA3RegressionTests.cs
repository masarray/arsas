namespace ARSAS.Tests;

public sealed class G26P1DeterministicA3RegressionTests
{
    [Fact]
    public void A3_CoreStillObservesRuntimeCommand_AndNeverExecutesControlItself()
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
    public void A3_PassRequiresSuccessfulNativeControlEvidence_AndReportStrictlyAfterCommand()
    {
        var wrapper = Read("Services/DynamicReportCommandBoundDataChangeCommissioningService.cs");
        var core = Read("Services/DynamicReportSpontaneousDataChangeCommissioningService.cs");

        Assert.Contains("IsAcceptedNativeControlResultDiagnostic", wrapper, StringComparison.Ordinal);
        Assert.Contains("nativeCommandAcceptance", wrapper, StringComparison.Ordinal);
        Assert.Contains("nativeControlAccepted &&", wrapper, StringComparison.Ordinal);
        Assert.Contains("Request intent alone cannot prove command acceptance", wrapper, StringComparison.Ordinal);
        Assert.Contains("coreResult.ReportReceivedAtUtc.Value > witnessResult.CommandObservedAtUtc.Value", wrapper, StringComparison.Ordinal);
        Assert.Contains("reportAfterCommand &&", wrapper, StringComparison.Ordinal);
        Assert.Contains("Pre-command report traffic cannot satisfy command-bound A3", wrapper, StringComparison.Ordinal);
        Assert.Contains("public DateTimeOffset? ReportReceivedAtUtc", core, StringComparison.Ordinal);
        Assert.Contains("receivedAt={frame.ReceivedAt:O}", core, StringComparison.Ordinal);
        Assert.Contains("reportReceivedAtUtc = frame.ReceivedAt", core, StringComparison.Ordinal);
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
        Assert.Contains("carries a non-dchg reason under a dchg-only lease", core, StringComparison.Ordinal);
        Assert.Contains("MonitorCleanupSucceeded", core, StringComparison.Ordinal);
        Assert.Contains("ProofFieldRestoreSucceeded", core, StringComparison.Ordinal);
        Assert.Contains("FreshCleanupClosureSucceeded", core, StringComparison.Ordinal);
    }

    [Fact]
    public void A3_CannotAdvanceProductionEligibility()
    {
        var source = Read("Services/DynamicReportCommandBoundDataChangeCommissioningService.cs");
        var auto = Read("Services/DynamicReportQ0TargetLockedAutoA3CommissioningService.cs");
        var evidenceWindow = Read("DynamicReportQualificationResultWindow.G26P1A3.cs");

        Assert.DoesNotContain("MarkProductionEligible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible", auto, StringComparison.Ordinal);
        Assert.Contains("profile remains InformationReportProven", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A3 command-bound dchg PASS != ProductionEligible", evidenceWindow, StringComparison.Ordinal);
        Assert.Contains("Production automatic dynamic reporting remains OFF", evidenceWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void A3_HasSeparateExplicitHotkeyFromA21Witness_AndUsesQ0AutoCoordinator()
    {
        var ui = Read("DynamicReportCommandBoundWitnessUiBehavior.cs");

        Assert.Contains("e.Key != Key.F", ui, StringComparison.Ordinal);
        Assert.Contains("e.Key != Key.A", ui, StringComparison.Ordinal);
        Assert.Contains("var a3 = e.Key == Key.A", ui, StringComparison.Ordinal);
        Assert.Contains("DynamicReportQ0TargetLockedAutoA3CommissioningService", ui, StringComparison.Ordinal);
        Assert.Contains("G2.6-P1 A3 READY", Read("Services/DynamicReportCommandBoundDataChangeCommissioningService.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Arm G2.6-P1 deterministic A3", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("G2.6-P1 Transactional Recovery\"", ui, StringComparison.Ordinal);
    }

    [Fact]
    public void Q0AutoA3_IsHardBoundToExactFieldIdentityControlStatusAndOpenStimulus()
    {
        var auto = Read("Services/DynamicReportQ0TargetLockedAutoA3CommissioningService.cs");

        Assert.Contains("ExpectedStableIdentity = \"ied:AA1C1F08R4\"", auto, StringComparison.Ordinal);
        Assert.Contains("sha256:50c691318c6d6a16b68b121ac48627c26e6e32b937836d559dca1b9eb559f0d9", auto, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TargetControlReference = \"AA1C1F08R4Q0/CSWI1.Pos\"", auto, StringComparison.Ordinal);
        Assert.Contains("TargetStatusReference = \"AA1C1F08R4Q0/CSWI1.Pos.stVal\"", auto, StringComparison.Ordinal);
        Assert.Contains("AutoStimulusValue = \"Open\"", auto, StringComparison.Ordinal);
        Assert.Contains("CurrentState.Equals(\"Closed\"", auto, StringComparison.Ordinal);
    }

    [Fact]
    public void Q0AutoA3_UsesExistingRuntimeControlPathExactlyOnceWithoutToggleRetryOrClose()
    {
        var auto = Read("Services/DynamicReportQ0TargetLockedAutoA3CommissioningService.cs");

        Assert.Contains("Interlocked.CompareExchange(ref autoDispatchStarted, 1, 0)", auto, StringComparison.Ordinal);
        Assert.Contains("runtime.ExecuteControlAsync(device.DeviceId, request, cancellationToken)", auto, StringComparison.Ordinal);
        Assert.Contains("InterlockCheck = true", auto, StringComparison.Ordinal);
        Assert.Contains("SynchroCheck = false", auto, StringComparison.Ordinal);
        Assert.Contains("TestMode = false", auto, StringComparison.Ordinal);
        Assert.Contains("retry=false", auto, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No CLOSE/toggle/restore command is allowed", auto, StringComparison.Ordinal);
        Assert.DoesNotContain("ValueText = \"Close\"", auto, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, CountOccurrences(auto, "runtime.ExecuteControlAsync("));
    }

    [Fact]
    public void Q0AutoA3_RechecksClosedStateAfterFinalA3ReadyBeforeDispatch()
    {
        var auto = Read("Services/DynamicReportQ0TargetLockedAutoA3CommissioningService.cs");

        var readyIntercept = auto.IndexOf("ReadyMarker", StringComparison.Ordinal);
        var dispatch = auto.IndexOf("DispatchOneShotOpenAsync", readyIntercept, StringComparison.Ordinal);
        var readyRecheck = auto.IndexOf("A3 READY recheck", dispatch, StringComparison.Ordinal);
        var execute = auto.IndexOf("runtime.ExecuteControlAsync", dispatch, StringComparison.Ordinal);

        Assert.True(readyIntercept >= 0);
        Assert.True(dispatch > readyIntercept);
        Assert.True(readyRecheck > dispatch);
        Assert.True(execute > readyRecheck);
    }

    [Fact]
    public void Q0AutoA3_TargetScopesRecoveryOnPrivateSignalClonesWithoutChangingIdentityEvidence()
    {
        var auto = Read("Services/DynamicReportQ0TargetLockedAutoA3CommissioningService.cs");
        var identity = Read("Services/DynamicReportQualificationIdentity.cs");

        Assert.Contains("MemberwiseClone", auto, StringComparison.Ordinal);
        Assert.Contains("CreateTargetScopedRecoveryModel", auto, StringComparison.Ordinal);
        Assert.Contains("backingField.SetValue(clone, string.Empty)", auto, StringComparison.Ordinal);
        Assert.Contains("statusSetter!.Invoke(clone, [string.Empty])", auto, StringComparison.Ordinal);
        Assert.Contains("scopedCommands.Length != 1", auto, StringComparison.Ordinal);
        Assert.Contains("DynamicReportQualificationIdentity.Build(device, recoverySignals)", auto, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlStatusReference", identity, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineLock_PreservesStrictShadowEvidenceAndAddsSeparateGuardedRuntimeBoundary()
    {
        var engineLock = Read("engines/ARIEC61850.lock.json");

        Assert.Contains("\"ref\": \"main\"", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("c899b05f18ba2bd4c82ebff6879e4748036e0d90", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sourcePullRequest\": 100", engineLock, StringComparison.Ordinal);
        Assert.Contains("PR #98", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report-vs-independent-MMS shadow evaluator", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #99", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paired report/poll quality evidence", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paired report/poll device timestamp evidence", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #100", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InformationReportProven", engineLock, StringComparison.Ordinal);
        Assert.Contains("does not call MarkProductionEligible", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductionEligible as a separate certification boundary", engineLock, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
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
