using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class CanonicalRuntimePointIdentityM213GTests
{
    private static SignalDefinition Signal(string reference, string fc) => new()
    {
        ObjectReference = reference, FunctionalConstraint = fc
    };

    [Fact]
    public void SameLegacyKeyWithDistinctIecCaseFailsClosedBeforeStateConstruction()
    {
        var upper = Signal("LD/CSWI1.Pos.stVal", "ST");
        var lower = Signal("LD/CSWI1.pos.stVal", "ST");
        Assert.NotEqual(
            IedBrowserLiveValues.CanonicalReference(upper.ObjectReference),
            IedBrowserLiveValues.CanonicalReference(lower.ObjectReference));
        Assert.Equal(
            Iec61850MonitorPoint.NormalizeReference(upper.ObjectReference),
            Iec61850MonitorPoint.NormalizeReference(lower.ObjectReference));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Iec61850MonitorRuntime.EnsureDistinctRuntimePointIdentities([upper, lower]));
        Assert.Contains("ambiguous IEC identities", ex.Message);
        Assert.Contains("[ST]", ex.Message);
    }

    [Fact]
    public void DifferentFcOnSameObjectCannotAliasOneRuntimePoint()
    {
        var first = Signal("LD/MMXU1.Hz.mag.f", "MX");
        var second = Signal("LD/MMXU1.Hz.mag.f", "CF");
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Iec61850MonitorRuntime.EnsureDistinctRuntimePointIdentities([first, second]));
        Assert.Contains("[MX]", ex.Message);
        Assert.Contains("[CF]", ex.Message);
    }

    [Fact]
    public void SameExactIdentityCanAppearTwiceWithoutFalseConflict()
    {
        Iec61850MonitorRuntime.EnsureDistinctRuntimePointIdentities([
            Signal("LD/MMXU1.Hz.mag.f", "mx"),
            Signal("LD/MMXU1.Hz.mag.f", "MX"),
            Signal("LD/MMXU1.A.phsA.cVal.mag.f", "MX"),
            Signal("LD/MMXU1.A.phsB.cVal.mag.f", "MX")
        ]);
    }

    [Fact]
    public void CollisionDetectionPrecedesDestructiveMonitorRestart()
    {
        var source = Source("Services/Iec61850MonitorRuntime.cs");
        var validate = source.IndexOf("EnsureDistinctRuntimePointIdentities(eligible);",
            StringComparison.Ordinal);
        var stop = source.IndexOf("await StopMonitoringSessionAsync(session, preservePointDefinitions: false)",
            StringComparison.Ordinal);
        Assert.True(validate >= 0 && stop > validate);
    }

    private static string Source(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var file = Path.Combine(directory.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
