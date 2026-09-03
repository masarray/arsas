namespace ARSAS.Tests;

public sealed class G27NativePerIedFieldCapabilityRegressionTests
{
    [Fact]
    public void P17_NormalRuntimeRequiresProfilePlusSeparateNativeCleanupWitness()
    {
        var guarded = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");
        var store = Read("Services/DynamicReportNativeFieldCapabilityWitnessStore.cs");

        Assert.Contains("DynamicReportNativeFieldCapabilityWitnessStore", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportNativeFieldCapabilityPolicy.TryValidate", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportNativeFieldCapabilityEnvelopeBoundRuntimePlanner.Build", guarded, StringComparison.Ordinal);
        Assert.Contains("DataChange profile is present but general Dynamic RCB runtime remains withheld", guarded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ModelFingerprint", store, StringComparison.Ordinal);
        Assert.Contains("ProfileRevision", store, StringComparison.Ordinal);
        Assert.Contains("StableIdentityKey", store, StringComparison.Ordinal);
        Assert.Contains("Cannot persist an incomplete native dynamic field-capability witness", store, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P17_ExplicitBootstrapReusesExistingGuardedCommissioningLadder()
    {
        var bootstrap = Read("Services/DynamicReportPerIedFieldCapabilityBootstrapService.cs");
        var recovery = Read("Services/DynamicReportQualificationFreshRecoveryCommissioningService.cs");
        var persistence = Read("Services/DynamicReportNativeFieldCapabilityPersistenceService.cs");

        Assert.Contains("DynamicReportQualificationFreshRecoveryCommissioningService", bootstrap, StringComparison.Ordinal);
        Assert.Contains("DynamicReportQualificationCommissioningService", recovery, StringComparison.Ordinal);
        Assert.Contains("DynamicReportActivationCommissioningServiceV2", bootstrap, StringComparison.Ordinal);
        Assert.Contains("DynamicReportNativeFieldCapabilityPersistenceService", bootstrap, StringComparison.Ordinal);
        Assert.Contains("DynamicReportSpontaneousDataChangeCommissioningService", persistence, StringComparison.Ordinal);
        Assert.Contains("RecordRcbActivationProof", persistence, StringComparison.Ordinal);
        Assert.Contains("RecordInformationReportProof", persistence, StringComparison.Ordinal);
        Assert.Contains("MmsDynamicInformationReportKind.DataChange", persistence, StringComparison.Ordinal);
        Assert.Contains("GeneralInterrogationDisabled = true", persistence, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible(", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible(", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible(", persistence, StringComparison.Ordinal);
    }

    [Fact]
    public void P17_G23FreshRecoveryIsExactBoundedAndOneRetryOnly()
    {
        var recovery = Read("Services/DynamicReportQualificationFreshRecoveryCommissioningService.cs");

        Assert.Contains("RequiresFreshAssociation", recovery, StringComparison.Ordinal);
        Assert.Contains("RecoverDynamicDataSetQualificationResidueAsync", recovery, StringComparison.Ordinal);
        Assert.Contains("IsExactCurrentRunG23TemporaryDataSet", recovery, StringComparison.Ordinal);
        Assert.Contains("ARQ<8-hex>", recovery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exactly one new G2.3 commissioning run", recovery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("repeated physical mutation-instability evidence", recovery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("while (", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible(", recovery, StringComparison.Ordinal);
    }

    [Fact]
    public void P17_PersistenceBindsActualDchgAndAllCleanupGatesBeforeRuntimeAuthorization()
    {
        var persistence = Read("Services/DynamicReportNativeFieldCapabilityPersistenceService.cs");

        Assert.Contains("IncludedMemberReferences = dataChange.IncludedMemberReferences.ToArray()", persistence, StringComparison.Ordinal);
        Assert.Contains("RcbActivationEvidenceId = activationEvidenceId", persistence, StringComparison.Ordinal);
        Assert.Contains("InformationReportEvidenceId = reportEvidenceId", persistence, StringComparison.Ordinal);
        Assert.Contains("MonitorCleanupSucceeded = dataChange.MonitorCleanupSucceeded", persistence, StringComparison.Ordinal);
        Assert.Contains("ProofFieldRestoreSucceeded = dataChange.ProofFieldRestoreSucceeded", persistence, StringComparison.Ordinal);
        Assert.Contains("FreshCleanupClosureSucceeded = dataChange.FreshCleanupClosureSucceeded", persistence, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportNativeFieldCapabilityPolicy.TryValidate", persistence, StringComparison.Ordinal);
        Assert.Contains("Save profile first", persistence, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime remains fail-closed", persistence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P17_UiIsExplicitZeroControlAndRequiresReconnectBeforeNormalRuntime()
    {
        var ui = Read("DynamicReportPerIedBootstrapUiBehavior.cs");
        var app = Read("App.xaml.cs");

        Assert.Contains("e.Key != Key.B", ui, StringComparison.Ordinal);
        Assert.Contains("ZERO automatic control commands", ui, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Disconnect -> Connect -> Start Monitor", ui, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductionEligible remains OFF", ui, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DynamicReportPerIedBootstrapUiBehavior.Install()", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteControl", ui, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Send Command", ui, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P17_EngineLockPinsMergedFreshRecoveryEngine()
    {
        var engineLock = Read("engines/ARIEC61850.lock.json");

        Assert.Contains("d108eb5967960e697769b00f2dfe4f21c64688cd", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sourcePullRequest\": 108", engineLock, StringComparison.Ordinal);
        Assert.Contains("PR #107", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("native per-IED field-capability authorization", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fresh-association qualification residue recovery", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name match alone never authorizes delete", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProductionEligible remains independent", engineLock, StringComparison.OrdinalIgnoreCase);
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
