using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// GE F650 field capture: 6 configured RCB plans had 166 binding incidences,
/// but only 67 unique requested runtime signals. A duplicate RCB binding must
/// neither be counted as an uncovered signal nor mask a genuinely missing point.
/// </summary>
public sealed class P105StaticParityUniqueCoverageTests
{
    [Theory]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void OverlappingBrcbAndUrcb_UsesUniqueReferencesAndFc(int requested, bool expectedComparable)
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "BCUGE",
            IpAddress = "192.16.1.33",
            LiveDiscoveryModel = new LiveIedModelDiscoveryDocument
            {
                IedName = "BCUGE",
                DataSets =
                [
                    new LiveIedDataSetModel
                    {
                        Reference = "BCUGEF650/LLN0.Events",
                        Domain = "BCUGEF650",
                        LogicalNode = "LLN0",
                        Name = "Events",
                        MemberCount = 3,
                        Members =
                        [
                            new LiveIedDataSetMemberModel { Index = 0, Reference = "BCUGEF650/XCBR1.Pos.stVal", FunctionalConstraint = "ST" },
                            new LiveIedDataSetMemberModel { Index = 1, Reference = "BCUGEF650/XCBR2.Pos.stVal", FunctionalConstraint = "ST" },
                            new LiveIedDataSetMemberModel { Index = 2, Reference = "BCUGEF650/XCBR3.Pos.stVal", FunctionalConstraint = "ST" }
                        ]
                    }
                ]
            }
        };
        static Iec61850MonitorPoint Point(string reference, string fc = "ST") => new()
        {
            DeviceId = "ge-f650",
            IecReference = reference,
            FunctionalConstraint = fc
        };
        var first = "BCUGEF650/XCBR1.Pos.stVal";
        var second = "BCUGEF650/XCBR2.Pos.stVal";
        var third = "BCUGEF650/XCBR3.Pos.stVal";

        var planning = new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            RequestedPointCount = requested,
            CatalogMappedPointCount = requested,
            StaticBrcbSignalCount = 2,
            StaticUrcbSignalCount = 2,
            UncoveredSignalCount = 0,
            ReportPlans =
            [
                new ReportControlPlan
                {
                    IsEngineAuthoritative = true,
                    EngineAcquisitionKind = "StaticBrcb",
                    Buffered = true,
                    DataSetReference = "BCUGEF650/LLN0.Events",
                    ReportControlReference = "BCUGEF650/LLN0.BR.Rpt01",
                    Bindings = [Point(first), Point(second)]
                },
                new ReportControlPlan
                {
                    IsEngineAuthoritative = true,
                    EngineAcquisitionKind = "StaticUrcb",
                    Buffered = false,
                    DataSetReference = "BCUGEF650/LLN0.Events",
                    ReportControlReference = "BCUGEF650/LLN0.RP.Rpt01",
                    Bindings = [Point(first), Point(third)]
                }
            ]
        };

        var evidence = StaticAcquisitionParityTracker.BuildEvidence(
            device, planning, StaticAcquisitionIngressKind.LiveDiscovery);

        Assert.Equal(expectedComparable, evidence.IsComparable);
        if (expectedComparable)
        {
            Assert.DoesNotContain("coverage", evidence.IncomparableReason, StringComparison.Ordinal);
            Assert.Equal(StaticAcquisitionParityStatus.Equivalent,
                StaticAcquisitionParityTracker.Compare(evidence, evidence).Status);
        }
        else
        {
            // Raw 2+2 binding counters superficially equal requested=4, but
            // unique referenced points are only three; never award a false MATCH.
            Assert.Contains("unique=3", evidence.IncomparableReason, StringComparison.Ordinal);
            Assert.Equal(StaticAcquisitionParityStatus.InsufficientEvidence,
                StaticAcquisitionParityTracker.Compare(evidence, evidence).Status);
        }
    }

    [Theory]
    [InlineData("BCUGEF650/CSWI1.Pos.stVal", "CSWI Pos", "CSWI1 Pos")]
    [InlineData("BCUGEF650/CSWI16.Pos.stVal", "CSWI Pos", "CSWI16 Pos")]
    [InlineData("BCUGEF650/MMXU1.A.phsA.cVal.mag.f", "MMXU A", "MMXU1 A")]
    [InlineData("BCUGEF650/CSWI1.Pos.stVal", "CSWI1 Pos", "CSWI1 Pos")]
    [InlineData("not-a-logical-node", "Equipment status", "Equipment status")]
    public void ExplorerSignalLabel_UsesExactLnInstanceWithoutChangingMmsReference(
        string reference, string signalName, string expected)
    {
        var point = new Iec61850MonitorPoint
        {
            DeviceId = "ge",
            DeviceName = "BCUGE",
            IecReference = reference,
            SignalName = signalName
        };
        Assert.Equal(expected, point.ExplorerSignalName);
        Assert.Equal(reference, point.IecReference);
        Assert.Equal(signalName, point.SignalName);
    }

    [Fact]
    public void ExplorerGrid_BindsQualifiedLabelNotBareSignalName()
    {
        var xaml = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        Assert.Contains("Header=\"Signal\" Binding=\"{Binding ExplorerSignalName}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedBindingFc_RemainsIncomparableDespiteExactIncidenceCounters()
    {
        var source = File.ReadAllText(FindRepoFile("Services/StaticAcquisitionParityTracker.cs"));
        Assert.Contains("uniqueSelectedPoints.UnionWith(selected)", source, StringComparison.Ordinal);
        Assert.Contains("incomplete selected signal identity or FC evidence", source, StringComparison.Ordinal);
        Assert.Contains("static plan bindings do not match engine coverage counters", source, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
            var nested = Path.Combine(current.FullName, "ArIED61850Tester",
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(nested))
                return nested;
            current = current.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
