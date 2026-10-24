using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// M2.13J: configuration evidence must come from a real MMS Read or one
/// exact-model discovery snapshot. IEC units follow verified CF enum values,
/// never DO-name guesses or another phase/FC.
/// </summary>
public sealed class IedBrowserCfEvidenceM213JTests
{
    private const string Owner = "LD/MMXU1.PhV";
    private static LiveIedDataAttributeModel Attr(string phase, string tail,
        string fc = "CF")
        => new()
        {
            ObjectReference = Owner,
            FunctionalConstraint = fc,
            AttributePath = "PhV." + phase + "." + tail,
            MmsReference = "LD/MMXU1$" + fc + "$PhV$" + phase + "$" +
                tail.Replace('.', '$')
        };

    private static LiveIedModelDiscoveryDocument Model()
    {
        var obj = new LiveIedDataObjectModel
        {
            Name = "PhV", Reference = Owner,
            Attributes =
            [
                Attr("phsA", "cVal.mag.f", "MX"),
                Attr("phsA", "units.SIUnit"), Attr("phsA", "units.multiplier"),
                Attr("phsB", "cVal.mag.f", "MX"),
                Attr("phsB", "units.SIUnit"), Attr("phsB", "units.multiplier")
            ]
        };
        return new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    DataObjects = [obj]
                }]
            }]
        };
    }

    [Fact]
    public void ObservedSiUnitAndMultiplierProduceVerifiedMagnitudeUnit()
    {
        var model = Model();
        var ledger = new IedBrowserLiveValues();
        var session = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(session,
            Owner + ".phsA.units.SIUnit", "CF", "29", "", "", "MMS Read"));
        Assert.True(ledger.PublishForAssociation(session,
            Owner + ".phsA.units.multiplier", "CF", "3", "", "", "MMS Read"));
        var projection = new IedBrowserCfEvidencePresentation(ledger, model, null);
        var units = projection.BuildVerifiedUnits([], model);
        Assert.Equal("kV", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, Owner + ".phsA.cVal.mag.f", "MX"));
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, Owner + ".phsB.cVal.mag.f", "MX"));
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, Owner + ".phsA.cVal.mag.f", "ST"));
        Assert.Equal("V", IedBrowserCfEvidencePresentation.RenderCfValue(
            Owner + ".phsA.units.SIUnit", "CF", "29"));
        Assert.Equal("k", IedBrowserCfEvidencePresentation.RenderCfValue(
            Owner + ".phsA.units.multiplier", "CF", "3"));
        Assert.Equal("999", IedBrowserCfEvidencePresentation.RenderCfValue(
            Owner + ".phsA.units.SIUnit", "CF", "999"));
    }

    [Fact]
    public void SnapshotEvidenceIsModelBoundAndClearlyHistorical()
    {
        var model = Model();
        var canonical = new LiveIedCanonicalModel
        {
            Discovery = model,
            InstanceValues =
            [
                new LiveIedInstanceValueEvidence
                {
                    Domain = "LD", LogicalNode = "MMXU1", DataObject = "PhV",
                    AttributePath = "phsA.units.SIUnit", FunctionalConstraint = "CF",
                    Value = MmsDataValue.Integer(29)
                },
                new LiveIedInstanceValueEvidence
                {
                    Domain = "LD", LogicalNode = "MMXU1", DataObject = "PhV",
                    AttributePath = "phsA.units.multiplier", FunctionalConstraint = "CF",
                    Value = MmsDataValue.Integer(3)
                }
            ]
        };
        var ledger = new IedBrowserLiveValues();
        var evidence = new IedBrowserCfEvidencePresentation(ledger, model, canonical);
        Assert.True(evidence.TryGet(Owner + ".phsA.units.SIUnit", "CF",
            out var ordinal, out var source, out var live));
        Assert.Equal("29", ordinal);
        Assert.Contains("Discovery snapshot", source);
        Assert.False(live);
        Assert.Equal("kV", IedBrowserEngineeringValuePresentation.ResolveUnit(
            evidence.BuildVerifiedUnits([], model),
            Owner + ".phsA.cVal.mag.f", "MX"));

        // The same IED name is not enough for reuse after schema reload.
        var newer = Model();
        var superseded = new IedBrowserCfEvidencePresentation(ledger, newer, canonical);
        Assert.False(superseded.TryGet(Owner + ".phsA.units.SIUnit",
            "CF", out _, out _, out _));
    }

    [Fact]
    public void NoUnitWhenMultiplierOrMatchingOwnerNotObserved()
    {
        var model = Model();
        var ledger = new IedBrowserLiveValues();
        var assoc = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(assoc,
            Owner + ".phsA.units.SIUnit", "CF", "29", "", "", "MMS Read"));
        Assert.True(ledger.PublishForAssociation(assoc,
            Owner + ".phsB.units.multiplier", "CF", "3", "", "", "MMS Read"));
        var cf = new IedBrowserCfEvidencePresentation(ledger, model, null);
        var units = cf.BuildVerifiedUnits([], model);
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, Owner + ".phsA.cVal.mag.f", "MX"));
        Assert.Equal("", IedBrowserEngineeringValuePresentation.ResolveUnit(
            units, Owner + ".phsB.cVal.mag.f", "MX"));
    }

    [Fact]
    public void CasingAndFunctionalConstraintDoNotBorrowConfiguration()
    {
        var ledger = new IedBrowserLiveValues();
        var assoc = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(assoc,
            Owner + ".phsA.units.SIUnit", "ST", "29", "", "", "Report"));
        var cf = new IedBrowserCfEvidencePresentation(ledger, Model(), null);
        Assert.False(cf.TryGet(Owner + ".phsA.units.SIUnit", "CF",
            out _, out _, out _));
        Assert.False(cf.TryGet(Owner + ".phsa.units.SIUnit", "ST",
            out _, out _, out _));
    }



    [Fact]
    public void FreshExactCfUnitOutranksStaleSignalUnitButSnapshotConflictsFailClosed()
    {
        var model = Model();
        var signals = new[]
        {
            new SignalDefinition {
                ObjectReference = Owner + ".phsA",
                FunctionalConstraint = "MX", Unit = "V"
            }
        };
        var ledger = new IedBrowserLiveValues();
        var assoc = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(assoc,
            Owner + ".phsA.units.SIUnit", "CF", "29", "", "", "MMS Read"));
        Assert.True(ledger.PublishForAssociation(assoc,
            Owner + ".phsA.units.multiplier", "CF", "3", "", "", "MMS Read"));
        var display = new IedBrowserCfEvidencePresentation(ledger, model, null);
        Assert.Equal("kV", IedBrowserEngineeringValuePresentation.ResolveUnit(
            display.BuildVerifiedUnits(signals, model),
            Owner + ".phsA.cVal.mag.f", "MX"));
    }

    [Fact]
    public void UnitsParentRequiresBothExactObservedCfLeaves()
    {
        var model = Model();
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        var cf = new IedBrowserCfEvidencePresentation(ledger, model, null);
        Assert.False(cf.TryFormatUnitsParent(
            Owner + ".phsA.units", "CF", out _, out _));
        Assert.True(ledger.PublishForAssociation(association,
            Owner + ".phsA.units.SIUnit", "CF", "29", "", "", "MMS Read"));
        Assert.False(cf.TryFormatUnitsParent(
            Owner + ".phsA.units", "CF", out _, out _));
        Assert.True(ledger.PublishForAssociation(association,
            Owner + ".phsA.units.multiplier", "CF", "3", "", "", "MMS Read"));
        Assert.True(cf.TryFormatUnitsParent(
            Owner + ".phsA.units", "CF", out var unit, out var provenance));
        Assert.Equal("kV", unit);
        Assert.Contains("SIUnit", provenance);
        Assert.False(cf.TryFormatUnitsParent(
            Owner + ".phsB.units", "CF", out _, out _));
    }


    [Fact]
    public void SelectedLogicalNodeExplicitReadIncludesCfAndMxLeafs()
    {
        var model = Model();
        var ln = model.LogicalDevices[0].LogicalNodes[0];
        var page = new IedBrowserTreeRow("model/ln/LD/MMXU1",
            "MMXU1", "LN", 2, ln);
        var targets = IedBrowserReadPlan.Build(model, page, readAll: false);
        Assert.Contains(targets, t =>
            IedBrowserLiveValues.CanonicalReference(t.Reference) ==
                Owner + ".phsA.units.SIUnit" && t.FunctionalConstraint == "CF");
        Assert.Contains(targets, t =>
            IedBrowserLiveValues.CanonicalReference(t.Reference) ==
                Owner + ".phsA.units.multiplier" && t.FunctionalConstraint == "CF");
        Assert.Contains(targets, t =>
            IedBrowserLiveValues.CanonicalReference(t.Reference) ==
                Owner + ".phsA.cVal.mag.f" && t.FunctionalConstraint == "MX");
        Assert.Equal(6, targets.Count);
    }

    [Fact]
    public void BrowserRefreshBindsCfEvidenceWithoutImplicitMmsRequests()
    {
        DirectoryInfo? cursor = new(AppContext.BaseDirectory);
        while (cursor != null &&
               !File.Exists(Path.Combine(cursor.FullName, "MainWindow.IedBrowser.cs")))
            cursor = cursor.Parent;
        Assert.NotNull(cursor);
        var source = File.ReadAllText(
            Path.Combine(cursor!.FullName, "MainWindow.IedBrowser.cs"));
        // The semantic bridge must still be used, but no longer rescan all
        // SG7 signals directly on every WPF value refresh.
        Assert.Contains("_iedBrowserUnitCache.Resolve(device, model, cfEvidence)", source);
        var cache = File.ReadAllText(Path.Combine(cursor.FullName,
            "Models", "IedBrowserUnitProjectionCache.cs"));
        Assert.Contains("evidence.BuildVerifiedUnits(device.Signals, model)", cache);
        Assert.Contains("device.LiveValues.ConfigurationVersion", cache);
        Assert.Contains("SignalDefinition.UnitMetadataRevision", cache);
        Assert.Contains("IedBrowserCfEvidencePresentation.RenderCfValue(", source);
        Assert.Contains("Discovery snapshot", source);
    }
}
