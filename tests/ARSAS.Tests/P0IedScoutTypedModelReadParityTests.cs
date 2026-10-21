using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Synthetic structural tests only. No vendor values are inferred from the
/// screenshots; accepted scalars must come from matching wire GVA + Read.
/// </summary>
public sealed class P0IedScoutTypedModelReadParityTests
{
    [Theory]
    [InlineData("SIEBCUCTRL/CSWI1$SE$BlkByProt$setVal", "SE",
        "SIEBCUCTRL/CSWI1$SE$BlkByProt")]
    [InlineData("SIEBCUCTRL/CSWI1$ST$SwAuth$stVal", "ST",
        "SIEBCUCTRL/CSWI1$ST$SwAuth")]
    [InlineData("SIEBCUCTRL/CSWI1.SwMod.stVal", "ST",
        "SIEBCUCTRL/CSWI1$ST$SwMod")]
    public void ExactTypedDoParent_UsesOnlySelectedDoAndFc(
        string reference, string fc, string expected)
    {
        var target = new IedBrowserReadTarget(reference, fc, "INT32");
        Assert.Equal(expected, IedBrowserVerifiedDoProjection.ParentFor(target));
    }

    [Theory]
    [InlineData("SIEBCUCTRL/CSWI1$SE$SwAuth$stVal", "ST")]
    [InlineData("SIEBCUCTRL/CSWI1$ST$SwAuth$stVal", "SE")]
    [InlineData("SIEBCUCTRL/CSWI1$SE$SwAuth$stVal", "CF")]
    [InlineData("SIEBCUCTRL/CSWI1$ST$SwAuth", "ST")]
    public void MismatchedOrNonLeafIdentity_IsNotRereadAsAnotherFc(
        string reference, string fc)
        => Assert.Equal("", IedBrowserVerifiedDoProjection.ParentFor(
            new IedBrowserReadTarget(reference, fc, "INT32")));

    [Fact]
    public void TypedParent_ProjectsOnlyExactBoundChildrenWithOrderedGva()
    {
        var parent = "SIEBCUCTRL/CSWI1$ST$SwAuth";
        var target = new IedBrowserReadTarget(
            "SIEBCUCTRL/CSWI1$ST$SwAuth$stVal", "ST", "INT32");
        var neighbor = new IedBrowserReadTarget(
            "SIEBCUCTRL/CSWI1$ST$SwMod$stVal", "ST", "INT32");
        var q = new IedBrowserReadTarget(
            "SIEBCUCTRL/CSWI1$ST$SwAuth$q", "ST", "Check");
        var type = Structure(
            Field("stVal", "integer"),
            Field("q", "bit-string"));
        var data = MmsDataValue.Structure(new[]
        {
            MmsDataValue.Integer(2),
            MmsDataValue.BitString(new byte[] { 0, 0 })
        });
        // No guessing: the raw decoded integer may render as an ordinal.
        var observed = IedBrowserVerifiedDoProjection.Project(
            parent, type, data, new[] { target, q, neighbor });
        Assert.Contains(observed, x => x.Target == target);
        Assert.DoesNotContain(observed, x => x.Target == neighbor);
        Assert.All(observed, x => Assert.True(
            IedBrowserReadValuePolicy.TryAcceptDisplay(x.Value, out _, out _)));
    }

    [Fact]
    public void MismatchedGvaTypeOrWireCardinality_FailsClosed()
    {
        var parent = "SIEBCUCTRL/CSWI1$SE$BlkByProt";
        var leaf = new IedBrowserReadTarget(
            "SIEBCUCTRL/CSWI1$SE$BlkByProt$setVal", "SE", "INT32");
        var type = Structure(Field("setVal", "integer"));
        var wrongType = MmsDataValue.Structure(new[] { MmsDataValue.Boolean(true) });
        Assert.Empty(IedBrowserVerifiedDoProjection.Project(
            parent, type, wrongType, new[] { leaf }));
        var wrongLength = MmsDataValue.Structure(new[]
        {
            MmsDataValue.Integer(1), MmsDataValue.Integer(2)
        });
        Assert.Empty(IedBrowserVerifiedDoProjection.Project(
            parent, type, wrongLength, new[] { leaf }));
        Assert.Empty(IedBrowserVerifiedDoProjection.Project(
            parent, type, MmsDataValue.Structure(new[] {
                MmsDataValue.Integer(1) }),
            new[] { leaf with { FunctionalConstraint = "SG" } }));
    }

    [Fact]
    public void SeRootGroupRead_UsesExactFCAndNeverBorrowsSg()
    {
        var se = new IedBrowserReadTarget(
            "SIEBCUCTRL/CSWI1$SE$NomEqActl$setVal", "SE", "INT32");
        var sg = se with
        {
            Reference = "SIEBCUCTRL/CSWI1$SG$NomEqActl$setVal",
            FunctionalConstraint = "SG"
        };
        Assert.Equal("SIEBCUCTRL/CSWI1$SE0",
            IedBrowserSgRootProjection.RootFor(se));
        Assert.Equal("SIEBCUCTRL/CSWI1$SG0",
            IedBrowserSgRootProjection.RootFor(sg));
    }

    [Fact]
    public void BrowserRepairsOnlyPreviouslyUnresolvedLeafAndCachesParentOnce()
    {
        var isolated = Source("Services/IedBrowserIsolatedReadService.cs");
        var flight = Source("Services/IedBrowserTypedDoReadFlight.cs");
        var native = Source("Services/NativeIec61850Client.TypedDoRead.cs");
        Assert.Contains("else\n                    {\n                        // Some IEDs",
            isolated.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("doReadFlight.TryReadAsync(", isolated, StringComparison.Ordinal);
        Assert.Contains("MaximumParentAttempts = 24", flight, StringComparison.Ordinal);
        Assert.Contains("_completed.Add(parent, projected)", flight, StringComparison.Ordinal);
        Assert.Contains("GetVariableAccessAttributesAsync(typeRoot", native,
            StringComparison.Ordinal);
        Assert.Contains("ReadSingleVariableAsync(typeRoot", native,
            StringComparison.Ordinal);
        Assert.DoesNotContain("WriteControlVariableAsync(", native,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CommandIsFinallyRejectedAtNativeAssociationGateInsteadOfQueueing()
    {
        var native = Source("Services/NativeIec61850Client.cs");
        var start = native.IndexOf("public async Task<Iec61850ControlCommandResult> ExecuteControlAsync(",
            StringComparison.Ordinal);
        var end = native.IndexOf("private async Task<Iec61850ControlCommandResult> ExecuteControlCoreAsync(",
            start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var handler = native[start..end];
        Assert.Contains("_controlCommandGate.WaitAsync(0, cancellationToken)", handler,
            StringComparison.Ordinal);
        Assert.Contains("command NOT SENT", handler, StringComparison.Ordinal);
        Assert.Contains("_controlCommandGate.Release()", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("_controlCommandGate.WaitAsync(cancellationToken)",
            handler, StringComparison.Ordinal);
    }

    private static MmsTypeSpecificationNode Field(string name, string kind)
        => new() { Name = name, MmsType = kind };
    private static MmsTypeSpecificationNode Structure(
        params MmsTypeSpecificationNode[] children)
        => new() { MmsType = "structure", Children = children };

    private static string Source(string path)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null)
        {
            var file = Path.Combine(root.FullName,
                path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file)) return File.ReadAllText(file);
            root = root.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
