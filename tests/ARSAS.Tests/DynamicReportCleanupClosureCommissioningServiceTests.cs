using ArIED61850Tester.Services;
using ArMms = AR.Iec61850.Mms;

namespace ARSAS.Tests;

public sealed class DynamicReportCleanupClosureCommissioningServiceTests
{
    [Fact]
    public void FreshClosure_AcceptsFullyReleasedUrcbAndAbsentTemporaryDataSet()
    {
        var snapshot = Snapshot();

        var success = DynamicReportCleanupClosureCommissioningService.IsFreshCleanupClosed(
            snapshot,
            temporaryDataSetAbsentFromNameList: true,
            temporaryDataSetDirectoryAbsent: true,
            associationHealthy: true,
            out var reason);

        Assert.True(success, reason);
        Assert.Contains("DatSet empty", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Resv=false", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Owner empty", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshClosure_RejectsReservationStillHeld()
    {
        var snapshot = Snapshot(reservationState: "true");

        var success = DynamicReportCleanupClosureCommissioningService.IsFreshCleanupClosed(
            snapshot, true, true, true, out var reason);

        Assert.False(success);
        Assert.Contains("reservation release is not explicit", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshClosure_RejectsOwnerStillPresent()
    {
        var snapshot = Snapshot(owner: "C0A851F0");

        var success = DynamicReportCleanupClosureCommissioningService.IsFreshCleanupClosed(
            snapshot, true, true, true, out var reason);

        Assert.False(success);
        Assert.Contains("Owner is still non-empty", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshClosure_RejectsRptEnaStillTrue()
    {
        var snapshot = Snapshot(enabledState: "true");

        var success = DynamicReportCleanupClosureCommissioningService.IsFreshCleanupClosed(
            snapshot, true, true, true, out var reason);

        Assert.False(success);
        Assert.Contains("RptEna is not explicit false", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshClosure_RejectsResidualRcbDataSetBinding()
    {
        var snapshot = Snapshot(dataSetReference: "LD0/LLN0.AR_G24_LEFTOVER");

        var success = DynamicReportCleanupClosureCommissioningService.IsFreshCleanupClosed(
            snapshot, true, true, true, out var reason);

        Assert.False(success);
        Assert.Contains("DatSet is not positively proven empty", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshClosure_RejectsTemporaryDataSetStillAdvertised()
    {
        var success = DynamicReportCleanupClosureCommissioningService.IsFreshCleanupClosed(
            Snapshot(), false, true, true, out var reason);

        Assert.False(success);
        Assert.Contains("still advertised", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshClosure_RejectsTemporaryDataSetDirectoryStillReadable()
    {
        var success = DynamicReportCleanupClosureCommissioningService.IsFreshCleanupClosed(
            Snapshot(), true, false, true, out var reason);

        Assert.False(success);
        Assert.Contains("still has a readable directory", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FreshClosure_RejectsAssociationFault()
    {
        var success = DynamicReportCleanupClosureCommissioningService.IsFreshCleanupClosed(
            Snapshot(), true, true, false, out var reason);

        Assert.False(success);
        Assert.Contains("association is not healthy", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TemporaryDataSetNameList_RecognizesIecAndMmsForms()
    {
        var snapshot = new ArMms.MmsDiscoverySnapshot
        {
            DomainVariableLists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["LD0"] = ["LLN0$STATIC", "LLN0$OTHER"]
            }
        };

        var absent = DynamicReportCleanupClosureCommissioningService.IsTemporaryDataSetAbsentFromNameList(
            snapshot,
            "LD0/LLN0.AR_G24_BFD53D73",
            out var reason);

        Assert.True(absent, reason);

        snapshot = new ArMms.MmsDiscoverySnapshot
        {
            DomainVariableLists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["LD0"] = ["LLN0$AR_G24_BFD53D73"]
            }
        };

        absent = DynamicReportCleanupClosureCommissioningService.IsTemporaryDataSetAbsentFromNameList(
            snapshot,
            "LD0/LLN0.AR_G24_BFD53D73",
            out reason);

        Assert.False(absent);
        Assert.Contains("still advertised", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void G24C_SourceBoundary_IsReadOnlyAndDoesNotAdvanceProfile()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(),
            "Services",
            "DynamicReportCleanupClosureCommissioningService.cs"));

        Assert.Contains("fresh association", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CheckReportControlAvailabilityAsync", source, StringComparison.Ordinal);
        Assert.Contains("GetDataSetDirectoryAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteReportAttributeAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteSingleVariableAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrepareDynamicRcbCommissioningFieldsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProbeDynamicRcbTriggerOptionsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProbeDynamicRcbOptionalFieldsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineNamedVariableListAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteNamedVariableListAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void G24C_UiUsesDedicatedReadOnlyShortcut()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "DynamicReportQualificationUiBehavior.cs"));

        Assert.Contains("e.Key != Key.C", source, StringComparison.Ordinal);
        Assert.Contains("RunG24CleanupClosureAsync", source, StringComparison.Ordinal);
        Assert.Contains("READ-ONLY PHYSICAL MERGE GATE", source, StringComparison.Ordinal);
        Assert.Contains("ZERO MMS writes", source, StringComparison.Ordinal);
    }

    private static ArMms.MmsRcbAvailabilitySnapshot Snapshot(
        string dataSetReference = "",
        string enabledState = "false",
        string reservationState = "false",
        string owner = "")
        => new()
        {
            Reference = "LD0/LLN0.RP01",
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = "RP01",
            Buffered = false,
            DataSetReference = dataSetReference,
            DataSetProbeState = ArMms.MmsRcbDataSetProbeState.ReadSucceeded,
            DataSetProbeMessage = "DatSet item: OK",
            ReportId = "RPT-01",
            TriggerOptions = "0204",
            OptionalFields = "060000",
            EnabledState = enabledState,
            ReservationState = reservationState,
            ReservationTimeSeconds = "0",
            Owner = owner,
            Availability = ArMms.MmsRcbOperationalAvailability.NoDataSet,
            Confidence = ArMms.MmsRcbAvailabilityConfidence.Exact,
            Attributes = ["DatSet", "RptID", "RptEna", "Resv", "TrgOps", "OptFlds", "GI"]
        };

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ArIED61850Tester.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("ARSAS repository root not found.");
    }
}
