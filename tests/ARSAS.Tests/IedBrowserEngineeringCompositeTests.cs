using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class IedBrowserEngineeringCompositeTests
{
    private static readonly string Owner = "LD/MMXU1.PhV";

    private static LiveIedDataAttributeModel Attribute(string phase, string fc, string tail)
        => new()
        {
            ObjectReference = Owner,
            AttributePath = "PhV." + phase + "." + tail,
            MmsReference = "LD/MMXU1$" + fc + "$PhV$" + phase + "$" +
                tail.Replace('.', '$'),
            FunctionalConstraint = fc
        };

    private static LiveIedDataObjectModel Object(bool angles = true)
    {
        var attributes = new List<LiveIedDataAttributeModel>();
        foreach (var phase in new[] { "phsA", "phsB", "phsC" })
        {
            attributes.Add(Attribute(phase, "MX", "cVal.mag.f"));
            if (angles) attributes.Add(Attribute(phase, "MX", "cVal.ang.f"));
        }
        return new LiveIedDataObjectModel
        {
            Name = "PhV", Reference = Owner, Attributes = attributes
        };
    }

    [Fact]
    public void ThreePhases_ActualZeroMagnitudesAndAnglesComposeWithoutGuessing()
    {
        var obj = Object();
        var root = IedBrowserStructuredDetails.DataObject(obj, "MX");
        Assert.Equal(3, root.PhasePlans.Count);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        foreach (var plan in root.PhasePlans)
        {
            Assert.True(ledger.PublishForAssociation(association,
                plan.MagnitudeReference, "MX", "0", "", "t0", "Report"));
            Assert.True(ledger.PublishForAssociation(association,
                plan.AngleReference, "MX", "0", "", "t0", "Report"));
        }
        var units = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var plan in root.PhasePlans)
        {
            units[plan.MagnitudeReference + "|MX"] = "kV";
            units[plan.AngleReference + "|MX"] = "deg";
        }
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            root, ledger, units, true, out var result));
        Assert.Equal("phsA: 0 kV ∠ 0°; phsB: 0 kV ∠ 0°; phsC: 0 kV ∠ 0°", result.Text);
        Assert.Contains(root.PhasePlans[2].AngleReference, result.Provenance);
        Assert.False(result.IsStale);
    }

    [Fact]
    public void MissingAngleShowsMagnitudeOnlyAndNeverFabricatesZeroDegrees()
    {
        var obj = Object(angles: false);
        var row = IedBrowserStructuredDetails.DataObject(obj, "MX");
        Assert.Equal(3, row.PhasePlans.Count);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        foreach (var plan in row.PhasePlans)
            ledger.PublishForAssociation(association,
                plan.MagnitudeReference, "MX", "0", "", "", "MMS Read");
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), false, out var result));
        Assert.Contains("angle not verified", result.Text);
        Assert.DoesNotContain("°", result.Text);
        Assert.DoesNotContain("kV", result.Text);
    }

    [Fact]
    public void MissingPhaseShowsExplicitPartialVector_LeavesRemainIndependentlyReadable()
    {
        var row = IedBrowserStructuredDetails.DataObject(Object(), "MX");
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        ledger.PublishForAssociation(association,
            row.PhasePlans[0].MagnitudeReference, "MX", "42", "", "", "Report");
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), true, out var summary));
        Assert.StartsWith("Partial · phsA: 42 (angle not verified); phsB: not observed;", summary.Text);
        Assert.Contains("phsC: not observed", summary.Text);
        Assert.DoesNotContain("∠ 0", summary.Text);
        Assert.True(ledger.TryGet(row.PhasePlans[0].MagnitudeReference, "MX",
            out var actual));
        Assert.Equal("42", actual.Value);
    }

    [Fact]
    public void WrongFcAndConflictingAngleTimestampFailClosed()
    {
        var obj = Object();
        var phase = IedBrowserEngineeringComposite.BuildPolar(
            obj.Attributes, Owner + ".phsA")!;
        Assert.Equal("MX", phase.FunctionalConstraint);
        var row = new IedBrowserDetailRow("Data", "phsA")
        {
            PolarPlan = phase, FunctionalConstraint = "MX"
        };
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        ledger.PublishForAssociation(association,
            phase.MagnitudeReference, "ST", "123", "", "", "Report");
        Assert.False(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), true, out _));
        ledger.PublishForAssociation(association,
            phase.MagnitudeReference, "MX", "12", "", "timestamp1", "Report");
        ledger.PublishForAssociation(association,
            phase.AngleReference, "MX", "33", "", "timestamp2", "Report");
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), true, out var output));
        Assert.Equal("12 (angle not verified)", output.Text);
        Assert.DoesNotContain("33", output.Text);
    }

    [Fact]
    public void ModelAndDataSetRowsShareSameExactTriplet()
    {
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    DataObjects = [Object()]
                }]
            }]
        };
        var member = new LiveIedDataSetMemberModel
        {
            Reference = Owner, FunctionalConstraint = "MX"
        };
        var row = IedBrowserStructuredDetails.Member(member, model);
        Assert.Equal(3, row.PhasePlans.Count);
        Assert.Equal(
            IedBrowserStructuredDetails.DataObject(Object(), "MX").PhasePlans,
            row.PhasePlans);
    }
    [Fact]
    public void PpvLineToLineThreePhasePairsFollowDeclaredAbBcCaOrder()
    {
        const string owner = "LD/MMXU1.PPV";
        var names = new[] { "phsAB", "phsBC", "phsCA" };
        var attributes = names.SelectMany(phase => new[] { "cVal.mag.f", "cVal.ang.f" }
            .Select(tail => new LiveIedDataAttributeModel
            {
                ObjectReference = owner, AttributePath = "PPV." + phase + "." + tail,
                MmsReference = "LD/MMXU1$MX$PPV$" + phase + "$" +
                    tail.Replace('.', '$'),
                FunctionalConstraint = "MX"
            })).ToArray();
        var obj = new LiveIedDataObjectModel
        {
            Name = "PPV", Reference = owner, Attributes = attributes
        };
        var root = IedBrowserStructuredDetails.DataObject(obj, "MX");
        Assert.Equal(names, root.PhasePlans.Select(p => p.Phase).ToArray());
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        foreach (var (plan, index) in root.PhasePlans.Select((p, index) => (p, index)))
        {
            ledger.PublishForAssociation(association, plan.MagnitudeReference,
                "MX", (index + 1).ToString(), "", "t0", "Report");
            ledger.PublishForAssociation(association, plan.AngleReference,
                "MX", "30", "", "t0", "Report");
        }
        var units = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var plan in root.PhasePlans)
        {
            units[plan.MagnitudeReference + "|MX"] = "kV";
            units[plan.AngleReference + "|MX"] = "deg";
        }
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            root, ledger, units, true, out var result));
        Assert.Equal("phsAB: 1 kV ∠ 30°; phsBC: 2 kV ∠ 30°; phsCA: 3 kV ∠ 30°",
            result.Text);
    }

    [Fact]
    public void PpvAndWyeFamiliesCannotBeMixedIntoAFalseSingleThreePhaseVector()
    {
        const string owner = "LD/MMXU1.PPV";
        LiveIedDataAttributeModel Leaf(string name) => new()
        {
            ObjectReference = owner,
            MmsReference = "LD/MMXU1$MX$PPV$" + name + "$cVal$mag$f",
            FunctionalConstraint = "MX"
        };
        var mixed = new[] { Leaf("phsA"), Leaf("phsB"), Leaf("phsCA") };
        Assert.Empty(IedBrowserEngineeringComposite.BuildThreePhases(mixed, owner));
    }

    [Theory]
    [InlineData("TotW")]
    [InlineData("TotVAr")]
    [InlineData("TotVA")]
    [InlineData("Hz")]
    public void ScalarMvNeverGainsAngularSemantics_FromMagnitudeOnly(string name)
    {
        var owner = "LD/MMXU1." + name;
        var leaf = new LiveIedDataAttributeModel
        {
            ObjectReference = owner, AttributePath = name + ".mag.f",
            FunctionalConstraint = "MX",
            MmsReference = "LD/MMXU1$MX$" + name + "$mag$f"
        };
        var model = new LiveIedDataObjectModel
        {
            Reference = owner, Name = name, InferredCdc = "MV",
            Attributes = [leaf]
        };
        var row = IedBrowserStructuredDetails.DataObject(model, "MX");
        Assert.Null(row.PolarPlan);
        Assert.Empty(row.PhasePlans);
        Assert.Empty(row.VectorComponents);
        Assert.Equal(IedBrowserLiveValues.CanonicalReference(leaf.MmsReference),
            IedBrowserLiveValues.CanonicalReference(row.ValueReference));
        Assert.All(row.GetChildren(), child =>
        {
            Assert.Null(child.PolarPlan);
            Assert.Empty(child.PhasePlans);
            Assert.Empty(child.VectorComponents);
        });
        Assert.Null(IedBrowserEngineeringComposite.BuildPolar(model.Attributes, owner));

        var ledger = new IedBrowserLiveValues();
        var generation = ledger.BeginAssociation();
        ledger.PublishForAssociation(generation, leaf.MmsReference,
            "MX", "50", "", "", "MMS Read");
        Assert.False(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), false, out _));
        Assert.True(ledger.TryGet(row.ValueReference, "MX", out var observed));
        Assert.Equal("50", observed.Value);
    }

    [Fact]
    public void DeclaredScalarMvDoesNotBecomePhasor_EvenWithVendorAngleSibling()
    {
        const string owner = "LD/MMXU1.Hz";
        LiveIedDataAttributeModel Attr(string field) => new()
        {
            ObjectReference = owner,
            AttributePath = "Hz." + field,
            FunctionalConstraint = "MX",
            MmsReference = "LD/MMXU1$MX$Hz$" + field.Replace('.', '$')
        };
        var model = new LiveIedDataObjectModel
        {
            Reference = owner, Name = "Hz", InferredCdc = "MV",
            Attributes = [Attr("mag.f"), Attr("ang.f")]
        };
        var rows = IedBrowserStructuredDetails.DataObject(model, "MX");
        Assert.Null(rows.PolarPlan);
        Assert.Empty(rows.PhasePlans);
        Assert.Empty(rows.VectorComponents);
        var projection = new IedBrowserDetailProjection();
        projection.Reset(rows.GetChildren());
        Assert.All(projection.Rows, r =>
        {
            Assert.Null(r.PolarPlan);
            Assert.Empty(r.PhasePlans);
            Assert.Empty(r.VectorComponents);
        });
        Assert.Equal("LD/MMXU1.Hz.mag.f",
            IedBrowserLiveValues.CanonicalReference(rows.ValueReference));
    }

    [Fact]
    public void CompleteVectorShapeStillRendersDeclaredAngle()
    {
        var vector = Object();
        var phase = IedBrowserEngineeringComposite.BuildPolar(
            vector.Attributes, Owner + ".phsA");
        Assert.NotNull(phase);
        Assert.EndsWith(".cVal.ang.f", phase!.AngleReference);
        // This is a legitimate complex measurement; scalar restrictions must
        // not suppress phase components or valid three-phase groups.
        var root = IedBrowserStructuredDetails.DataObject(vector, "MX");
        Assert.Equal(3, root.PhasePlans.Count);
    }

}
