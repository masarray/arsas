using AR.Iec61850.Discovery;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SchemaSafeAggregateProjectionRegressionTests
{
    [Fact]
    public void ThdA_ReadPlan_UsesExactNamedPhases_IndependentOfAttributeOrder()
    {
        const string parent = "IEDLD0/I_MHAI1.ThdA";
        var model = Model(DataObject(
            parent,
            "ThdA",
            // Deliberately scrambled with unrelated numeric leaves first.
            Attribute(parent + ".noise.f", "noise.f"),
            Attribute(parent + ".phsC.cVal.mag.f", "phsC.cVal.mag.f"),
            Attribute(parent + ".phsA.instCVal.mag.f", "phsA.instCVal.mag.f"),
            Attribute(parent + ".phsB.cVal.mag.f", "phsB.cVal.mag.f"),
            Attribute(parent + ".phsA.cVal.mag.f", "phsA.cVal.mag.f"),
            Attribute(parent + ".phsC.instCVal.mag.f", "phsC.instCVal.mag.f")));

        var ok = SchemaSafeAggregateProjectionService.TryBuildReadPlan(
            model,
            parent,
            out var plan,
            out var status);

        Assert.True(ok, status);
        Assert.Equal("ThreePhaseThd", plan.Kind);
        Assert.Equal(new[] { "A", "B", "C" }, plan.Leaves.Select(leaf => leaf.Label));
        Assert.Equal(
            new[]
            {
                parent + ".phsA.cVal.mag.f",
                parent + ".phsB.cVal.mag.f",
                parent + ".phsC.cVal.mag.f"
            },
            plan.Leaves.Select(leaf => leaf.Reference),
            StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(plan.Leaves, leaf => leaf.Reference.Contains("noise", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.Leaves, leaf => leaf.Reference.Contains("instCVal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ThdPPV_ReadPlan_UsesExactAB_BC_CA_Identities()
    {
        const string parent = "IEDLD0/V_MHAI1.ThdPPV";
        var model = Model(DataObject(
            parent,
            "ThdPPV",
            Attribute(parent + ".phsCA.cVal.mag.f", "phsCA.cVal.mag.f"),
            Attribute(parent + ".phsAB.cVal.mag.f", "phsAB.cVal.mag.f"),
            Attribute(parent + ".phsBC.cVal.mag.f", "phsBC.cVal.mag.f")));

        Assert.True(SchemaSafeAggregateProjectionService.TryBuildReadPlan(
            model,
            parent,
            out var plan,
            out var status), status);

        Assert.Equal(new[] { "AB", "BC", "CA" }, plan.Leaves.Select(leaf => leaf.Label));
        Assert.Equal(
            new[]
            {
                parent + ".phsAB.cVal.mag.f",
                parent + ".phsBC.cVal.mag.f",
                parent + ".phsCA.cVal.mag.f"
            },
            plan.Leaves.Select(leaf => leaf.Reference),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Thd_ReadPlan_MissingOneNamedPhase_FailsClosed()
    {
        const string parent = "IEDLD0/I_MHAI1.ThdA";
        var model = Model(DataObject(
            parent,
            "ThdA",
            Attribute(parent + ".phsA.cVal.mag.f", "phsA.cVal.mag.f"),
            Attribute(parent + ".phsC.cVal.mag.f", "phsC.cVal.mag.f"),
            Attribute(parent + ".someOtherFloat.f", "someOtherFloat.f")));

        var ok = SchemaSafeAggregateProjectionService.TryBuildReadPlan(
            model,
            parent,
            out var plan,
            out var status);

        Assert.False(ok);
        Assert.Empty(plan.Leaves);
        Assert.Contains("phase B", status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("none of the approved exact magnitude references", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DemandEnergy_ReadPlan_PrefersCanonicalMag_AndIgnoresOtherNumericLeaves()
    {
        const string parent = "IEDLD0/XPRE_MMTR1.DmdWhMV";
        var model = Model(DataObject(
            parent,
            "DmdWhMV",
            Attribute(parent + ".noise.f", "noise.f"),
            Attribute(parent + ".instMag.f", "instMag.f"),
            Attribute(parent + ".mag.f", "mag.f")));

        Assert.True(SchemaSafeAggregateProjectionService.TryBuildReadPlan(
            model,
            parent,
            out var plan,
            out var status), status);

        var leaf = Assert.Single(plan.Leaves);
        Assert.Equal("DemandEnergy", plan.Kind);
        Assert.Equal(parent + ".mag.f", leaf.Reference, ignoreCase: true);
    }

    [Fact]
    public void DemandEnergy_ReadPlan_UsesOneExactInstantFallback_WhenCanonicalIsAbsent()
    {
        const string parent = "IEDLD0/XPRE_MMTR1.DmdWhMV";
        var model = Model(DataObject(
            parent,
            "DmdWhMV",
            Attribute(parent + ".noise.f", "noise.f"),
            Attribute(parent + ".instMag.f", "instMag.f")));

        Assert.True(SchemaSafeAggregateProjectionService.TryBuildReadPlan(
            model,
            parent,
            out var plan,
            out var status), status);

        Assert.Equal(parent + ".instMag.f", Assert.Single(plan.Leaves).Reference, ignoreCase: true);
    }

    [Fact]
    public void DemandEnergy_ReadPlan_AmbiguousInstantFallback_FailsClosed()
    {
        const string parent = "IEDLD0/XPRE_MMTR1.DmdWhMV";
        var model = Model(DataObject(
            parent,
            "DmdWhMV",
            Attribute(parent + ".instMag.f", "instMag.f"),
            Attribute(parent + ".instCVal.mag.f", "instCVal.mag.f")));

        var ok = SchemaSafeAggregateProjectionService.TryBuildReadPlan(
            model,
            parent,
            out _,
            out var status);

        Assert.False(ok);
        Assert.Contains("ambiguous", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeResolver_InterceptsAggregateBeforeGenericParentRead()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IecSignalReadResolver.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var intercept = source.IndexOf("if (client is NativeIec61850Client native && IsSchemaSafeAggregate(signal.ObjectReference))", StringComparison.Ordinal);
        var generic = source.IndexOf("var references = BuildReadCandidates(signal.ObjectReference).ToList();", StringComparison.Ordinal);

        Assert.True(intercept >= 0, "Aggregate values must have an explicit schema-safe runtime interception path.");
        Assert.True(generic > intercept, "Aggregate interception must occur before generic parent/alternate read candidates are built.");
        Assert.Contains("TryBuildSchemaSafeAggregateReadPlan", source, StringComparison.Ordinal);
        Assert.Contains("schema-safe-three-phase-exact-leaf-reads", source, StringComparison.Ordinal);
        Assert.Contains("schema-safe-demand-energy-exact-leaf-read", source, StringComparison.Ordinal);

        var helperStart = source.IndexOf("private static async Task<ResolvedIecSignalRead?> ReadSchemaSafeAggregateAsync", StringComparison.Ordinal);
        var helperEnd = source.IndexOf("private static bool IsSchemaSafeAggregate", helperStart, StringComparison.Ordinal);
        Assert.True(helperStart >= 0 && helperEnd > helperStart);
        var helper = source[helperStart..helperEnd];
        Assert.DoesNotContain("ReadValueAsync(signal.ObjectReference", helper, StringComparison.Ordinal);
        Assert.Contains("ReadValueAsync(\n                leaf.Reference", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaSafeService_HasNoPositionalOrFirstNumericAggregateSelection()
    {
        var source = File.ReadAllText(FindRepoFile("Services/SchemaSafeAggregateProjectionService.cs"));

        Assert.DoesNotContain("Children.Take(3)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FindFirstFloating", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FindFirstInteger", source, StringComparison.Ordinal);
        Assert.Contains("TryResolvePreferredAttributeReference", source, StringComparison.Ordinal);
    }

    private static LiveIedModelDiscoveryDocument Model(params LiveIedDataObjectModel[] objects)
        => new()
        {
            Source = "SclWorkspace regression",
            IedName = "IED",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD0",
                    Inst = "LD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MHAI1",
                            LnClass = "MHAI",
                            LnInst = "1",
                            DataObjects = objects
                        }
                    ]
                }
            ]
        };

    private static LiveIedDataObjectModel DataObject(
        string reference,
        string name,
        params LiveIedDataAttributeModel[] attributes)
        => new()
        {
            Reference = reference,
            Name = name,
            InferredCdc = "MV",
            Attributes = attributes
        };

    private static LiveIedDataAttributeModel Attribute(string reference, string path)
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = "MX",
            MmsReference = reference.Replace('.', '$'),
            SclBType = "FLOAT32",
            MmsType = "floating-point",
            Source = "SCL.DataTypeTemplates",
            TypeSource = "SCL.DataTypeTemplates",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

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
