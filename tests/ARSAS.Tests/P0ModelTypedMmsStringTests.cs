using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0ModelTypedMmsStringTests
{
    [Theory]
    [InlineData("[IRV:vendor-valid", "VisString255", MmsDataKind.VisibleString)]
    [InlineData("{literal vendor text}", "visible-string", MmsDataKind.VisibleString)]
    [InlineData("[literal MMS text]", "MmsString128", MmsDataKind.MmsString)]
    public void VerifiedDecodedStringsWithBracketPrefixAreNotMistakenForMmsArrays(
        string scalar, string declaredType, MmsDataKind wireKind)
    {
        Assert.False(IedBrowserReadValuePolicy.TryAcceptDisplay(scalar, out _, out _));
        Assert.True(IedBrowserReadValuePolicy.TryAcceptVerifiedMmsString(
            scalar, declaredType, wireKind, out var accepted, out var reason));
        Assert.Equal(scalar, accepted);
        Assert.Equal("", reason);
    }

    [Theory]
    [InlineData("[unverified", "VisString255", null)]
    [InlineData("{not a string}", "Int32", MmsDataKind.VisibleString)]
    [InlineData("[wrong decoder]", "VisString255", MmsDataKind.MmsString)]
    [InlineData("[wrong primitive]", "VisString255", MmsDataKind.Array)]
    [InlineData("[unknown declaration]", "", MmsDataKind.VisibleString)]
    [InlineData("unknown(tag=0x83)", "VisString255", MmsDataKind.VisibleString)]
    [InlineData("Structure(2)", "VisString255", MmsDataKind.VisibleString)]
    [InlineData("", "VisString255", MmsDataKind.VisibleString)]
    public void UnverifiedArrayLikeAndUnsupportedResultsFailClosed(
        string scalar, string modelType, MmsDataKind? kind)
    {
        Assert.False(IedBrowserReadValuePolicy.TryAcceptVerifiedMmsString(
            scalar, modelType, kind, out _, out _));
    }

    [Fact]
    public void ScalarBoundaryRemainsBoundedDespiteWireProof()
    {
        Assert.False(IedBrowserReadValuePolicy.TryAcceptVerifiedMmsString(
            "[" + new string('x', 513), "VisString600",
            MmsDataKind.VisibleString, out _, out _));
    }

    [Fact]
    public void LiveLedgerRetainsTypedProofOnlyForCurrentReadAssociation()
    {
        const string reference = "ZIVAVR/LLN0.NamPlt.swRev";
        var ledger = new IedBrowserLiveValues();
        var epoch = ledger.BeginAssociation();
        Assert.False(ledger.PublishForAssociation(epoch, reference, "DC", "[IRV:firmware",
            "", "", "MMS Read"));
        Assert.False(ledger.PublishForAssociation(epoch, reference, "DC", "[IRV:firmware",
            "", "", "MMS Read", verifiedStringWireKind: MmsDataKind.VisibleString,
            declaredDataType: "Int32"));
        Assert.True(ledger.PublishForAssociation(epoch, reference, "DC", "[IRV:firmware",
            "", "", "MMS Read", verifiedStringWireKind: MmsDataKind.VisibleString,
            declaredDataType: "VisString255"));
        Assert.True(ledger.TryGet(reference, "DC", out var observed, preferReport: false));
        Assert.Equal("[IRV:firmware", observed.Value);
        Assert.Equal("MMS Read", observed.Source);
        ledger.BeginAssociation();
        Assert.False(ledger.PublishForAssociation(epoch, reference, "DC", "[IRV:old",
            "", "", "MMS Read", verifiedStringWireKind: MmsDataKind.VisibleString,
            declaredDataType: "VisString255"));
        Assert.False(ledger.TryGet(reference, "DC", out _, preferReport: false));
    }


    [Fact]
    public void ExactGvaDcRootCanProjectVendorTextStartingWithBracket()
    {
        const string root = "ZIVAVR/LLN0$DC0";
        var target = new IedBrowserReadTarget(
            "ZIVAVR/LLN0$DC$NamPlt$swRev", "DC", "VisString255");
        var gva = new MmsTypeSpecificationNode
        {
            MmsType = "structure",
            Children =
            [
                new MmsTypeSpecificationNode
                {
                    Name = "NamPlt", MmsType = "structure",
                    Children = [new MmsTypeSpecificationNode
                    {
                        Name = "swRev", MmsType = "visible-string"
                    }]
                }
            ]
        };
        var wire = MmsDataValue.Structure([
            MmsDataValue.Structure([MmsDataValue.VisibleString("[IRV:firmware")])
        ]);
        var result = Assert.Single(IedBrowserSgRootProjection.Project(
            root, gva, wire, [target]));
        Assert.Equal(target, result.Target);
        Assert.Equal("[IRV:firmware", result.Value);
        Assert.Equal(MmsDataKind.VisibleString, result.VerifiedStringWireKind);
        Assert.False(IedBrowserSgRootProjection.Project(root, gva, wire,
            [target with { FunctionalConstraint = "CF" }]).Any());
    }

    [Fact]
    public void ExactGvaDoParentCanProjectLiteralStringButRejectWrongModelType()
    {
        const string parent = "ZIVAVR/CSWI1$SE$VendorDO";
        var target = new IedBrowserReadTarget(
            "ZIVAVR/CSWI1$SE$VendorDO$setVal", "SE", "VisString64");
        var gva = new MmsTypeSpecificationNode
        {
            MmsType = "structure",
            Children = [new MmsTypeSpecificationNode
            {
                Name = "setVal", MmsType = "visible-string"
            }]
        };
        var wire = MmsDataValue.Structure([MmsDataValue.VisibleString("{safe literal")]);
        var result = Assert.Single(IedBrowserVerifiedDoProjection.Project(
            parent, gva, wire, [target]));
        Assert.Equal("{safe literal", result.Value);
        Assert.Equal(MmsDataKind.VisibleString, result.VerifiedStringWireKind);
        Assert.Empty(IedBrowserVerifiedDoProjection.Project(
            parent, gva, wire, [target with { DataType = "INT32" }]));
    }

    [Fact]
    public void ExactGvaParentPreservesFalseTypedBooleanWithoutInventingTrue()
    {
        const string parent = "SIEBCUCTRL/CSWI1$ST$SwAuth";
        var target = new IedBrowserReadTarget(
            "SIEBCUCTRL/CSWI1$ST$SwAuth$stVal", "ST", "Boolean");
        var gva = new MmsTypeSpecificationNode
        {
            MmsType = "structure",
            Children = [new MmsTypeSpecificationNode
            {
                Name = "stVal", MmsType = "boolean"
            }]
        };
        var wire = MmsDataValue.Structure([MmsDataValue.Boolean(false)]);
        var result = Assert.Single(IedBrowserVerifiedDoProjection.Project(
            parent, gva, wire, [target]));
        Assert.False(result.TypedBoolean);
        Assert.Null(result.VerifiedStringWireKind);
    }

    [Fact]
    public void StSeParentFlightReusesOnlyVerifiedScheduledSiblingsAndBoundedUnscheduledFanout()
    {
        var service = ReadRepoSource("Services/IedBrowserIsolatedReadService.cs");
        var flight = ReadRepoSource("Services/IedBrowserTypedDoReadFlight.cs");
        Assert.Contains("doReadFlight.PreviouslyVerified(target)", service);
        Assert.Contains("doReadFlight.DrainUnscheduledSiblings(target)", service);
        Assert.Contains("_scheduledKeys.Contains(item.Key)", flight);
        Assert.Contains("_drainedParents.Add(parent)", flight);
        Assert.Contains("_completed.TryGetValue(parent", flight);
        Assert.Contains("MaximumParentAttempts = 24", flight);
    }

    [Fact]
    public void ReadAllDeduplicatesVerifiedRefAndFcAcrossIndependentParentFlights()
    {
        var isolated = ReadRepoSource("Services/IedBrowserIsolatedReadService.cs");
        Assert.Contains("var emitted = new HashSet<string>(StringComparer.Ordinal)", isolated);
        Assert.Contains("IedBrowserLiveValues.CanonicalReference(target.Reference)", isolated);
        Assert.Contains("target.FunctionalConstraint.Trim().ToUpperInvariant()", isolated);
        Assert.Contains("if (!emitted.Add(Key(result.Target))) return;", isolated);
        Assert.Contains("if (emitted.Contains(Key(target)))", isolated);
        Assert.Contains("EmitVerified(verifiedSg)", isolated);
        Assert.Contains("EmitVerified(verifiedParent)", isolated);
        Assert.Contains("EmitVerified(previouslyVerified)", isolated);
        Assert.Equal(1, isolated.Split("successful++;").Length - 1);
    }

    [Fact]
    public void UnresolvedWireShapeExplainsDecoderKindWithoutLoggingVendorValues()
    {
        var primitive = IedBrowserReadWireShapeEvidence.Describe(
            MmsDataValue.Integer(-517832));
        Assert.Contains("wireKind=Integer", primitive);
        Assert.Contains("category=primitive", primitive);
        Assert.Contains("childCount=0", primitive);
        Assert.DoesNotContain("-517832", primitive);

        var composite = IedBrowserReadWireShapeEvidence.Describe(
            MmsDataValue.Structure([
                MmsDataValue.Integer(468123),
                MmsDataValue.Boolean(false)
            ]));
        Assert.Contains("wireKind=Structure", composite);
        Assert.Contains("category=composite", composite);
        Assert.Contains("childCount=2", composite);
        Assert.DoesNotContain("468123", composite);
        Assert.Equal("wire=null", IedBrowserReadWireShapeEvidence.Describe(null));

        var native = ReadRepoSource("Services/NativeIec61850Client.cs");
        var failure = ReadRepoSource("Models/IedBrowserRejectedScalarEvidence.cs");
        Assert.Contains("WireShapeEvidence = IedBrowserReadWireShapeEvidence.Describe(raw)", native);
        Assert.Contains("read.WireShapeEvidence", failure);
        Assert.DoesNotContain("LastReadResponseHex", failure);
    }

    private static string ReadRepoSource(string relativePath)
    {
        for (DirectoryInfo? root = new(AppContext.BaseDirectory);
             root is not null; root = root.Parent)
        {
            var path = Path.Combine(root.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relativePath);
    }

    [Fact]
    public void ReadDecodeProofMustFlowThroughIsolatedReaderAndBrowserIngestion()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MainWindow.IedBrowser.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.IedBrowser.cs"));
        var isolated = File.ReadAllText(Path.Combine(root.FullName,
            "Services", "IedBrowserIsolatedReadService.cs"));
        var native = File.ReadAllText(Path.Combine(root.FullName,
            "Services", "NativeIec61850Client.cs"));
        Assert.Contains("result.VerifiedStringWireKind", source);
        Assert.Contains("declaredDataType: result.Target.DataType", source);
        Assert.Contains("read?.VerifiedStringWireKind", isolated);
        Assert.Contains("raw.Value is string decoded", native);
        Assert.Contains("CanonicalReference(projected.ReadReference)", native);
        Assert.Contains("CanonicalReference(reference)", native);
        Assert.Contains("projected.VerifiedStringWireKind = raw.Kind", native);
    }
}
