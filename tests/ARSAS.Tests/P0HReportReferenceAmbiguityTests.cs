using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>
/// Prevents a Report parent alias from borrowing the first sibling phase,
/// or a canonical FC collision from inheriting insertion order.
/// </summary>
public sealed class P0HReportReferenceAmbiguityTests
{
    private static Iec61850MonitorPoint Point(string reference, string fc = "MX") => new()
    {
        DeviceId = "IED-1",
        IecReference = reference,
        FunctionalConstraint = fc
    };

    [Fact]
    public void StructuredParentCannotChooseFirstOfMultiplePhases()
    {
        var first = Point("IEDLD0/MMXU1.A.phsA.cVal.mag.f");
        var second = Point("IEDLD0/MMXU1.A.phsB.cVal.mag.f");
        Assert.Null(Iec61850MonitorRuntime.FindUniqueReportReferenceFallback(
            new[] { first, second }, "IEDLD0/MMXU1.A"));
        Assert.Null(Iec61850MonitorRuntime.FindUniqueReportReferenceFallback(
            new[] { second, first }, "IEDLD0/MMXU1.A"));
    }

    [Fact]
    public void OneSelectedPhaseDoesNotProveParentAggregateButExactFcAliasDoes()
    {
        var phaseA = Point("IEDLD0/MMXU1.A.phsA.cVal.mag.f");
        var unrelated = Point("IEDLD0/MMXU1.B.phsB.cVal.mag.f");
        // A three-phase parent cannot be reduced to a selected child, even
        // when that child is the only one currently monitored.
        Assert.Null(Iec61850MonitorRuntime.FindUniqueReportReferenceFallback(
            new[] { phaseA, unrelated }, "IEDLD0/MMXU1.A"));
        Assert.Same(phaseA, Iec61850MonitorRuntime.FindUniqueReportReferenceFallback(
            new[] { phaseA, unrelated }, "IEDLD0/MMXU1$MX$A$phsA$cVal$mag$f"));
    }

    [Fact]
    public void SameNamePrefixWithoutSegmentBoundaryDoesNotMatch()
    {
        var phaseA1 = Point("IEDLD0/MMXU1.A1.phsA.cVal.mag.f");
        Assert.Null(Iec61850MonitorRuntime.FindUniqueReportReferenceFallback(
            new[] { phaseA1 }, "IEDLD0/MMXU1.A"));
    }

    [Fact]
    public void CanonicalCollisionRejectsAllCandidatesAndCannotBeRecached()
    {
        var index = new Dictionary<string, Iec61850MonitorPoint>(
            StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var st = Point("IEDLD0/XCBR1.Pos.stVal", "ST");
        var sv = Point("IEDLD0/XCBR1.Pos.subVal", "SV");
        const string key = "iedld0/xcbr1.pos";
        Iec61850MonitorRuntime.AddReportReferenceCandidate(
            index, ambiguous, key, st);
        Assert.Same(st, index[key]);
        Iec61850MonitorRuntime.AddReportReferenceCandidate(
            index, ambiguous, key, sv);
        Assert.False(index.ContainsKey(key));
        Assert.Contains(key, ambiguous);
        Iec61850MonitorRuntime.AddReportReferenceCandidate(
            index, ambiguous, key, st);
        Assert.False(index.ContainsKey(key));
    }

    [Fact]
    public void RepeatOfSamePointKeepsStableAliasAndExactDifferentPointIndependent()
    {
        var index = new Dictionary<string, Iec61850MonitorPoint>(
            StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var point = Point("IEDLD0/XCBR1.Pos.stVal", "ST");
        Iec61850MonitorRuntime.AddReportReferenceCandidate(
            index, ambiguous, "iedld0/xcbr1.pos.stval", point);
        Iec61850MonitorRuntime.AddReportReferenceCandidate(
            index, ambiguous, "iedld0/xcbr1.pos.stval", point);
        Assert.Same(point, index["iedld0/xcbr1.pos.stval"]);
        Assert.Empty(ambiguous);
    }
}
