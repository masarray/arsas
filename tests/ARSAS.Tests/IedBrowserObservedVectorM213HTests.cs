using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// M2.13H — model-proven vector aggregates: no borrowed phase, guessed units,
/// fictitious zero, stale association or scalar overwrite.
/// </summary>
public sealed class IedBrowserObservedVectorM213HTests
{
    private const string Owner = "LD/MMXU1.PhV";

    private static LiveIedDataAttributeModel Attribute(string tail, string fc = "MX")
        => new()
        {
            ObjectReference = Owner,
            AttributePath = "PhV." + tail,
            FunctionalConstraint = fc,
            MmsReference = "LD/MMXU1$" + fc + "$PhV$" + tail.Replace('.', '$')
        };

    private static LiveIedDataObjectModel VectorModel()
        => new()
        {
            Name = "PhV", Reference = Owner,
            Attributes =
            [
                Attribute("phsA.cVal.mag.f"), Attribute("phsA.cVal.ang.f"),
                Attribute("phsB.cVal.mag.f"), Attribute("phsB.cVal.ang.f"),
                Attribute("phsC.cVal.mag.f"), Attribute("phsC.cVal.ang.f"),
                Attribute("phsC.q"), Attribute("phsC.t"),
                Attribute("phsC.instCVal.mag.f"),
                Attribute("neut.cVal.mag.f"), Attribute("neut.cVal.ang.f"),
                Attribute("phsC.units.SIUnit", "CF"),
                Attribute("phsC.units.multiplier", "CF")
            ]
        };

    private static IReadOnlyDictionary<string, string?> Units(params SignalDefinition[] signals)
        => IedBrowserEngineeringValuePresentation.BuildUnitIndex(signals);

    private static SignalDefinition Unit(string reference, string unit, string fc = "MX")
        => new() { ObjectReference = reference, FunctionalConstraint = fc, Unit = unit };

    [Fact]
    public void ThreePhaseDoAndExpandedCvalExposeVerifiedVectorDescriptors()
    {
        var obj = VectorModel();
        var root = IedBrowserStructuredDetails.DataObject(obj, "");
        Assert.Equal("MX", root.FunctionalConstraint);
        Assert.Equal(["phsA", "phsB", "phsC", "neut"],
            root.VectorComponents.Select(x => x.Label).ToArray());
        // Configuration companions (CF) must never disqualify the exact MX
        // process vectors nor be interpreted as raw process observations.
        Assert.All(root.VectorComponents, c => Assert.Equal("MX", c.Fc));
        var projection = new IedBrowserDetailProjection();
        projection.Reset(IedBrowserStructuredDetails.AttributeRows(obj.Attributes, obj.Name));
        var phase = Assert.Single(projection.Rows, r => r.Name == "phsC [MX]");
        Assert.Single(phase.VectorComponents);
        projection.Toggle(phase);
        var cval = Assert.Single(projection.Rows, r => r.Name == "cVal [MX]");
        Assert.Single(cval.VectorComponents);
        Assert.Equal("", cval.VectorComponents[0].Label);
        Assert.EndsWith(".cVal.mag.f", cval.VectorComponents[0].MagnitudeReference);
        Assert.EndsWith(".cVal.ang.f", cval.VectorComponents[0].AngleReference);
        Assert.Empty(Assert.Single(projection.Rows, r => r.Name == "q [MX]").VectorComponents);
    }

    [Fact]
    public void OnlyObservedPhaseAndProvenUnitAppear_NoFabricatedMagnitudeOrUnknownUnits()
    {
        var root = IedBrowserStructuredDetails.DataObject(VectorModel(), "");
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(association,
            Owner + ".phsC.cVal.mag.f", "MX", "0", "good", "T1", "Report"));
        Assert.True(values.PublishForAssociation(association,
            Owner + ".phsC.cVal.ang.f", "MX", "17", "good", "T1", "Report"));
        var units = Units(Unit(Owner + ".phsC", "kV"),
            Unit(Owner + ".phsC.cVal.ang.f", "°"));
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, root.VectorComponents, units, true, out var display));
        Assert.Equal("phsC: 0 kV ∠ 17°", display.Value);
        // An owner-level magnitude unit must never leak to angle.
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, root.VectorComponents, Units(Unit(Owner + ".phsC", "kV")),
            true, out var unqualifiedAngle));
        Assert.Equal("phsC: 0 kV ∠ 17°", unqualifiedAngle.Value);
        Assert.Equal("Report", display.Source);
        Assert.Contains(Owner + ".phsC.cVal.mag.f", display.Provenance);
        Assert.DoesNotContain("phsA:", display.Value);
        Assert.DoesNotContain("phsB:", display.Value);

        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, root.VectorComponents, Units(), true, out var raw));
        Assert.Equal("phsC: 0 ∠ 17°", raw.Value);
        Assert.DoesNotContain("kV", raw.Value);
        Assert.Contains("°", raw.Value);
    }

    [Fact]
    public void ParentIsNotReadUntilItsOwnExactLeafIsObserved()
    {
        var row = IedBrowserStructuredDetails.DataObject(VectorModel(), "");
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(association,
            Owner + ".phsC.cVal.mag.f", "ST", "99", "", "", "Report"));
        Assert.True(values.PublishForAssociation(association,
            "LD/MMXU1.phV.phsA.cVal.mag.f", "MX", "88", "", "", "Report"));
        Assert.False(IedBrowserObservedVectorPresentation.TryFormat(
            values, row.VectorComponents, Units(), true, out _));
        Assert.True(values.PublishForAssociation(association,
            Owner + ".phsA.cVal.mag.f", "MX", "4", "", "", "Report"));
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, row.VectorComponents, Units(), true, out var partial));
        Assert.Equal("phsA: 4", partial.Value);
    }

    [Fact]
    public void LateMmsReadCannotReplaceCanonicalReportedMagnitude()
    {
        var row = IedBrowserStructuredDetails.DataObject(VectorModel(), "");
        var values = new IedBrowserLiveValues();
        var session = values.BeginAssociation();
        var leaf = Owner + ".phsC.cVal.mag.f";
        Assert.True(values.PublishForAssociation(session, leaf, "MX",
            "22", "", "", "Report"));
        Assert.True(values.PublishForAssociation(session, leaf, "MX",
            "999", "", "", "MMS Read"));
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, row.VectorComponents, Units(), true, out var report));
        Assert.Equal("phsC: 22", report.Value);
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, row.VectorComponents, Units(), false, out var read));
        Assert.Equal("phsC: 999", read.Value);
        values.MarkOffline();
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, row.VectorComponents, Units(), true, out var stale));
        Assert.True(stale.IsStale);
        values.BeginAssociation();
        Assert.False(IedBrowserObservedVectorPresentation.TryFormat(
            values, row.VectorComponents, Units(), true, out _));
        Assert.False(values.PublishForAssociation(session, leaf, "MX",
            "9999", "", "", "Report"));
    }



    [Fact]
    public void NeutralAndExtraModelBranchesRemainBoundedAndObservedOnly()
    {
        var root = IedBrowserStructuredDetails.DataObject(VectorModel(), "");
        Assert.Equal(4, root.VectorComponents.Count);
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(association,
            Owner + ".neut.cVal.mag.f", "MX", "3", "", "", "Report"));
        var units = Units(Unit(Owner + ".neut", "kV"));
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, root.VectorComponents, units, true, out var projection));
        Assert.Equal("neut: 3 kV", projection.Value);
        Assert.DoesNotContain("phsA:", projection.Value);
    }

    [Fact]
    public void IncompatibleVectorStructureDoesNotPairAngleFromAnotherOwner()
    {
        var obj = new LiveIedDataObjectModel
        {
            Name = "PhV", Reference = Owner,
            Attributes =
            [
                Attribute("phsC.mag.f"),
                Attribute("phsC.cVal.ang.f")
            ]
        };
        var root = IedBrowserStructuredDetails.DataObject(obj, "MX");
        Assert.Empty(root.VectorComponents);
    }

    [Fact]
    public void UnitConflictsFailClosedAndNeverBorrowAnotherPhase()
    {
        var root = IedBrowserStructuredDetails.DataObject(VectorModel(), "");
        var values = new IedBrowserLiveValues();
        var a = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(a, Owner + ".phsC.cVal.mag.f",
            "MX", "13", "", "", "Report"));
        var units = Units(Unit(Owner + ".phsA", "kV"),
            Unit(Owner + ".phsC", "kV"), Unit(Owner + ".phsC", "V"));
        Assert.True(IedBrowserObservedVectorPresentation.TryFormat(
            values, root.VectorComponents, units, true, out var display));
        Assert.Equal("phsC: 13", display.Value);
    }

    [Fact]
    public void BrowserValueRefreshWiresModelProvenVectorProjection()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MainWindow.IedBrowser.cs")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "MainWindow.IedBrowser.cs"));
        Assert.Contains("IedBrowserEngineeringComposite.TryCompose(", source);
        Assert.Contains("row, device.LiveValues, engineeringUnits", source);
        Assert.DoesNotContain("if (IedBrowserObservedVectorPresentation.TryFormat(", source);
    }
}
