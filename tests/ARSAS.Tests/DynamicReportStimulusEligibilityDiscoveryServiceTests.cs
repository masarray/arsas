using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using ArMms = AR.Iec61850.Mms;

namespace ARSAS.Tests;

public sealed class DynamicReportStimulusEligibilityDiscoveryServiceTests
{
    [Fact]
    public void BuildRankedCandidates_PrioritizesControlStatusAndBreakerPosition()
    {
        var directory = new ArMms.MmsIedModelDirectory(new[]
        {
            Point("LD0", "XCBR1", "Pos.stVal"),
            Point("LD0", "CSWI1", "Pos.stVal"),
            Point("LD0", "GGIO2", "CBOpnCmdRecv.stVal"),
            Point("LD0", "GGIO2", "ComFail.stVal"),
            Point("LD0", "MMXU1", "Health.stVal"),
            Point("LD0", "XCBR1", "Pos.q", fc: "ST")
        });
        var signals = new[]
        {
            new SignalDefinition
            {
                IsControlSignal = true,
                ControlStatusReference = "LD0/XCBR1.Pos.stVal"
            }
        };

        var ranked = DynamicReportStimulusEligibilityDiscoveryService.BuildRankedCandidates(
            directory,
            signals,
            "LD0/LLN0.RP.A_URCB01");

        Assert.NotEmpty(ranked);
        Assert.Equal("LD0/XCBR1.Pos.stVal", ranked[0].Point.UserReference);
        Assert.Contains("ControlStatusReference", ranked[0].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ranked, candidate => candidate.Point.DataObjectPath.Equals("Pos.q", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildRankedCandidates_UsesBoundedStatusSemanticsOnly()
    {
        var directory = new ArMms.MmsIedModelDirectory(new[]
        {
            Point("LD0", "GGIO2", "CBClsCmdRecv.stVal"),
            Point("LD0", "GGIO2", "CBOpnCmdRecv.stVal"),
            Point("LD0", "GGIO1", "SwRem.stVal"),
            Point("LD0", "XCBR1", "Pos.stVal"),
            Point("LD0", "XCBR1", "Pos.ctlVal", fc: "CO"),
            Point("LD0", "MMXU1", "A.phsA.cVal.mag.f", fc: "MX")
        });

        var ranked = DynamicReportStimulusEligibilityDiscoveryService.BuildRankedCandidates(
            directory,
            Array.Empty<SignalDefinition>(),
            "LD0/LLN0.RP.A_URCB01");

        Assert.Equal(4, ranked.Count);
        Assert.All(ranked, candidate => Assert.Equal("ST", candidate.Point.FunctionalConstraint));
        Assert.All(ranked, candidate => Assert.EndsWith("stVal", candidate.Point.DataObjectPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ClassifyObservation_DistinguishesPersistentAndMomentary()
    {
        var t0 = DateTimeOffset.Parse("2026-08-21T07:00:00Z");
        var persistent = new[]
        {
            Transition("LD0/XCBR1.Pos.stVal", "false", "true", t0)
        };
        var pulse = new[]
        {
            Transition("LD0/GGIO2.CBOpnCmdRecv.stVal", "false", "true", t0),
            Transition("LD0/GGIO2.CBOpnCmdRecv.stVal", "true", "false", t0.AddMilliseconds(80))
        };

        Assert.Equal(
            DynamicReportStimulusEligibilityKind.PersistentOrLatched,
            DynamicReportStimulusEligibilityDiscoveryService.ClassifyObservation("false", "true", persistent));
        Assert.Equal(
            DynamicReportStimulusEligibilityKind.MomentaryOrPulse,
            DynamicReportStimulusEligibilityDiscoveryService.ClassifyObservation("false", "false", pulse));
        Assert.Equal(
            DynamicReportStimulusEligibilityKind.None,
            DynamicReportStimulusEligibilityDiscoveryService.ClassifyObservation("false", "false", Array.Empty<DynamicReportStimulusEligibilityTransition>()));
    }

    [Fact]
    public void Source_IsReadOnlyAndOperatorMustWaitForReadyMarker()
    {
        var root = RepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "Services", "DynamicReportStimulusEligibilityDiscoveryService.cs"));
        var ui = File.ReadAllText(Path.Combine(root, "DynamicReportQualificationUiBehavior.cs"));

        Assert.Contains("G2.5-A2 READY — READ ONLY", service, StringComparison.Ordinal);
        Assert.Contains("MaximumFastLaneCandidates = 8", service, StringComparison.Ordinal);
        Assert.Contains("PostTransitionSettleWindow", service, StringComparison.Ordinal);
        Assert.Contains("ReadSingleVariableAsync", service, StringComparison.Ordinal);

        Assert.DoesNotContain("WriteReportAttributeAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteSingleVariableAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineNamedVariableListAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteNamedVariableListAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitor", service, StringComparison.Ordinal);
        Assert.DoesNotContain("TriggerGeneralInterrogation", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", service, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible", service, StringComparison.Ordinal);

        Assert.Contains("Key.E", ui, StringComparison.Ordinal);
        Assert.Contains("RunG25A2StimulusEligibilityAsync", ui, StringComparison.Ordinal);
        Assert.Contains("WAIT until the status explicitly shows 'G2.5-A2 READY — READ ONLY'", ui, StringComparison.Ordinal);
        Assert.Contains("ZERO RCB/DataSet mutation", ui, StringComparison.Ordinal);
    }

    private static ArMms.MmsFcResolvedPoint Point(string domain, string logicalNode, string dataObjectPath, string fc = "ST")
    {
        var mmsPath = dataObjectPath.Replace('.', '$');
        return new ArMms.MmsFcResolvedPoint
        {
            Domain = domain,
            LogicalNode = logicalNode,
            FunctionalConstraint = fc,
            DataObjectPath = dataObjectPath,
            MmsItemName = $"{logicalNode}${fc}${mmsPath}"
        };
    }

    private static DynamicReportStimulusEligibilityTransition Transition(
        string reference,
        string before,
        string after,
        DateTimeOffset at)
        => new()
        {
            Reference = reference,
            MmsReference = reference.Replace('.', '$'),
            BeforeValue = before,
            AfterValue = after,
            ObservedAtUtc = at
        };

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ArIED61850Tester.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("ARSAS repository root not found.");
    }
}
